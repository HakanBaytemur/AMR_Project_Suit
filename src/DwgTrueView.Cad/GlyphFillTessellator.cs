using System.Numerics;

namespace DwgTrueView.Cad;

/// <summary>
/// Even-odd scanline fill for TTF glyph contours. Ear-clipping the outer ring
/// and dropping triangles whose centroid falls in a hole destroys counters
/// ('O' vanishes, 'P' becomes a stem that looks like '1').
/// </summary>
internal static class GlyphFillTessellator
{
    private const int Rows = 40;

    public static (Vector2 A, Vector2 B, Vector2 C)[] Fill(IReadOnlyList<List<Vector2>> loops)
    {
        if (loops is null || loops.Count == 0)
        {
            return [];
        }

        float minX = float.PositiveInfinity;
        float minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float maxY = float.NegativeInfinity;
        foreach (List<Vector2> loop in loops)
        {
            foreach (Vector2 point in loop)
            {
                minX = MathF.Min(minX, point.X);
                minY = MathF.Min(minY, point.Y);
                maxX = MathF.Max(maxX, point.X);
                maxY = MathF.Max(maxY, point.Y);
            }
        }
        if (!float.IsFinite(minX) || maxY - minY < 1e-6f)
        {
            return [];
        }

        float dy = (maxY - minY) / Rows;
        var xs = new List<float>(16);
        var triangles = new List<(Vector2 A, Vector2 B, Vector2 C)>();
        for (int row = 0; row < Rows; row++)
        {
            float y0 = minY + row * dy;
            float y1 = y0 + dy;
            float y = y0 + dy * 0.5f;
            xs.Clear();
            foreach (List<Vector2> loop in loops)
            {
                Intersect(loop, y, xs);
            }
            if (xs.Count < 2)
            {
                continue;
            }
            xs.Sort();
            for (int i = 0; i + 1 < xs.Count; i += 2)
            {
                float x0 = xs[i];
                float x1 = xs[i + 1];
                if (x1 - x0 <= 1e-6f)
                {
                    continue;
                }
                var a = new Vector2(x0, y0);
                var b = new Vector2(x1, y0);
                var c = new Vector2(x1, y1);
                var d = new Vector2(x0, y1);
                triangles.Add((a, b, c));
                triangles.Add((a, c, d));
            }
        }
        return triangles.ToArray();
    }

    private static void Intersect(IReadOnlyList<Vector2> loop, float y, List<float> xs)
    {
        for (int i = 0; i < loop.Count; i++)
        {
            Vector2 a = loop[i];
            Vector2 b = loop[(i + 1) % loop.Count];
            if ((a.Y > y) == (b.Y > y) || MathF.Abs(b.Y - a.Y) <= 1e-12f)
            {
                continue;
            }
            float t = (y - a.Y) / (b.Y - a.Y);
            if (t < 0 || t > 1)
            {
                continue;
            }
            xs.Add(a.X + t * (b.X - a.X));
        }
    }
}
