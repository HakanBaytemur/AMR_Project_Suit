using System.Numerics;

namespace DwgTrueView.Core;

/// <summary>
/// Adaptive AutoCAD-style viewport grid plus a SolidWorks-style origin triad
/// at world (0,0). Axis vectors are positive-only; screen size stays constant
/// while zooming.
/// </summary>
public static class ViewportOverlay
{
    public const int MaxVertices = 8192;

    public static readonly uint MinorGridColor = CadVertex.FromArgb(unchecked((int)0xFF31363C));
    public static readonly uint MajorGridColor = CadVertex.FromArgb(unchecked((int)0xFF3E454C));
    public static readonly uint AxisXColor = CadVertex.FromArgb(unchecked((int)0xFFE51400));
    public static readonly uint AxisYColor = CadVertex.FromArgb(unchecked((int)0xFF00A800));
    public static readonly uint UcsColor = CadVertex.FromArgb(unchecked((int)0xFFFFFFFF));

    public readonly record struct Counts(int GridVertices, int AccentVertices)
    {
        public int Total => GridVertices + AccentVertices;
    }

    public static Counts Write(
        Span<CadVertex> destination,
        Vector2 center,
        float unitsPerPixel,
        float viewportWidth,
        float viewportHeight)
    {
        if (destination.Length == 0
            || !float.IsFinite(unitsPerPixel)
            || unitsPerPixel <= 0
            || viewportWidth <= 0
            || viewportHeight <= 0)
        {
            return default;
        }

        float halfWidth = unitsPerPixel * viewportWidth * 0.5f;
        float halfHeight = unitsPerPixel * viewportHeight * 0.5f;
        float left = center.X - halfWidth;
        float right = center.X + halfWidth;
        float bottom = center.Y - halfHeight;
        float top = center.Y + halfHeight;
        int written = 0;

        float minor = NiceGridStep(unitsPerPixel * 28f);
        float major = minor * 5f;
        written = WriteGrid(
            destination,
            written,
            left,
            right,
            bottom,
            top,
            minor,
            major);

        int gridVertices = written;
        written = WriteOriginTriad(
            destination,
            written,
            left,
            right,
            bottom,
            top,
            unitsPerPixel);
        return new Counts(gridVertices, written - gridVertices);
    }

    public static float NiceGridStep(float target)
    {
        target = Math.Max(target, 1e-9f);
        float magnitude = MathF.Pow(10, MathF.Floor(MathF.Log10(target)));
        float normalized = target / magnitude;
        float multiplier = normalized <= 1
            ? 1
            : normalized <= 2
                ? 2
                : normalized <= 5
                    ? 5
                    : 10;
        return magnitude * multiplier;
    }

    private static int WriteGrid(
        Span<CadVertex> destination,
        int written,
        float left,
        float right,
        float bottom,
        float top,
        float minor,
        float major)
    {
        float firstX = MathF.Floor(left / minor) * minor;
        for (float x = firstX; x <= right + minor * 0.5f && written + 2 <= destination.Length; x += minor)
        {
            uint color = IsMajor(x, major) ? MajorGridColor : MinorGridColor;
            written = AddSegment(destination, written, x, bottom, x, top, color);
        }
        float firstY = MathF.Floor(bottom / minor) * minor;
        for (float y = firstY; y <= top + minor * 0.5f && written + 2 <= destination.Length; y += minor)
        {
            uint color = IsMajor(y, major) ? MajorGridColor : MinorGridColor;
            written = AddSegment(destination, written, left, y, right, y, color);
        }
        return written;
    }

    private static int WriteOriginTriad(
        Span<CadVertex> destination,
        int written,
        float left,
        float right,
        float bottom,
        float top,
        float unitsPerPixel)
    {
        float arm = unitsPerPixel * 72f;
        float head = unitsPerPixel * 11f;
        float pad = unitsPerPixel * 16f;
        float glyph = unitsPerPixel * 7f;
        float dot = unitsPerPixel * 3.5f;
        if (0 < left - arm || 0 > right + arm || 0 < bottom - arm || 0 > top + arm)
        {
            return written;
        }

        written = AddSegment(destination, written, -dot, 0, 0, 0, UcsColor);
        written = AddSegment(destination, written, 0, 0, dot, 0, UcsColor);
        written = AddSegment(destination, written, 0, -dot, 0, 0, UcsColor);
        written = AddSegment(destination, written, 0, 0, 0, dot, UcsColor);
        written = AddSegment(destination, written, -dot, -dot, dot, -dot, UcsColor);
        written = AddSegment(destination, written, dot, -dot, dot, dot, UcsColor);
        written = AddSegment(destination, written, dot, dot, -dot, dot, UcsColor);
        written = AddSegment(destination, written, -dot, dot, -dot, -dot, UcsColor);

        written = AddSegment(destination, written, 0, 0, arm, 0, AxisXColor);
        written = AddSegment(
            destination,
            written,
            arm,
            0,
            arm - head,
            head * 0.55f,
            AxisXColor);
        written = AddSegment(
            destination,
            written,
            arm,
            0,
            arm - head,
            -head * 0.55f,
            AxisXColor);

        written = AddSegment(destination, written, 0, 0, 0, arm, AxisYColor);
        written = AddSegment(
            destination,
            written,
            0,
            arm,
            head * 0.55f,
            arm - head,
            AxisYColor);
        written = AddSegment(
            destination,
            written,
            0,
            arm,
            -head * 0.55f,
            arm - head,
            AxisYColor);

        float xCenter = arm + pad;
        written = AddSegment(
            destination,
            written,
            xCenter - glyph,
            -glyph,
            xCenter + glyph,
            glyph,
            UcsColor);
        written = AddSegment(
            destination,
            written,
            xCenter - glyph,
            glyph,
            xCenter + glyph,
            -glyph,
            UcsColor);

        float yCenter = arm + pad;
        written = AddSegment(
            destination,
            written,
            -glyph,
            yCenter + glyph,
            0,
            yCenter,
            UcsColor);
        written = AddSegment(
            destination,
            written,
            glyph,
            yCenter + glyph,
            0,
            yCenter,
            UcsColor);
        written = AddSegment(
            destination,
            written,
            0,
            yCenter,
            0,
            yCenter - glyph,
            UcsColor);
        return written;
    }

    private static bool IsMajor(float value, float major)
    {
        float scaled = value / major;
        return MathF.Abs(scaled - MathF.Round(scaled)) <= 0.02f;
    }

    private static int AddSegment(
        Span<CadVertex> destination,
        int written,
        float x0,
        float y0,
        float x1,
        float y1,
        uint color)
    {
        if (written + 2 > destination.Length)
        {
            return written;
        }
        destination[written] = new CadVertex(x0, y0, color);
        destination[written + 1] = new CadVertex(x1, y1, color);
        return written + 2;
    }
}
