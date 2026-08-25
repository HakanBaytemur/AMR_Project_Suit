using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Objects;
using ACadSharp.Tables;
using CSMath;
using DwgTrueView.Cad;
using DwgTrueView.Core;
using System.Numerics;

namespace DwgTrueView.Tests;

public sealed class SpecComplianceTests
{
    [Fact]
    public void InvisibleGroup60EntitiesAreNotDrawn()
    {
        var line = new ACadSharp.Entities.Line(new XYZ(4, 4, 0), new XYZ(8, 4, 0))
        {
            IsInvisible = true,
        };
        var segments = new List<LocalSegment>();
        Assert.True(DisplayGeometryExtractor.Append(line, 0, segments));
        Assert.Empty(segments);
    }

    [Fact]
    public void DefaultMTextHasNoBackgroundFrame()
    {
        var plain = new MText("STA")
        {
            Height = 2,
            InsertPoint = new XYZ(10, 10, 0),
            BackgroundFillFlags = BackgroundFillFlags.None,
        };
        var framed = new MText("STA")
        {
            Height = 2,
            InsertPoint = new XYZ(10, 10, 0),
            BackgroundFillFlags = BackgroundFillFlags.TextFrame,
        };
        var plainSegments = new List<LocalSegment>();
        var framedSegments = new List<LocalSegment>();
        Assert.True(DisplayGeometryExtractor.Append(plain, 0, plainSegments));
        Assert.True(DisplayGeometryExtractor.Append(framed, 0, framedSegments));
        Assert.True(plainSegments.Count >= 3);
        Assert.Equal(plainSegments.Count + 4, framedSegments.Count);
    }

    [Fact]
    public void UnresolvedColorFallsBackToAutocadWhite()
    {
        Assert.Equal(
            unchecked((int)0xFFFFFFFF),
            CadColorResolver.Resolve(
                CadColorValue.ByLayer,
                CadColorValue.ByBlock,
                CadColorValue.ByLayer));
        Assert.Equal(
            unchecked((int)0xFFFFFFFF),
            CadColorResolver.AciArgb(7));
    }

    [Fact]
    public void UnhandledAndCorruptEntitiesAreSkipped()
    {
        var segments = new List<LocalSegment>();
        Assert.False(DisplayGeometryExtractor.Append(null!, 0, segments));
        Assert.False(DisplayGeometryExtractor.Append(new Point(new XYZ(1, 2, 0)), 0, segments));
        Assert.True(DisplayGeometryExtractor.Append(new Solid(), 0, segments));
        Assert.True(DisplayGeometryExtractor.Append(new ProxyEntity(), 0, segments));
        Assert.True(DisplayGeometryExtractor.Append(new LwPolyline(), 0, segments));
        Assert.True(DisplayGeometryExtractor.Append(new Spline { Degree = 3 }, 0, segments));
        Assert.Empty(segments);
    }

    [Fact]
    public void MTextDoesNotWrapWhenRectangleWidthIsZero()
    {
        var mtext = new MText("SYSTEM PALLET DIAGRAM")
        {
            Height = 2,
            InsertPoint = new XYZ(50, 20, 0),
            RectangleWidth = 0,
            AttachmentPoint = AttachmentPointType.TopLeft,
        };
        var segments = new List<LocalSegment>();
        Assert.True(DisplayGeometryExtractor.Append(mtext, 0, segments));
        Assert.True(segments.Count >= 8);
        float minY = segments.Min(static s => MathF.Min(s.Start.Y, s.End.Y));
        float maxY = segments.Max(static s => MathF.Max(s.Start.Y, s.End.Y));
        Assert.True(maxY - minY < 4.5f);
    }

    [Fact]
    public void TinyMTextRectangleWidthKeepsDigitsOnOneLine()
    {
        string[] wrapped = CadTextLayout.Wrap(
            ["158"],
            wrapWidth: 0.1f,
            measure: static text => text.Length * 1.2f);
        Assert.Single(wrapped);
        Assert.Equal("158", wrapped[0]);

        var mtext = new MText("158")
        {
            Height = 2.5,
            InsertPoint = new XYZ(80, 40, 0),
            RectangleWidth = 0.1,
            AttachmentPoint = AttachmentPointType.MiddleCenter,
        };
        var segments = new List<LocalSegment>();
        var fills = new List<LocalTriangle>();
        Assert.True(DisplayGeometryExtractor.Append(mtext, 0, segments, fills));
        Assert.True(fills.Count >= 2);
        float minY = fills.Min(static t => MathF.Min(t.A.Y, MathF.Min(t.B.Y, t.C.Y)));
        float maxY = fills.Max(static t => MathF.Max(t.A.Y, MathF.Max(t.B.Y, t.C.Y)));
        Assert.True(maxY - minY < 4.5f, $"stacked height was {maxY - minY}");
        float minX = fills.Min(static t => MathF.Min(t.A.X, MathF.Min(t.B.X, t.C.X)));
        float maxX = fills.Max(static t => MathF.Max(t.A.X, MathF.Max(t.B.X, t.C.X)));
        Assert.True(maxX - minX > maxY - minY, "digits should read horizontally, not as a vertical stack");
    }

