using System.Numerics;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using CSMath;
using DwgTrueView.Cad;
using DwgTrueView.Core;

namespace DwgTrueView.Tests;

public sealed class CoordinateTransformTests
{
    [Fact]
    public void ArbitraryAxisMapsOcsCircleAwayFromRawXy()
    {
        var circle = new Circle
        {
            Center = new XYZ(10, 5, 0),
            Radius = 2,
            Normal = new XYZ(0, 0, -1),
        };
        var segments = new List<LocalSegment>();
        Assert.True(DisplayGeometryExtractor.Append(circle, 0, segments));
        Assert.NotEmpty(segments);
        foreach (LocalSegment segment in segments)
        {
            Assert.InRange(segment.Start.X, -12.1f, -7.9f);
            Assert.InRange(segment.Start.Y, 2.9f, 7.1f);
            Assert.False(segment.Start.X > 0);
        }
    }

    [Fact]
    public void InvalidPolylineVertexDoesNotStretchToOrigin()
    {
        var polyline = new LwPolyline
        {
            Vertices =
            {
                new LwPolyline.Vertex(new XY(20, 10)),
                new LwPolyline.Vertex(new XY(double.NaN, 10)),
                new LwPolyline.Vertex(new XY(30, 12)),
            },
        };
        var segments = new List<LocalSegment>();
        Assert.True(DisplayGeometryExtractor.Append(polyline, 0, segments));
        Assert.Empty(segments);
    }

    [Fact]
    public void TextIgnoresMissingAlignmentPointAtOrigin()
    {
        var text = new TextEntity
        {
            Value = "STA",
            Height = 2,
            InsertPoint = new XYZ(40, 15, 0),
            AlignmentPoint = new XYZ(0, 0, 0),
            HorizontalAlignment = TextHorizontalAlignment.Center,
        };
        var segments = new List<LocalSegment>();
        Assert.True(DisplayGeometryExtractor.Append(text, 0, segments));
        Assert.NotEmpty(segments);
        Assert.DoesNotContain(
            segments,
            segment =>
                MathF.Abs(segment.Start.X) < 1
                && MathF.Abs(segment.Start.Y) < 1);
        Assert.Contains(
            segments,
            segment => MathF.Abs(segment.Start.X - 40) < 8);
    }

    [Fact]
    public void InsertSubtractsBlockBasePointBeforePlacement()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.dxf");
        try
        {
            var document = new CadDocument();
            var symbol = new BlockRecord("SYM");
            symbol.BlockEntity.BasePoint = new XYZ(4, 2, 0);
            symbol.Entities.Add(new ACadSharp.Entities.Line(
                new XYZ(4, 2, 0),
                new XYZ(8, 2, 0)));
            document.BlockRecords.Add(symbol);
            document.Entities.Add(new Insert(symbol)
            {
                InsertPoint = new XYZ(10, 20, 0),
            });

            using (var writer = new DxfWriter(path, document))
            {
                writer.Write();
            }

            PackedCadDrawing drawing = new ShallowCadReader().Read(path);
            Assert.Equal(1, drawing.SegmentCount);
            CadVertex[] vertices = drawing.Vertices.Span.ToArray();
            Assert.Contains(vertices, vertex =>
                MathF.Abs(vertex.X - 10) < 0.01f && MathF.Abs(vertex.Y - 20) < 0.01f);
            Assert.Contains(vertices, vertex =>
                MathF.Abs(vertex.X - 14) < 0.01f && MathF.Abs(vertex.Y - 20) < 0.01f);
            Assert.DoesNotContain(vertices, vertex =>
                MathF.Abs(vertex.X - 4) < 0.01f && MathF.Abs(vertex.Y - 2) < 0.01f);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TwoDPolylineUsesElevationAndExtrusion()
    {
        var polyline = new LwPolyline
        {
            Elevation = 3,
            Normal = new XYZ(0, 0, -1),
            Vertices =
            {
                new LwPolyline.Vertex(new XY(6, 1)),
                new LwPolyline.Vertex(new XY(9, 1)),
            },
        };
        var segments = new List<LocalSegment>();
        Assert.True(DisplayGeometryExtractor.Append(polyline, 0, segments));
        Assert.Single(segments);
        Assert.Equal(-6, segments[0].Start.X, precision: 3);
        Assert.Equal(1, segments[0].Start.Y, precision: 3);
        Assert.Equal(-3, segments[0].Start.Z, precision: 3);
        Assert.Equal(-9, segments[0].End.X, precision: 3);
    }
}
