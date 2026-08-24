using System.Numerics;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using CSMath;
using DwgTrueView.Cad;
using DwgTrueView.Core;

namespace DwgTrueView.Tests;

public sealed class ComplexEntityTests
{
    [Fact]
    public void EllipseTessellatesOntoTheParametricCurve()
    {
        var segments = new List<LocalSegment>();
        var ellipse = new Ellipse
        {
            Center = new XYZ(10, 5, 0),
            MajorAxisEndPoint = new XYZ(8, 0, 0),
            RadiusRatio = 0.5,
            StartParameter = 0,
            EndParameter = Math.PI * 2,
        };

        Assert.True(DisplayGeometryExtractor.Append(ellipse, layerId: 0, segments));
        Assert.True(segments.Count >= CurveTessellator.MinSegments);
        foreach (LocalSegment segment in segments)
        {
            AssertOnEllipse(segment.Start, new Vector3(10, 5, 0), new Vector3(8, 0, 0), 4);
            AssertOnEllipse(segment.End, new Vector3(10, 5, 0), new Vector3(8, 0, 0), 4);
        }
    }

    [Fact]
    public void SplineBecomesConnectedPolylineSegments()
    {
        var spline = new Spline { Degree = 3 };
        foreach (XYZ point in new[]
        {
            new XYZ(0, 0, 0),
            new XYZ(10, 20, 0),
            new XYZ(30, 20, 0),
            new XYZ(40, 0, 0),
        })
        {
            spline.ControlPoints.Add(point);
        }
        foreach (double knot in new[] { 0, 0, 0, 0, 1, 1, 1, 1 })
        {
            spline.Knots.Add(knot);
        }

        var segments = new List<LocalSegment>();
        Assert.True(DisplayGeometryExtractor.Append(spline, layerId: 2, segments));
        Assert.True(segments.Count >= CurveTessellator.MinSegments);
        Assert.All(segments, segment => Assert.Equal(2, segment.LayerId));
        Assert.Contains(segments, segment => MathF.Abs(segment.Start.Y - segment.End.Y) > 0.5f);
    }

    [Fact]
    public void TextAndMTextEmitBoundaryAndStrokeOutlines()
    {
        var text = new TextEntity
        {
            Value = "ST-01",
            Height = 2.5,
            InsertPoint = new XYZ(100, 50, 0),
            Color = new Color(0x20, 0x40, 0x80),
        };
        var mtext = new MText("PLATFORM A")
        {
            Height = 3,
            InsertPoint = new XYZ(0, 0, 0),
            RectangleWidth = 40,
            AttachmentPoint = AttachmentPointType.TopLeft,
        };

        var textSegments = new List<LocalSegment>();
        var mtextSegments = new List<LocalSegment>();
        Assert.True(DisplayGeometryExtractor.Append(text, 0, textSegments));
        Assert.True(DisplayGeometryExtractor.Append(mtext, 0, mtextSegments));
        Assert.True(textSegments.Count >= 8);
        Assert.True(mtextSegments.Count >= 8);
        Assert.All(
            textSegments,
            segment => Assert.Equal(CadColorKind.TrueColor, segment.Color.Kind));
        Assert.Contains(
            textSegments,
            segment => MathF.Abs(segment.Start.X - 100) < 8 && MathF.Abs(segment.Start.Y - 50) < 8);
    }

    [Fact]
    public void HatchExportsBoundaryLoopsOnly()
    {
        var hatch = new Hatch { IsSolid = true };
        var path = new Hatch.BoundaryPath();
        path.Edges.Add(new Hatch.BoundaryPath.Polyline(
            [
                new XYZ(0, 0, 0),
                new XYZ(12, 0, 0),
                new XYZ(12, 8, 0),
                new XYZ(0, 8, 0),
            ],
            isClosed: true));
        hatch.Paths.Add(path);

        var segments = new List<LocalSegment>();
        var fills = new List<LocalTriangle>();
        Assert.True(DisplayGeometryExtractor.Append(hatch, 0, segments, fills));
        Assert.Empty(segments);
        Assert.True(fills.Count >= 2);
        Assert.DoesNotContain(
            segments,
            segment =>
                segment.Start.X > 0.5f
                && segment.Start.X < 11.5f
                && segment.Start.Y > 0.5f
                && segment.Start.Y < 7.5f
                && segment.End.X > 0.5f
                && segment.End.X < 11.5f
                && segment.End.Y > 0.5f
                && segment.End.Y < 7.5f);
    }

