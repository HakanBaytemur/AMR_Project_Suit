using System.Collections.Concurrent;
using System.Numerics;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Objects;
using ACadSharp.Objects.Evaluations;
using ACadSharp.Tables;
using DwgTrueView.Core;

namespace DwgTrueView.Cad;

public sealed record CadReadOptions
{
    public int MaxDegreeOfParallelism { get; init; } = Environment.ProcessorCount;
}

/// <summary>
/// Display-only CAD reader. It tessellates supported 2D entities into one
/// contiguous line-list GPU buffer. Invisible entities, frozen/off layers, and
/// dynamic-block visibility masters stay out of the drawn set. XData,
/// constraints, actions, dimension internals, and application XRecords are
/// never walked.
/// </summary>
public sealed class ShallowCadReader
{
    public Task<PackedCadDrawing> ReadAsync(
        string path,
        CadReadOptions? options = null,
        IProgress<CadLoadProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => Read(path, options, progress, cancellationToken),
            cancellationToken);

    public PackedCadDrawing Read(
        string path,
        CadReadOptions? options = null,
        IProgress<CadLoadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        options ??= new CadReadOptions();
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new CadLoadProgress(2, "Reading DWG/DXF"));

        CadDocument document = ReadDocument(path);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new CadLoadProgress(25, "Indexing layers and blocks"));

        Layer[] sourceLayers = document.Layers.ToArray();
        LayerState[] layers = BuildLayers(sourceLayers);
        Dictionary<string, int> layerIds = layers.ToDictionary(
            static layer => layer.Name,
            static layer => layer.Id,
            StringComparer.OrdinalIgnoreCase);
        int zeroLayerId = layerIds.TryGetValue("0", out int zero) ? zero : 0;

        BlockRecord[] sourceBlocks = document.BlockRecords
            .Where(static block => !IsLayoutBlock(block.Name))
            .ToArray();
        Dictionary<string, int> blockIds = sourceBlocks
            .Select(static (block, index) => (block.Name, index))
            .ToDictionary(
                static item => item.Name,
                static item => item.index,
                StringComparer.OrdinalIgnoreCase);
        HashSet<int> visibilityMasters = sourceBlocks
            .Where(static block => !block.IsAnonymous && HasVisibilityController(block))
            .Select(block => blockIds[block.Name])
            .ToHashSet();
        Dictionary<int, int> anonymousFallbacks = CreateAnonymousFallbacks(
            sourceBlocks,
            blockIds);
        var templates = new BlockTemplate[sourceBlocks.Length];
        var parallel = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, options.MaxDegreeOfParallelism),
            CancellationToken = cancellationToken,
        };

        Parallel.For(0, sourceBlocks.Length, parallel, index =>
        {
            templates[index] = visibilityMasters.Contains(index)
                ? BlockTemplate.Empty
                : BuildBlockTemplate(
                    sourceBlocks[index],
                    blockIds,
                    sourceBlocks,
                    visibilityMasters,
                    anonymousFallbacks,
                    layerIds);
        });

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new CadLoadProgress(45, "Flattening display geometry"));
        Entity[] modelEntities = document.Entities.ToArray();
        int processed = 0;
        var partitions = new ConcurrentBag<ExtractionPartition>();
        Parallel.ForEach(
            modelEntities,
            parallel,
            static () => new ExtractionPartition(),
            (entity, _, partition) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                int layerId = ResolveLayerId(entity, layerIds);
                if (entity.IsInvisible)
                {
                    partition.Skipped++;
                }
                else if (entity is Insert insert)
                {
                    FlattenInsert(
                        insert,
                        layerId,
                        Matrix4x4.Identity,
                        CadColorValue.Rgb(CadColorResolver.ForegroundArgb & 0xFFFFFF),
                        sourceBlocks,
                        blockIds,
                        templates,
                        visibilityMasters,
                        anonymousFallbacks,
                        layers,
                        zeroLayerId,
                        partition,
                        cancellationToken);
                }
                else
                {
                    partition.Scratch.Clear();
                    if (DisplayGeometryExtractor.Append(
                            entity,
                            layerId,
                            partition.Scratch))
                    {
                        foreach (LocalSegment segment in partition.Scratch)
                        {
                            AppendResolvedSegment(
                                segment,
                                Matrix4x4.Identity,
                                layerId,
                                layerId,
                                CadColorValue.Rgb(
                                    CadColorResolver.ForegroundArgb & 0xFFFFFF),
                                layers,
                                zeroLayerId,
                                partition);
                        }
                    }
                    else
                    {
                        partition.Skipped++;
                    }
                }

                int current = Interlocked.Increment(ref processed);
                if ((current & 0x1FFF) == 0)
                {
                    progress?.Report(new CadLoadProgress(
                        45 + 45 * current / Math.Max(1, modelEntities.Length),
                        "Flattening display geometry",
                        current,
                        modelEntities.Length));
                }
                return partition;
            },
            partitions.Add);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new CadLoadProgress(92, "Packing GPU vertex buffer"));
        PackedCadDrawing result = Pack(
            path,
            layers,
            partitions,
            modelEntities.Length,
            templates.Sum(static template => template.Skipped),
            MetersPerDrawingUnit((int)document.Header.InsUnits));
        progress?.Report(new CadLoadProgress(
            100,
            "Ready",
            modelEntities.Length,
            modelEntities.Length));
        return result;
    }

    private static CadDocument ReadDocument(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".dwg", StringComparison.OrdinalIgnoreCase)
            ? DwgReader.Read(path)
            : extension.Equals(".dxf", StringComparison.OrdinalIgnoreCase)
                ? DxfReader.Read(path)
                : throw new NotSupportedException("Only DWG and DXF files are supported.");
    }

    private static LayerState[] BuildLayers(IReadOnlyList<Layer> source)
    {
        var result = new LayerState[source.Count];
        for (int index = 0; index < source.Count; index++)
        {
            Layer layer = source[index];
            CadColorValue color = CadColorResolver.FromCadColor(layer.Color);
            result[index] = new LayerState(
                index,
                layer.Name,
                color,
                CadColorResolver.Resolve(
                    color,
                    CadColorValue.Aci(7),
                    CadColorValue.Aci(7)),
                layer.IsOn && (layer.Flags & LayerFlags.Frozen) == 0);
        }
        return result;
    }

    private static BlockTemplate BuildBlockTemplate(
        BlockRecord record,
        IReadOnlyDictionary<string, int> blockIds,
        IReadOnlyList<BlockRecord> blocks,
        IReadOnlySet<int> visibilityMasters,
        IReadOnlyDictionary<int, int> anonymousFallbacks,
        IReadOnlyDictionary<string, int> layerIds)
    {
        var segments = new List<LocalSegment>();
        var nested = new List<NestedInsert>();
        int skipped = 0;
        foreach (Entity entity in record.Entities)
        {
            if (entity.IsInvisible)
            {
                skipped++;
                continue;
            }
            int layerId = ResolveLayerId(entity, layerIds);
            if (entity is Insert insert)
            {
                if (!TryResolveBlock(
                        insert,
                        blockIds,
                        blocks,
                        visibilityMasters,
                        anonymousFallbacks,
                        out int blockId,
                        out BlockRecord? resolved))
                {
                    skipped++;
                    continue;
                }
                AppendNestedInstances(insert, blockId, resolved!, layerId, nested);
            }
            else if (!DisplayGeometryExtractor.Append(entity, layerId, segments))
            {
                skipped++;
            }
        }
        return new BlockTemplate(segments.ToArray(), nested.ToArray(), skipped);
    }

    private static void FlattenInsert(
        Insert insert,
        int layerId,
        Matrix4x4 parentTransform,
        CadColorValue parentBlockColor,
        IReadOnlyList<BlockRecord> sourceBlocks,
        IReadOnlyDictionary<string, int> blockIds,
        IReadOnlyList<BlockTemplate> templates,
        IReadOnlySet<int> visibilityMasters,
        IReadOnlyDictionary<int, int> anonymousFallbacks,
        IReadOnlyList<LayerState> layers,
        int zeroLayerId,
        ExtractionPartition destination,
        CancellationToken cancellationToken)
    {
        if (!TryResolveBlock(
                insert,
                blockIds,
                sourceBlocks,
                visibilityMasters,
                anonymousFallbacks,
                out int blockId,
                out BlockRecord? resolved))
        {
            destination.Skipped++;
            return;
        }

        CadColorValue insertColor = CadColorResolver.FromCadColor(insert.Color);
        LayerState layer = LayerAt(layerId, layers);
        int blockArgb = CadColorResolver.Resolve(insertColor, layer.Color, parentBlockColor);
        CadColorValue blockColor = CadColorValue.Rgb(blockArgb & 0xFFFFFF);
        Vector3 basePoint = resolved!.BlockEntity is null
            ? Vector3.Zero
            : CadMath.ToVector(resolved.BlockEntity.BasePoint);
        int rows = Math.Max(1, (int)insert.RowCount);
        int columns = Math.Max(1, (int)insert.ColumnCount);
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                Matrix4x4 transform = CadMath.InsertTransform(
                        insert,
                        basePoint,
                        new Vector2(
                            column * (float)insert.ColumnSpacing,
                            row * (float)insert.RowSpacing))
                    * parentTransform;
                FlattenTemplate(
                    blockId,
                    layerId,
                    layerId,
                    blockColor,
                    transform,
                    templates,
                    layers,
                    zeroLayerId,
                    destination,
                    new HashSet<int>(),
                    cancellationToken);
            }
        }
    }

    private static void FlattenTemplate(
        int blockId,
        int parentLayerId,
        int gateLayerId,
        CadColorValue blockColor,
        Matrix4x4 transform,
        IReadOnlyList<BlockTemplate> templates,
        IReadOnlyList<LayerState> layers,
        int zeroLayerId,
        ExtractionPartition destination,
        HashSet<int> stack,
        CancellationToken cancellationToken)
    {
        if ((uint)blockId >= (uint)templates.Count
            || stack.Count >= 64
            || !stack.Add(blockId))
        {
            destination.Skipped++;
            return;
        }
        cancellationToken.ThrowIfCancellationRequested();
        BlockTemplate template = templates[blockId];
        foreach (LocalSegment segment in template.Segments)
        {
            AppendResolvedSegment(
                segment,
                transform,
                parentLayerId,
                gateLayerId,
                blockColor,
                layers,
                zeroLayerId,
                destination);
        }
        foreach (NestedInsert nested in template.Nested)
        {
            int layerId = nested.LayerId == zeroLayerId
                ? parentLayerId
                : nested.LayerId;
            LayerState layer = LayerAt(layerId, layers);
            int nestedArgb = CadColorResolver.Resolve(
                nested.Color,
                layer.Color,
                blockColor);
            FlattenTemplate(
                nested.BlockId,
                layerId,
                gateLayerId,
                CadColorValue.Rgb(nestedArgb & 0xFFFFFF),
                nested.Transform * transform,
                templates,
                layers,
                zeroLayerId,
                destination,
                stack,
                cancellationToken);
        }
        stack.Remove(blockId);
    }

    private static void AppendResolvedSegment(
        LocalSegment segment,
        Matrix4x4 transform,
        int parentLayerId,
        int gateLayerId,
        CadColorValue blockColor,
        IReadOnlyList<LayerState> layers,
        int zeroLayerId,
        ExtractionPartition destination)
    {
        int layerId = segment.LayerId == zeroLayerId
            ? parentLayerId
            : segment.LayerId;
        LayerState layer = LayerAt(layerId, layers);
        int argb = CadColorResolver.Resolve(segment.Color, layer.Color, blockColor);
        uint rgba = CadVertex.FromArgb(argb);
        Vector3 start3 = Vector3.Transform(segment.Start, transform);
        Vector3 end3 = Vector3.Transform(segment.End, transform);
        Vector2 start = new(start3.X, start3.Y);
        Vector2 end = new(end3.X, end3.Y);
        if (!IsFinite(start) || !IsFinite(end) || start == end)
        {
            destination.Skipped++;
            return;
        }
        destination.Add(
            layerId,
            gateLayerId,
            new CadVertex(start.X, start.Y, rgba),
            new CadVertex(end.X, end.Y, rgba));
    }

    private static void AppendNestedInstances(
        Insert insert,
        int blockId,
        BlockRecord block,
        int layerId,
        List<NestedInsert> destination)
    {
        Vector3 basePoint = block.BlockEntity is null
            ? Vector3.Zero
            : CadMath.ToVector(block.BlockEntity.BasePoint);
        int rows = Math.Max(1, (int)insert.RowCount);
        int columns = Math.Max(1, (int)insert.ColumnCount);
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                destination.Add(new NestedInsert(
                    blockId,
                    layerId,
                    CadColorResolver.FromCadColor(insert.Color),
                    CadMath.InsertTransform(
                        insert,
                        basePoint,
                        new Vector2(
                            column * (float)insert.ColumnSpacing,
                            row * (float)insert.RowSpacing))));
            }
        }
    }

    private static bool TryResolveBlock(
        Insert insert,
        IReadOnlyDictionary<string, int> blockIds,
        IReadOnlyList<BlockRecord> blocks,
        IReadOnlySet<int> visibilityMasters,
        IReadOnlyDictionary<int, int> anonymousFallbacks,
        out int blockId,
        out BlockRecord? block)
    {
        block = GetRepresentationBlock(insert) ?? insert.Block;
        if (block is null || !blockIds.TryGetValue(block.Name, out blockId))
        {
            blockId = -1;
            return false;
        }
        if (visibilityMasters.Contains(blockId))
        {
            if (!anonymousFallbacks.TryGetValue(blockId, out blockId))
            {
                block = null;
                return false;
            }
            block = blocks[blockId];
        }
        return true;
    }

    private static BlockRecord? GetRepresentationBlock(Insert insert)
    {
        BlockRepresentationData? representation = insert.XDictionary?
            .GetEntry<BlockRepresentationData>(
                DxfFileToken.ObjectBlockRepresentationData);
        BlockRecord? block = representation?.Block;
        return block is not null
            && block.Name.StartsWith("*U", StringComparison.OrdinalIgnoreCase)
            ? block
            : null;
    }

    private static Dictionary<int, int> CreateAnonymousFallbacks(
        IReadOnlyList<BlockRecord> blocks,
        IReadOnlyDictionary<string, int> blockIds) =>
        blocks
            .Where(static block => block.IsAnonymous && block.Source is not null)
            .GroupBy(static block => block.Source!.Name, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() == 1)
            .Where(group => blockIds.ContainsKey(group.Key))
            .ToDictionary(
                group => blockIds[group.Key],
                group => blockIds[group.Single().Name]);

    private static PackedCadDrawing Pack(
        string path,
        IReadOnlyList<LayerState> layers,
        IEnumerable<ExtractionPartition> partitions,
        int sourceEntityCount,
        int templateSkipped,
        double metersPerUnit)
    {
        ExtractionPartition[] sources = partitions.ToArray();
        var counts = new Dictionary<LayerRangeKey, int>();
        CadBounds2 bounds = CadBounds2.Empty;
        int skipped = templateSkipped;
        foreach (ExtractionPartition partition in sources)
        {
            skipped += partition.Skipped;
            bounds = bounds.IsEmpty
                ? partition.Bounds
                : partition.Bounds.IsEmpty
                    ? bounds
                    : new CadBounds2(
                        Vector2.Min(bounds.Minimum, partition.Bounds.Minimum),
                        Vector2.Max(bounds.Maximum, partition.Bounds.Maximum));
            foreach ((LayerRangeKey key, List<CadVertex> layerVertices) in partition.ByRange)
            {
                counts[key] = counts.GetValueOrDefault(key) + layerVertices.Count;
            }
        }

        LayerRangeKey[] orderedKeys = counts.Keys
            .OrderBy(static key => key.LayerId)
            .ThenBy(static key => key.GateLayerId)
            .ToArray();
        var vertices = new CadVertex[counts.Values.Sum()];
        var drawRanges = new CadDrawRange[orderedKeys.Length];
        int offset = 0;
        for (int rangeIndex = 0; rangeIndex < orderedKeys.Length; rangeIndex++)
        {
            LayerRangeKey key = orderedKeys[rangeIndex];
            int start = offset;
            foreach (ExtractionPartition partition in sources)
            {
                if (!partition.ByRange.TryGetValue(key, out List<CadVertex>? source))
                {
                    continue;
                }
                source.CopyTo(vertices, offset);
                offset += source.Count;
            }
            drawRanges[rangeIndex] = new CadDrawRange(
                start,
                offset - start,
                key.LayerId,
                key.GateLayerId);
        }
        var outputLayers = new CadLayer[layers.Count];
        for (int layerId = 0; layerId < layers.Count; layerId++)
        {
            LayerState layer = layers[layerId];
            outputLayers[layerId] = new CadLayer
            {
                Id = layer.Id,
                Name = layer.Name,
                ColorArgb = layer.Argb,
                IsInitiallyVisible = layer.IsVisible,
            };
        }

        return new PackedCadDrawing(
            path,
            vertices,
            outputLayers,
            drawRanges,
            bounds,
            metersPerUnit,
            sourceEntityCount,
            skipped);
    }

    private static int ResolveLayerId(
        Entity entity,
        IReadOnlyDictionary<string, int> layerIds)
    {
        string name = entity.Layer?.Name ?? "0";
        return layerIds.TryGetValue(name, out int id) ? id : 0;
    }

    private static LayerState LayerAt(
        int layerId,
        IReadOnlyList<LayerState> layers) =>
        (uint)layerId < (uint)layers.Count
            ? layers[layerId]
            : new LayerState(
                0,
                "0",
                CadColorValue.Aci(7),
                CadColorResolver.ForegroundArgb,
                true);

    private static bool HasVisibilityController(BlockRecord block) =>
        block.EvaluationGraph?.Nodes.Any(
            static node => node.Expression is BlockVisibilityParameter) == true;

    private static bool IsLayoutBlock(string name) =>
        name.Contains("MODEL_SPACE", StringComparison.OrdinalIgnoreCase)
        || name.Contains("PAPER_SPACE", StringComparison.OrdinalIgnoreCase);

    private static bool IsFinite(Vector2 point) =>
        float.IsFinite(point.X) && float.IsFinite(point.Y);

    private static double MetersPerDrawingUnit(int unit) => unit switch
    {
        1 => 0.0254,
        2 => 0.3048,
        3 => 1609.344,
        4 => 0.001,
        5 => 0.01,
        6 => 1,
        7 => 1000,
        10 => 0.9144,
        14 => 0.1,
        15 => 10,
        16 => 100,
        21 => 1200d / 3937d,
        _ => 1,
    };

    private sealed class ExtractionPartition
    {
        public Dictionary<LayerRangeKey, List<CadVertex>> ByRange { get; } = [];
        public List<LocalSegment> Scratch { get; } = [];
        public CadBounds2 Bounds { get; private set; } = CadBounds2.Empty;
        public int Skipped { get; set; }

        public void Add(
            int layerId,
            int gateLayerId,
            CadVertex start,
            CadVertex end)
        {
            var key = new LayerRangeKey(layerId, gateLayerId);
            if (!ByRange.TryGetValue(key, out List<CadVertex>? vertices))
            {
                vertices = [];
                ByRange[key] = vertices;
            }
            vertices.Add(start);
            vertices.Add(end);
            Bounds = Bounds.Include(start.Position).Include(end.Position);
        }
    }

    private sealed record BlockTemplate(
        LocalSegment[] Segments,
        NestedInsert[] Nested,
        int Skipped)
    {
        public static BlockTemplate Empty { get; } = new([], [], 0);
    }

    private readonly record struct NestedInsert(
        int BlockId,
        int LayerId,
        CadColorValue Color,
        Matrix4x4 Transform);

    private readonly record struct LayerState(
        int Id,
        string Name,
        CadColorValue Color,
        int Argb,
        bool IsVisible);

    private readonly record struct LayerRangeKey(int LayerId, int GateLayerId);
}
