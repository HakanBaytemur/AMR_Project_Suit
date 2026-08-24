using System.Numerics;

namespace DwgTrueView.Cad;

/// <summary>
/// Ear-clip triangulation of closed hatch loops. Holes and self-intersections
/// that cannot be resolved are dropped rather than filled with broken geometry.
/// </summary>
internal static class HatchFillTessellator
{
    private const int MaxVertices = 256;

    public static void Append(
        IReadOnlyList<Vector3> loop,
        int layerId,
        CadColorValue colorA,
        CadColorValue colorB,
        CadColorValue colorC,
        List<LocalTriangle> destination)
    {
        if (destination is null || loop is null || loop.Count < 3)
        {
            return;
        }

        var points = new List<Vector3>(loop.Count);
        foreach (Vector3 point in loop)
        {
            if (!CadMath.IsUsable(point))
            {
                continue;
            }
            if (points.Count > 0 && Vector3.DistanceSquared(points[^1], point) <= 1e-12f)
            {
                continue;
            }
            points.Add(point);
        }
        if (points.Count >= 2
            && Vector3.DistanceSquared(points[0], points[^1]) <= 1e-12f)
        {
            points.RemoveAt(points.Count - 1);
        }
        if (points.Count < 3 || points.Count > MaxVertices)
        {
            return;
        }
        if (SignedArea(points) < 0)
        {
            points.Reverse();
        }

        if (points.Count == 3)
        {
            Add(destination, points[0], points[1], points[2], layerId, colorA, colorB, colorC);
            return;
        }

        var indices = new List<int>(points.Count);
        for (int i = 0; i < points.Count; i++)
        {
            indices.Add(i);
        }

        int guard = indices.Count * 3;
        while (indices.Count > 3 && guard-- > 0)
        {
            bool clipped = false;
            for (int i = 0; i < indices.Count; i++)
            {
                int previous = indices[(i + indices.Count - 1) % indices.Count];
                int current = indices[i];
                int next = indices[(i + 1) % indices.Count];
                if (!IsEar(points, indices, previous, current, next))
                {
                    continue;
                }
                Add(
                    destination,
                    points[previous],
                    points[current],
                    points[next],
                    layerId,
                    colorA,
                    colorB,
                    colorC);
                indices.RemoveAt(i);
                clipped = true;
                break;
            }
            if (!clipped)
            {
                return;
            }
        }
        if (indices.Count == 3)
        {
            Add(
                destination,
                points[indices[0]],
                points[indices[1]],
                points[indices[2]],
                layerId,
                colorA,
                colorB,
                colorC);
        }
    }

    private static bool IsEar(
        IReadOnlyList<Vector3> points,
        IReadOnlyList<int> indices,
        int previous,
        int current,
        int next)
    {
        Vector3 a = points[previous];
        Vector3 b = points[current];
        Vector3 c = points[next];
        if (Cross(a, b, c) <= 1e-12f)
        {
            return false;
        }
        for (int i = 0; i < indices.Count; i++)
        {
            int index = indices[i];
            if (index == previous || index == current || index == next)
            {
                continue;
            }
            if (PointInTriangle(points[index], a, b, c))
            {
                return false;
            }
        }
        return true;
    }

    private static bool PointInTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        float c1 = Cross(a, b, p);
        float c2 = Cross(b, c, p);
        float c3 = Cross(c, a, p);
        return c1 >= -1e-8f && c2 >= -1e-8f && c3 >= -1e-8f;
    }

    private static float SignedArea(IReadOnlyList<Vector3> points)
    {
        float area = 0;
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 a = points[i];
            Vector3 b = points[(i + 1) % points.Count];
            area += (a.X * b.Y) - (b.X * a.Y);
        }
        return area * 0.5f;
    }

    private static float Cross(Vector3 a, Vector3 b, Vector3 c) =>
        ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));

    private static void Add(
        List<LocalTriangle> destination,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        int layerId,
        CadColorValue colorA,
        CadColorValue colorB,
        CadColorValue colorC)
    {
        if (!CadMath.IsUsable(a)
            || !CadMath.IsUsable(b)
            || !CadMath.IsUsable(c))
        {
            return;
        }
        destination.Add(new LocalTriangle(a, b, c, layerId, colorA, colorB, colorC));
    }
}
