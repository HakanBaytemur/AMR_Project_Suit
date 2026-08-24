using ACadSharp.Entities;
using CSMath;
using DwgTrueView.Cad;
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
    public void OriginConnectedFarSegmentsAreCorruptRays()
    {
        Assert.True(CadMath.IsCorruptOriginRay(Vector3.Zero, new Vector3(1200, 800, 0)));
        Assert.False(CadMath.IsCorruptOriginRay(new Vector3(4, 4, 0), new Vector3(8, 4, 0)));
        Assert.False(CadMath.IsCorruptOriginRay(Vector3.Zero, new Vector3(0.5f, 0, 0)));
    }
}
