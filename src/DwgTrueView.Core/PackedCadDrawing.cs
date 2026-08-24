using System.Numerics;
using System.Runtime.InteropServices;

namespace DwgTrueView.Core;

/// <summary>
/// GPU-ready line-list vertex. Two adjacent vertices form one CAD segment.
/// Color bytes are packed as RGBA for DXGI_FORMAT_R8G8B8A8_UNORM.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly record struct CadVertex(float X, float Y, uint ColorRgba)
{
    public const int SizeInBytes = 12;

    public Vector2 Position => new(X, Y);

    public static uint FromArgb(int argb) =>
        (uint)((argb >> 16) & 0xFF)
        | (uint)((argb >> 8) & 0xFF) << 8
        | (uint)(argb & 0xFF) << 16
        | (uint)((argb >> 24) & 0xFF) << 24;
}

public readonly record struct CadBounds2(Vector2 Minimum, Vector2 Maximum)
{
    public static CadBounds2 Empty { get; } = new(
        new Vector2(float.PositiveInfinity),
        new Vector2(float.NegativeInfinity));

    public bool IsEmpty =>
        !float.IsFinite(Minimum.X)
        || !float.IsFinite(Minimum.Y)
        || !float.IsFinite(Maximum.X)
        || !float.IsFinite(Maximum.Y);

    public Vector2 Center => IsEmpty ? Vector2.Zero : (Minimum + Maximum) * 0.5f;
    public Vector2 Size => IsEmpty ? Vector2.Zero : Maximum - Minimum;

    public CadBounds2 Include(Vector2 point) =>
        IsEmpty
            ? new CadBounds2(point, point)
            : new CadBounds2(Vector2.Min(Minimum, point), Vector2.Max(Maximum, point));
}

public sealed class CadLayer
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required int ColorArgb { get; init; }
    public bool IsInitiallyVisible { get; init; } = true;
}

public readonly record struct CadDrawRange(
    int StartVertex,
    int VertexCount,
    int LayerId,
    int GateLayerId);

public sealed class PackedCadDrawing
{
    private readonly CadVertex[] _vertices;
    private readonly CadLayer[] _layers;
    private readonly CadDrawRange[] _drawRanges;

    public PackedCadDrawing(
        string sourcePath,
        CadVertex[] vertices,
        CadLayer[] layers,
        CadDrawRange[] drawRanges,
        CadBounds2 bounds,
        double metersPerDrawingUnit,
        int sourceEntityCount,
        int skippedEntityCount,
        CadVertex[]? fillVertices = null,
        CadDrawRange[]? fillDrawRanges = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(drawRanges);
        fillVertices ??= [];
        fillDrawRanges ??= [];
        if ((vertices.Length & 1) != 0)
        {
            throw new ArgumentException(
                "A line-list buffer must contain an even number of vertices.",
                nameof(vertices));
        }
        if (fillVertices.Length % 3 != 0)
        {
            throw new ArgumentException(
                "A triangle-list buffer must contain a multiple of three vertices.",
                nameof(fillVertices));
        }

        SourcePath = sourcePath;
        _vertices = vertices;
        _layers = layers;
        _drawRanges = drawRanges;
        _fillVertices = fillVertices;
        _fillDrawRanges = fillDrawRanges;
        Bounds = bounds;
        MetersPerDrawingUnit = metersPerDrawingUnit;
        SourceEntityCount = sourceEntityCount;
        SkippedEntityCount = skippedEntityCount;
    }

    private readonly CadVertex[] _fillVertices;
    private readonly CadDrawRange[] _fillDrawRanges;

    public string SourcePath { get; }
    public ReadOnlyMemory<CadVertex> Vertices => _vertices;
    public ReadOnlyMemory<CadLayer> Layers => _layers;
    public ReadOnlyMemory<CadDrawRange> DrawRanges => _drawRanges;
    public ReadOnlyMemory<CadVertex> FillVertices => _fillVertices;
    public ReadOnlyMemory<CadDrawRange> FillDrawRanges => _fillDrawRanges;
    public CadBounds2 Bounds { get; }
    public double MetersPerDrawingUnit { get; }
    public int SourceEntityCount { get; }
    public int SkippedEntityCount { get; }
    public int SegmentCount => _vertices.Length / 2;
    public long VertexBytes => (long)_vertices.Length * CadVertex.SizeInBytes;
}

public readonly record struct CadLoadProgress(
    int Percent,
    string Stage,
    int ProcessedEntities = 0,
    int TotalEntities = 0);
