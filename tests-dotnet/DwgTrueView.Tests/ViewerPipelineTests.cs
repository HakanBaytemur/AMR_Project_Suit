using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using ACadSharp.Types.Units;
using CSMath;
using DwgTrueView.Cad;
using DwgTrueView.Core;

namespace DwgTrueView.Tests;

public sealed class ViewerPipelineTests
{
    [Fact]
    public void VertexPayloadIsTwelveByteBlittableData()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<CadVertex>());
        Assert.Equal(CadVertex.SizeInBytes, Marshal.SizeOf<CadVertex>());
        Assert.Equal(12, Marshal.SizeOf<CadVertex>());
    }

    [Fact]
    public void ZoomKeepsMouseWorldAnchorStable()
    {
        var camera = new ViewCamera2D();
        var viewport = new Vector2(1000, 800);
        var cursor = new Vector2(750, 275);
        Vector2 before = camera.ScreenToWorld(cursor, viewport);

        camera.ZoomAt(cursor, viewport, 0.25f);

        Vector2 after = camera.ScreenToWorld(cursor, viewport);
        Assert.Equal(before.X, after.X, precision: 5);
        Assert.Equal(before.Y, after.Y, precision: 5);
    }

    [Fact]
    public void PanAndFitUseCadWorldCoordinates()
    {
        var camera = new ViewCamera2D();
        camera.Fit(
            new CadBounds2(new Vector2(-50, -25), new Vector2(50, 25)),
            new Vector2(1000, 500),
            margin: 0);
        Assert.Equal(0.1f, camera.UnitsPerPixel, precision: 5);
        Assert.Equal(Vector2.Zero, camera.Center);

        camera.PanPixels(new Vector2(100, -50));

        Assert.Equal(-10f, camera.Center.X, precision: 5);
        Assert.Equal(-5f, camera.Center.Y, precision: 5);
    }

    [Fact]
    public void FitScreenRectZoomsToTheDraggedWindow()
    {
        var camera = new ViewCamera2D();
        var viewport = new Vector2(200, 100);
        camera.FitScreenRect(new Vector2(50, 25), new Vector2(150, 75), viewport, margin: 0);
        Assert.Equal(0.5f, camera.UnitsPerPixel, precision: 5);
        Assert.Equal(Vector2.Zero, camera.Center);
    }

    [Fact]
    public void RestorePutsTheCameraBackToAPreviousView()
    {
        var camera = new ViewCamera2D();
        camera.Fit(
            new CadBounds2(new Vector2(-50, -25), new Vector2(50, 25)),
            new Vector2(1000, 500),
            margin: 0);
        Vector2 center = camera.Center;
        float scale = camera.UnitsPerPixel;
        camera.PanPixels(new Vector2(80, 40));
        camera.Restore(center, scale);
        Assert.Equal(center, camera.Center);
        Assert.Equal(scale, camera.UnitsPerPixel);
        Assert.True(camera.Matches(center, scale));
    }

    [Fact]
    public void SuppliedDxfBecomesOneContiguousLayerSortedBuffer()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "Scenarios",
            "06_l_shape.dxf");
        PackedCadDrawing drawing = new ShallowCadReader().Read(path);

        Assert.True(drawing.SegmentCount > 0);
        Assert.Equal(0, drawing.Vertices.Length & 1);
        int nextStart = 0;
        foreach (CadDrawRange range in drawing.DrawRanges.Span)
        {
            Assert.Equal(nextStart, range.StartVertex);
            Assert.Equal(0, range.VertexCount & 1);
            nextStart += range.VertexCount;
        }
        Assert.Equal(drawing.Vertices.Length, nextStart);
        Assert.False(drawing.Bounds.IsEmpty);
    }

    [Fact]
    public void ReaderFlattensNestedMInsertAndResolvesDisplayColors()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.dxf");
        try
        {
            var document = new CadDocument();
            document.Header.InsUnits = UnitsType.Millimeters;
            var redLayer = new Layer("RED") { Color = new ACadSharp.Color((short)1) };
            var offLayer = new Layer("OFF") { IsOn = false };
            document.Layers.Add(redLayer);
            document.Layers.Add(offLayer);

            document.Entities.Add(new ACadSharp.Entities.Line(
                new XYZ(0, 0, 0),
                new XYZ(1, 0, 0))
            {
                Color = new ACadSharp.Color(0x12, 0x34, 0x56),
            });
            document.Entities.Add(new ACadSharp.Entities.Line(
                new XYZ(0, 1, 0),
                new XYZ(1, 1, 0))
            {
                Layer = redLayer,
                Color = ACadSharp.Color.ByLayer,
            });
            document.Entities.Add(new ACadSharp.Entities.Line(
                new XYZ(0, 2, 0),
                new XYZ(1, 2, 0))
            {
                IsInvisible = true,
            });
            document.Entities.Add(new ACadSharp.Entities.Line(
                new XYZ(0, 3, 0),
                new XYZ(1, 3, 0))
            {
                Layer = offLayer,
            });
            document.Entities.Add(new ACadSharp.Entities.Point(new XYZ(3, 3, 0)));

            var inner = new BlockRecord("INNER");
            inner.Entities.Add(new ACadSharp.Entities.Line(
                new XYZ(0, 0, 0),
                new XYZ(4, 0, 0))
            {
                Color = ACadSharp.Color.ByBlock,
            });
            var outer = new BlockRecord("OUTER");
            outer.Entities.Add(new Insert(inner)
            {
                InsertPoint = new XYZ(1, 0, 0),
                Color = ACadSharp.Color.ByBlock,
            });
            document.BlockRecords.Add(inner);
            document.BlockRecords.Add(outer);
            document.Entities.Add(new Insert(outer)
            {
                InsertPoint = new XYZ(10, 20, 0),
                RowCount = 2,
                ColumnCount = 3,
                RowSpacing = 4,
                ColumnSpacing = 5,
                Color = new ACadSharp.Color((short)3),
            });

            using (var writer = new DxfWriter(path, document))
            {
                writer.Write();
            }
            var values = new List<CadLoadProgress>();
            PackedCadDrawing drawing = new ShallowCadReader().Read(
                path,
                new CadReadOptions { MaxDegreeOfParallelism = 4 },
                new InlineProgress<CadLoadProgress>(values.Add));

            Assert.Equal(0.001d, drawing.MetersPerDrawingUnit);
            Assert.Equal(9, drawing.SegmentCount);
            Assert.Equal(2, drawing.SkippedEntityCount);
            Assert.False(drawing.Layers.Span
                .ToArray()
                .Single(layer => layer.Name == "OFF")
                .IsInitiallyVisible);
            Assert.Equal(100, values[^1].Percent);

            ReadOnlySpan<CadVertex> vertices = drawing.Vertices.Span;
            uint trueColor = CadVertex.FromArgb(unchecked((int)0xFF123456));
            Assert.Contains(vertices.ToArray(), vertex =>
                vertex.Y == 0 && vertex.ColorRgba == trueColor);
            uint red = CadVertex.FromArgb(unchecked((int)0xFFFF0000));
            Assert.Contains(vertices.ToArray(), vertex =>
                vertex.Y == 1 && vertex.ColorRgba == red);
            uint green = CadVertex.FromArgb(unchecked((int)0xFF00FF00));
            Assert.Equal(
                12,
                vertices.ToArray().Count(vertex => vertex.ColorRgba == green));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReaderHonorsPreCancelledToken()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(
            () => new ShallowCadReader().Read(
                "unused.dxf",
                cancellationToken: cancellation.Token));
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