    [Fact]
    public void MTextEmitsSolidGlyphFills()
    {
        var mtext = new MText("21")
        {
            Height = 3,
            InsertPoint = new XYZ(10, 10, 0),
            RectangleWidth = 0,
            AttachmentPoint = AttachmentPointType.MiddleCenter,
        };
        var segments = new List<LocalSegment>();
        var fills = new List<LocalTriangle>();
        Assert.True(DisplayGeometryExtractor.Append(mtext, 0, segments, fills));
        Assert.True(fills.Count >= 2);
        Assert.All(
            fills,
            triangle =>
            {
                Assert.InRange(triangle.A.X, 4f, 16f);
                Assert.InRange(triangle.A.Y, 6f, 14f);
            });
    }

    [Fact]
    public void AnnotationScaleDefaultsToOneToOne()
    {
        Assert.Equal(1f, AnnotationScale.Factor(null));
        Assert.Equal(1f, AnnotationScale.Factor(new Scale { IsUnitScale = true, PaperUnits = 1, DrawingUnits = 1 }));
        Assert.Equal(100f, AnnotationScale.Factor(new Scale { PaperUnits = 1, DrawingUnits = 100 }));
        Assert.Equal(1f, AnnotationScale.ModelFactor(new MText("A") { Height = 2 }));
    }

    [Fact]
    public void RegionSatBoundaryIsFilled()
    {
        var region = new Region
        {
            AcisData = System.Text.Encoding.ASCII.GetBytes(
                """
                700 0 1 0
                point $-1 $-1 $-1 10 20 0 #
                point $-1 $-1 $-1 22 20 0 #
                point $-1 $-1 $-1 22 28 0 #
                point $-1 $-1 $-1 10 28 0 #
                """),
        };
        var segments = new List<LocalSegment>();
        var fills = new List<LocalTriangle>();
        Assert.True(DisplayGeometryExtractor.Append(region, 0, segments, fills));
        Assert.True(fills.Count >= 1);
        Assert.All(
            fills,
            triangle =>
            {
                Assert.InRange(triangle.A.X, 9.9f, 22.1f);
                Assert.InRange(triangle.A.Y, 19.9f, 28.1f);
            });
    }

    [Fact]
    public void SolidNamedHatchFillsEvenWhenIsSolidIsFalse()
    {
        var hatch = new Hatch
        {
            IsSolid = false,
            Pattern = new HatchPattern("SOLID"),
        };
        var path = new Hatch.BoundaryPath();
        path.Edges.Add(new Hatch.BoundaryPath.Polyline(
            [
                new XYZ(100, 50, 0),
                new XYZ(112, 50, 0),
                new XYZ(112, 58, 0),
                new XYZ(100, 58, 0),
            ],
            isClosed: true));
        hatch.Paths.Add(path);

        var segments = new List<LocalSegment>();
        var fills = new List<LocalTriangle>();
        Assert.True(DisplayGeometryExtractor.Append(hatch, 0, segments, fills));
        Assert.True(fills.Count >= 2);
        Assert.All(
            fills,
            triangle =>
            {
                Assert.InRange(triangle.A.X, 99.9f, 112.1f);
                Assert.InRange(triangle.A.Y, 49.9f, 58.1f);
            });
    }

    [Fact]
    public void AttributeDefinitionsAreHiddenWhileAttributeValuesDraw()
    {
        var definition = new AttributeDefinition
        {
            Tag = "TAG",
            Value = "TEMPLATE",
            Height = 2,
            InsertPoint = new XYZ(0, 0, 0),
        };
        var value = new AttributeEntity(definition)
        {
            Value = "101",
            Height = 2,
            InsertPoint = new XYZ(30, 40, 0),
        };
        var hidden = new List<LocalSegment>();
        var visible = new List<LocalSegment>();
        Assert.True(DisplayGeometryExtractor.Append(definition, 0, hidden));
        Assert.True(DisplayGeometryExtractor.Append(value, 0, visible));
        Assert.Empty(hidden);
        Assert.True(visible.Count >= 3);
    }

