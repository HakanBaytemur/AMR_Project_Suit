using System.Globalization;
using System.Numerics;
using ACadSharp.Entities;

namespace DwgTrueView.Cad;

/// <summary>
/// Turns AcDbRegion SAT/ACIS payloads (and legacy wire silhouettes) into planar
/// boundary loops. Unreadable or corrupt SAT is hidden rather than faked.
/// </summary>
internal static class RegionBoundaryEvaluator
{
    public static void Append(
        Region region,
        int layerId,
        CadColorValue color,
        List<LocalSegment> destination,
        List<LocalTriangle>? fills)
    {
        var loops = new List<List<Vector3>>();
        CollectWires(region, loops);
        if (loops.Count == 0)
        {
            CollectSat(region, loops);
        }
        if (loops.Count == 0)
        {
            return;
        }

        foreach (List<Vector3> loop in loops)
        {
            if (loop.Count < 2)
            {
                continue;
            }
            for (int i = 0; i < loop.Count; i++)
            {
                Vector3 start = loop[i];
                Vector3 end = loop[(i + 1) % loop.Count];
                if (!CadMath.IsPlausible(start) || !CadMath.IsPlausible(end) || start == end)
                {
                    continue;
                }
                destination.Add(new LocalSegment(start, end, layerId, color));
            }
            if (fills is not null && loop.Count >= 3)
            {
                HatchFillTessellator.Append(loop, layerId, color, color, color, fills);
            }
        }
    }

    private static void CollectWires(Region region, List<List<Vector3>> loops)
    {
        try
        {
            if (region.Wires is null || region.Wires.Count == 0)
            {
                return;
            }
            foreach (ModelerGeometry.Wire? wire in region.Wires)
            {
                if (wire?.Points is null || wire.Points.Count < 2)
                {
                    continue;
                }
                var loop = new List<Vector3>(wire.Points.Count);
                foreach (CSMath.XYZ point in wire.Points)
                {
                    if (!CadMath.TryPoint(point, out Vector3 world) || !CadMath.IsPlausible(world))
                    {
                        continue;
                    }
                    if (loop.Count > 0 && Vector3.DistanceSquared(loop[^1], world) <= 1e-12f)
                    {
                        continue;
                    }
                    loop.Add(world);
                }
                if (loop.Count >= 2)
                {
                    loops.Add(loop);
                }
            }
        }
        catch (Exception)
        {
        }
    }

    private static void CollectSat(Region region, List<List<Vector3>> loops)
    {
        string? sat = null;
        try
        {
            sat = region.GetAcisText();
        }
        catch (Exception)
        {
        }
        if (string.IsNullOrWhiteSpace(sat))
        {
            try
            {
                sat = region.ProprietaryData?.ToString();
            }
            catch (Exception)
            {
            }
        }
        if (string.IsNullOrWhiteSpace(sat))
        {
            return;
        }

        string payload = sat;
        var points = new List<Vector3>();
        int index = 0;
        while ((index = payload.IndexOf("point", index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            int end = payload.IndexOf('#', index);
            string slice = end > index ? payload[index..end] : payload[index..];
            var numbers = new List<float>(4);
            foreach (string token in slice.Split(
                         [' ', '\t', '\r', '\n'],
                         StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.StartsWith('$')
                    || token.Equals("point", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (TryParse(token, out float value))
                {
                    numbers.Add(value);
                }
            }
            index += 5;
            if (numbers.Count < 3)
            {
                continue;
            }
            var point = new Vector3(
                numbers[^3],
                numbers[^2],
                numbers[^1]);
            if (!CadMath.IsPlausible(point))
            {
                continue;
            }
            bool duplicate = false;
            foreach (Vector3 existing in points)
            {
                if (Vector3.DistanceSquared(existing, point) <= 1e-8f)
                {
                    duplicate = true;
                    break;
                }
            }
            if (!duplicate)
            {
                points.Add(point);
            }
        }
        if (points.Count < 3 || points.Count > 256)
        {
            return;
        }
        List<Vector3> hull = ConvexHull(points);
        if (hull.Count >= 3)
        {
            loops.Add(hull);
        }
    }

    private static bool TryParse(string text, out float value) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && float.IsFinite(value);

    private static List<Vector3> ConvexHull(IReadOnlyList<Vector3> points)
    {
        var hull = new List<Vector3>();
        int start = 0;
        for (int i = 1; i < points.Count; i++)
        {
            if (points[i].X < points[start].X
                || (points[i].X == points[start].X && points[i].Y < points[start].Y))
            {
                start = i;
            }
        }
        int current = start;
        int guard = points.Count + 1;
        do
        {
            hull.Add(points[current]);
            int next = 0;
            for (int i = 1; i < points.Count; i++)
            {
                float cross =
                    ((points[next].X - points[current].X) * (points[i].Y - points[current].Y))
                    - ((points[next].Y - points[current].Y) * (points[i].X - points[current].X));
                if (next == current
                    || cross < 0
                    || (MathF.Abs(cross) <= 1e-8f
                        && Vector2.DistanceSquared(
                            new Vector2(points[current].X, points[current].Y),
                            new Vector2(points[i].X, points[i].Y))
                        > Vector2.DistanceSquared(
                            new Vector2(points[current].X, points[current].Y),
                            new Vector2(points[next].X, points[next].Y))))
                {
                    next = i;
                }
            }
            current = next;
        }
        while (current != start && --guard > 0 && hull.Count < points.Count);
        return hull;
    }
}