    [Fact]
    public void ReaderBatchesComplexEntitiesWithLayerColors()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.dxf");
        try
        {
            var document = new CadDocument();
            var labels = new Layer("LABELS") { Color = new Color((short)1) };
            var hidden = new Layer("HIDDEN") { IsOn = false };
            document.Layers.Add(labels);
            document.Layers.Add(hidden);

            var spline = new Spline { Degree = 3, Layer = labels, Color = Color.ByLayer };
            foreach (XYZ point in new[]
            {
                new XYZ(0, 0, 0),
                new XYZ(5, 8, 0),
                new XYZ(12, 8, 0),
                new XYZ(16, 0, 0),
            })
            {
                spline.ControlPoints.Add(point);
            }
            foreach (double knot in new[] { 0, 0, 0, 0, 1, 1, 1, 1 })
            {
                spline.Knots.Add(knot);
            }
            document.Entities.Add(spline);
            document.Entities.Add(new Ellipse
            {
                Center = new XYZ(30, 4, 0),
                MajorAxisEndPoint = new XYZ(6, 0, 0),
                RadiusRatio = 0.4,
                StartParameter = 0,
                EndParameter = Math.PI,
                Color = new Color(0x11, 0x22, 0x33),
            });
            document.Entities.Add(new TextEntity
            {
                Value = "STA",
                Height = 2,
                InsertPoint = new XYZ(0, 12, 0),
                Layer = labels,
                Color = Color.ByLayer,
            });
            document.Entities.Add(new MText("GATE")
            {
                Height = 2,
                InsertPoint = new XYZ(20, 12, 0),
            });
            var hatch = new Hatch { IsSolid = true };
            hatch.Paths.Add(new Hatch.BoundaryPath());
            hatch.Paths[0].Edges.Add(new Hatch.BoundaryPath.Line
            {
                Start = new XY(40, 0),
                End = new XY(46, 0),
            });
            hatch.Paths[0].Edges.Add(new Hatch.BoundaryPath.Line
            {
                Start = new XY(46, 0),
                End = new XY(46, 4),
            });
            hatch.Paths[0].Edges.Add(new Hatch.BoundaryPath.Line
            {
                Start = new XY(46, 4),
                End = new XY(40, 4),
            });
            hatch.Paths[0].Edges.Add(new Hatch.BoundaryPath.Line
            {
                Start = new XY(40, 4),
                End = new XY(40, 0),
            });
            document.Entities.Add(hatch);
            document.Entities.Add(new Ellipse
            {
                Center = new XYZ(0, 0, 0),
                MajorAxisEndPoint = new XYZ(2, 0, 0),
                RadiusRatio = 0.5,
                Layer = hidden,
            });
            document.Entities.Add(new ACadSharp.Entities.Line(
                new XYZ(100, 0, 0),
                new XYZ(101, 0, 0))
            {
                IsInvisible = true,
            });

            using (var writer = new DxfWriter(path, document))
            {
                writer.Write();
            }

            PackedCadDrawing drawing = new ShallowCadReader().Read(path);
            Assert.True(drawing.SegmentCount >= 20);
            Assert.Equal(0, drawing.Vertices.Length & 1);
            Assert.Equal(1, drawing.SkippedEntityCount);
            Assert.False(drawing.Layers.Span
                .ToArray()
                .Single(layer => layer.Name == "HIDDEN")
                .IsInitiallyVisible);

            uint red = CadVertex.FromArgb(unchecked((int)0xFFFF0000));
            uint trueColor = CadVertex.FromArgb(unchecked((int)0xFF112233));
            Assert.Contains(drawing.Vertices.Span.ToArray(), vertex => vertex.ColorRgba == red);
            Assert.Contains(drawing.Vertices.Span.ToArray(), vertex => vertex.ColorRgba == trueColor);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void InvisibleSplineIsNotTessellated()
    {
        var spline = new Spline { Degree = 2, IsInvisible = true };
        spline.ControlPoints.Add(new XYZ(0, 0, 0));
        spline.ControlPoints.Add(new XYZ(1, 1, 0));
        spline.ControlPoints.Add(new XYZ(2, 0, 0));
        var segments = new List<LocalSegment>();
        Assert.True(DisplayGeometryExtractor.Append(spline, 0, segments));
        Assert.Empty(segments);
    }

    private static void AssertOnEllipse(
        Vector3 point,
        Vector3 center,
        Vector3 major,
        float minorLength)
    {
        Vector3 local = point - center;
        float x = Vector3.Dot(local, Vector3.Normalize(major)) / major.Length();
        float y = local.Y / minorLength;
        Assert.InRange(x * x + y * y, 0.92, 1.08);
    }
}
