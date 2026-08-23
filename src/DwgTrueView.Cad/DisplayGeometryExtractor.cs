using System.Numerics;
using ACadSharp.Entities;

namespace DwgTrueView.Cad;

internal readonly record struct LocalSegment(
    Vector3 Start,
    Vector3 End,
    int LayerId,
    CadColorValue Color);

internal static class DisplayGeometryExtractor
{
    private const float TwoPi = MathF.PI * 2;
    private const float BulgeTolerance = 1e-7f;
    private const int CircleSegments = 64;

    public static bool Append(
        Entity entity,
        int layerId,
        List<LocalSegment> destination)
    {
        if (entity.IsInvisible)
        {
            return true;
        }
        CadColorValue color = CadColorResolver.FromCadColor(entity.Color);
        switch (entity)
        {
            case ACadSharp.Entities.Line line:
                Add(
                    destination,
                    CadMath.ToVector(line.StartPoint),
                    CadMath.ToVector(line.EndPoint),
                    layerId,
                    color);
                return true;
            case LwPolyline polyline:
                AppendPolyline(polyline, layerId, color, destination);
                return true;
            case Arc arc:
                AppendArc(
                    CadMath.ToVector(arc.Center),
                    CadMath.ToVector(arc.Normal),
                    (float)arc.Radius,
                    (float)arc.StartAngle,
                    PositiveSweep((float)arc.StartAngle, (float)arc.EndAngle),
                    layerId,
                    color,
                    destination);
                return true;
            case Circle circle:
                AppendArc(
                    CadMath.ToVector(circle.Center),
                    CadMath.ToVector(circle.Normal),
                    (float)circle.Radius,
                    0,
                    TwoPi,
                    layerId,
                    color,
                    destination);
                return true;
            default:
                return false;
        }
    }

    private static void AppendPolyline(
        LwPolyline polyline,
        int layerId,
        CadColorValue color,
        List<LocalSegment> destination)
    {
        int count = polyline.Vertices.Count;
        if (count < 2)
        {
            return;
        }
        int segmentCount = polyline.IsClosed ? count : count - 1;
        float elevation = (float)polyline.Elevation;
        for (int index = 0; index < segmentCount; index++)
        {
            LwPolyline.Vertex current = polyline.Vertices[index];
            LwPolyline.Vertex next = polyline.Vertices[(index + 1) % count];
            Vector3 start = new(
                (float)current.Location.X,
                (float)current.Location.Y,
                elevation);
            Vector3 end = new(
                (float)next.Location.X,
                (float)next.Location.Y,
                elevation);
            float bulge = (float)current.Bulge;
            if (MathF.Abs(bulge) <= BulgeTolerance)
            {
                Add(destination, start, end, layerId, color);
            }
            else
            {
                AppendBulge(start, end, bulge, layerId, color, destination);
            }
        }
    }

    private static void AppendBulge(
        Vector3 start,
        Vector3 end,
        float bulge,
        int layerId,
        CadColorValue color,
        List<LocalSegment> destination)
    {
        Vector2 p0 = new(start.X, start.Y);
        Vector2 p1 = new(end.X, end.Y);
        Vector2 chord = p1 - p0;
        float length = chord.Length();
        float sweep = 4 * MathF.Atan(bulge);
        if (length <= float.Epsilon || MathF.Abs(sweep) <= BulgeTolerance)
        {
            Add(destination, start, end, layerId, color);
            return;
        }
        Vector2 midpoint = (p0 + p1) * 0.5f;
        Vector2 left = Vector2.Normalize(new Vector2(-chord.Y, chord.X));
        Vector2 center = midpoint + left * (length / (2 * MathF.Tan(sweep / 2)));
        float radius = Vector2.Distance(center, p0);
        float startAngle = MathF.Atan2(p0.Y - center.Y, p0.X - center.X);
        AppendArc(
            new Vector3(center, start.Z),
            Vector3.UnitZ,
            radius,
            startAngle,
            sweep,
            layerId,
            color,
            destination);
    }

    private static void AppendArc(
        Vector3 center,
        Vector3 normal,
        float radius,
        float startAngle,
        float sweep,
        int layerId,
        CadColorValue color,
        List<LocalSegment> destination)
    {
        if (radius <= 0 || MathF.Abs(sweep) <= float.Epsilon)
        {
            return;
        }
        CadMath.CreateOcsBasis(
            normal,
            out Vector3 axisX,
            out Vector3 axisY,
            out _);
        int count = Math.Clamp(
            (int)MathF.Ceiling(CircleSegments * MathF.Abs(sweep) / TwoPi),
            2,
            CircleSegments);
        Vector3 previous = Point(center, axisX, axisY, radius, startAngle);
        for (int index = 1; index <= count; index++)
        {
            float angle = startAngle + sweep * index / count;
            Vector3 current = Point(center, axisX, axisY, radius, angle);
            Add(destination, previous, current, layerId, color);
            previous = current;
        }
    }

    private static Vector3 Point(
        Vector3 center,
        Vector3 axisX,
        Vector3 axisY,
        float radius,
        float angle) =>
        center
        + axisX * (MathF.Cos(angle) * radius)
        + axisY * (MathF.Sin(angle) * radius);

    private static void Add(
        List<LocalSegment> destination,
        Vector3 start,
        Vector3 end,
        int layerId,
        CadColorValue color)
    {
        if (IsFinite(start) && IsFinite(end) && start != end)
        {
            destination.Add(new LocalSegment(start, end, layerId, color));
        }
    }

    private static bool IsFinite(Vector3 point) =>
        float.IsFinite(point.X)
        && float.IsFinite(point.Y)
        && float.IsFinite(point.Z);

    private static float PositiveSweep(float start, float end)
    {
        float sweep = end - start;
        while (sweep < 0)
        {
            sweep += TwoPi;
        }
        return sweep;
    }
}