    [Fact]
    public void SolidHatchEmitsFilledTrianglesNotInteriorStrokes()
    {
        var hatch = new Hatch { IsSolid = true };
        var path = new Hatch.BoundaryPath();
        path.Edges.Add(new Hatch.BoundaryPath.Polyline(
            [
                new XYZ(10, 10, 0),
                new XYZ(22, 10, 0),
                new XYZ(22, 18, 0),
                new XYZ(10, 18, 0),
            ],
            isClosed: true));
        hatch.Paths.Add(path);

        var segments = new List<LocalSegment>();
        var fills = new List<LocalTriangle>();
        Assert.True(DisplayGeometryExtractor.Append(hatch, 0, segments, fills));
        Assert.Empty(segments);
        Assert.True(fills.Count >= 2);
        Assert.All(
            fills,
            triangle =>
            {
                Assert.InRange(triangle.A.X, 9.9f, 22.1f);
                Assert.InRange(triangle.B.X, 9.9f, 22.1f);
                Assert.InRange(triangle.C.X, 9.9f, 22.1f);
            });
    }

    [Fact]
    public void MTextFormatCodesAreStrippedAndAsciiLettersStayIntact()
    {
        Assert.Equal("Pallet", CadTextCodec.ToPlain(@"\W1;Pallet"));
        Assert.Equal("QTY", CadTextCodec.ToPlain(@"\W1;QTY"));
        Assert.Equal("Walk Speed", CadTextCodec.ToPlain(@"{\fArial|b0|i0;\W1;Walk Speed}"));
        Assert.Equal("Parameters", CadTextCodec.ToPlain(@"Parameters"));
        Assert.Equal('P', CadTextCodec.MapGlyph('P'));
        Assert.Equal('d', CadTextCodec.MapGlyph('d'));
    }

    [Fact]
    public void ClosedLettersKeepCountersInsteadOfCollapsingToStems()
    {
        var pee = new TextEntity
        {
            Value = "P",
            Height = 10,
            InsertPoint = new XYZ(0, 0, 0),
        };
        var eye = new TextEntity
        {
            Value = "I",
            Height = 10,
            InsertPoint = new XYZ(0, 0, 0),
        };
        var oh = new TextEntity
        {
            Value = "O",
            Height = 10,
            InsertPoint = new XYZ(0, 0, 0),
        };
        var pFills = new List<LocalTriangle>();
        var iFills = new List<LocalTriangle>();
        var oFills = new List<LocalTriangle>();
        Assert.True(DisplayGeometryExtractor.Append(pee, 0, [], pFills));
        Assert.True(DisplayGeometryExtractor.Append(eye, 0, [], iFills));
        Assert.True(DisplayGeometryExtractor.Append(oh, 0, [], oFills));
        Assert.True(pFills.Count >= 8);
        Assert.True(oFills.Count >= 8);
        float pWidth = Width(pFills);
        float iWidth = Width(iFills);
        Assert.True(pWidth > iWidth * 1.15f, $"P width {pWidth} vs I width {iWidth}");
    }

    [Fact]
    public void UnreferencedBlockDefinitionsAreNotDrawnInModelSpace()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.dxf");
        try
        {
            var document = new CadDocument();
            var unused = new BlockRecord("UNUSED_GHOST");
            unused.Entities.Add(new ACadSharp.Entities.Line(
                new XYZ(8000, 9000, 0),
                new XYZ(8100, 9000, 0)));
            document.BlockRecords.Add(unused);
            document.Entities.Add(new ACadSharp.Entities.Line(
                new XYZ(10, 10, 0),
                new XYZ(20, 10, 0)));

            using (var writer = new DxfWriter(path, document))
            {
                writer.Write();
            }

            PackedCadDrawing drawing = new ShallowCadReader().Read(path);
            Assert.DoesNotContain(
                drawing.Vertices.ToArray(),
                vertex => vertex.X > 7000 || vertex.Y > 7000);
            Assert.Contains(
                drawing.Vertices.ToArray(),
                vertex => MathF.Abs(vertex.X - 10) < 1 || MathF.Abs(vertex.X - 20) < 1);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static float Width(IReadOnlyList<LocalTriangle> fills)
    {
        float min = fills.Min(static t => MathF.Min(t.A.X, MathF.Min(t.B.X, t.C.X)));
        float max = fills.Max(static t => MathF.Max(t.A.X, MathF.Max(t.B.X, t.C.X)));
        return max - min;
    }

    [Fact]
    public void OriginConnectedFarSegmentsAreCorruptRays()
    {
        Assert.True(CadMath.IsCorruptOriginRay(Vector3.Zero, new Vector3(1200, 800, 0)));
        Assert.False(CadMath.IsCorruptOriginRay(new Vector3(4, 4, 0), new Vector3(8, 4, 0)));
        Assert.False(CadMath.IsCorruptOriginRay(Vector3.Zero, new Vector3(0.5f, 0, 0)));
    }
}
