using System.Numerics;
using DwgTrueView.Core;

namespace DwgTrueView.Tests;

public sealed class ViewportOverlayTests
{
    [Fact]
    public void NiceGridStepSnapsToOneTwoFiveDecades()
    {
        Assert.Equal(1f, ViewportOverlay.NiceGridStep(1.0f));
        Assert.Equal(2f, ViewportOverlay.NiceGridStep(1.1f));
        Assert.Equal(2f, ViewportOverlay.NiceGridStep(1.8f));
        Assert.Equal(5f, ViewportOverlay.NiceGridStep(4f));
        Assert.Equal(10f, ViewportOverlay.NiceGridStep(9f));
    }

    [Fact]
    public void OverlayIncludesSubtleGridAndPositiveOnlyOriginTriad()
    {
        var vertices = new CadVertex[ViewportOverlay.MaxVertices];
        ViewportOverlay.Counts counts = ViewportOverlay.Write(
            vertices,
            center: Vector2.Zero,
            unitsPerPixel: 1f,
            viewportWidth: 800,
            viewportHeight: 600);

        Assert.True(counts.GridVertices >= 8);
        Assert.True(counts.AccentVertices >= 10);
        CadVertex[] used = vertices.Take(counts.Total).ToArray();
        Assert.Contains(used, vertex => vertex.ColorRgba == ViewportOverlay.MinorGridColor);
        Assert.Contains(
            used,
            vertex => vertex.ColorRgba == ViewportOverlay.AxisXColor && vertex.Y == 0 && vertex.X > 0);
        Assert.Contains(
            used,
            vertex => vertex.ColorRgba == ViewportOverlay.AxisYColor && vertex.X == 0 && vertex.Y > 0);
        Assert.Contains(
            used,
            vertex => vertex.ColorRgba == ViewportOverlay.UcsColor && vertex.X == 0 && vertex.Y == 0);

        CadVertex[] red = used
            .Where(vertex => vertex.ColorRgba == ViewportOverlay.AxisXColor)
            .ToArray();
        CadVertex[] green = used
            .Where(vertex => vertex.ColorRgba == ViewportOverlay.AxisYColor)
            .ToArray();
        Assert.All(red, vertex => Assert.True(vertex.X >= -0.01f));
        Assert.DoesNotContain(red, vertex => vertex.Y == 0 && vertex.X < -0.01f);
        Assert.All(green, vertex => Assert.True(vertex.Y >= -0.01f));
        Assert.DoesNotContain(green, vertex => vertex.X == 0 && vertex.Y < -0.01f);
        Assert.DoesNotContain(
            used,
            vertex => vertex.ColorRgba == ViewportOverlay.AxisXColor
                && vertex.X < -1f);
        Assert.DoesNotContain(
            used,
            vertex => vertex.ColorRgba == ViewportOverlay.AxisYColor
                && vertex.Y < -1f);
    }
}
