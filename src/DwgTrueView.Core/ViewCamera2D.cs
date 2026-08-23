using System.Numerics;

namespace DwgTrueView.Core;

/// <summary>
/// Allocation-free orthographic CAD camera measured in drawing units.
/// </summary>
public sealed class ViewCamera2D
{
    private const float MinimumUnitsPerPixel = 1e-9f;
    private const float MaximumUnitsPerPixel = 1e12f;

    public Vector2 Center { get; private set; }
    public float UnitsPerPixel { get; private set; } = 1;

    public Vector2 ScreenToWorld(Vector2 screen, Vector2 viewportSize)
    {
        Vector2 delta = screen - viewportSize * 0.5f;
        return new Vector2(
            Center.X + delta.X * UnitsPerPixel,
            Center.Y - delta.Y * UnitsPerPixel);
    }

    public Vector2 WorldToScreen(Vector2 world, Vector2 viewportSize) =>
        new(
            viewportSize.X * 0.5f + (world.X - Center.X) / UnitsPerPixel,
            viewportSize.Y * 0.5f - (world.Y - Center.Y) / UnitsPerPixel);

    public void PanPixels(Vector2 pixelDelta)
    {
        Center += new Vector2(
            -pixelDelta.X * UnitsPerPixel,
            pixelDelta.Y * UnitsPerPixel);
    }

    public void ZoomAt(
        Vector2 screenAnchor,
        Vector2 viewportSize,
        float zoomFactor)
    {
        if (!float.IsFinite(zoomFactor) || zoomFactor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(zoomFactor));
        }

        Vector2 worldBefore = ScreenToWorld(screenAnchor, viewportSize);
        UnitsPerPixel = Math.Clamp(
            UnitsPerPixel * zoomFactor,
            MinimumUnitsPerPixel,
            MaximumUnitsPerPixel);
        Vector2 worldAfter = ScreenToWorld(screenAnchor, viewportSize);
        Center += worldBefore - worldAfter;
    }

    public void Fit(CadBounds2 bounds, Vector2 viewportSize, float margin = 0.05f)
    {
        if (bounds.IsEmpty || viewportSize.X <= 0 || viewportSize.Y <= 0)
        {
            Center = Vector2.Zero;
            UnitsPerPixel = 1;
            return;
        }
        if (!float.IsFinite(margin) || margin < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(margin));
        }

        Vector2 size = Vector2.Max(bounds.Size, new Vector2(MinimumUnitsPerPixel));
        float usableWidth = Math.Max(1, viewportSize.X * (1 - 2 * margin));
        float usableHeight = Math.Max(1, viewportSize.Y * (1 - 2 * margin));
        Center = bounds.Center;
        UnitsPerPixel = Math.Clamp(
            MathF.Max(size.X / usableWidth, size.Y / usableHeight),
            MinimumUnitsPerPixel,
            MaximumUnitsPerPixel);
    }
}
