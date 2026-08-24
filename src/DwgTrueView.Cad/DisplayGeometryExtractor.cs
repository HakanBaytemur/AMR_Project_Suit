using System.Numerics;
using ACadSharp.Entities;

namespace DwgTrueView.Cad;

internal readonly record struct LocalSegment(
    Vector3 Start,
    Vector3 End,
    int LayerId,
    CadColorValue Color);

internal readonly record struct LocalTriangle(
    Vector3 A,
    Vector3 B,
    Vector3 C,
    int LayerId,
    CadColorValue Color,
    CadColorValue ColorB,
    CadColorValue ColorC)
{
    public LocalTriangle(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        int layerId,
        CadColorValue color)
        : this(a, b, c, layerId, color, color, color)
    {
    }
}

internal static class DisplayGeometryExtractor
{
    private const float TwoPi = MathF.PI * 2;
    private const float BulgeTolerance = 1e-7f;
    private const int CircleSegments = 64;

    public static bool Append(
        Entity entity,
        int layerId,
        List<LocalSegment> destination) =>
        Append(entity, layerId, destination, fills: null);

    public static bool Append(
        Entity entity,
        int layerId,
        List<LocalSegment> destination,
        List<LocalTriangle>? fills)
    {
        if (entity is null)
        {
            return false;
        }
        try
        {
            return AppendCore(entity, layerId, destination, fills);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool AppendCore(
        Entity entity,
        int layerId,
        List<LocalSegment> destination,
        List<LocalTriangle>? fills)
    {
        if (entity.IsInvisible)
        {
            return true;
        }
        CadColorValue color = CadColorResolver.FromCadColor(entity.Color);
        switch (entity)
        {
            case AttributeDefinition:
                return true;
            case AttributeEntity attrib:
                if ((attrib.Flags & AttributeFlags.Hidden) != 0)
                {
                    return true;
                }
                AppendText(attrib, layerId, color, destination);
                return true;
            case ACadSharp.Entities.Line line:
                if (CadMath.TryWorldPoint(line.StartPoint, out Vector3 lineStart)
                    && CadMath.TryWorldPoint(line.EndPoint, out Vector3 lineEnd))
                {
                    Add(destination, lineStart, lineEnd, layerId, color);
                }
                return true;
            case LwPolyline polyline:
                AppendLwPolyline(polyline, layerId, color, destination);
                return true;
            case Polyline2D polyline:
                AppendPolyline2D(polyline, layerId, color, destination);
                return true;
            case Polyline3D polyline:
                AppendPolyline3D(polyline, layerId, color, destination);
                return true;
            case Arc arc:
                if (CadMath.TryOcsToWcs(arc.Center, arc.Normal, out Vector3 arcCenter))
                {
                    AppendArc(
                        arcCenter,
                        CadMath.UsableNormal(arc.Normal),
                        (float)arc.Radius,
                        (float)arc.StartAngle,
                        PositiveSweep((float)arc.StartAngle, (float)arc.EndAngle),
                        layerId,
                        color,
                        destination);
                }
                return true;
            case Circle circle:
                if (CadMath.TryOcsToWcs(circle.Center, circle.Normal, out Vector3 circleCenter))
                {
                    AppendArc(
                        circleCenter,
                        CadMath.UsableNormal(circle.Normal),
                        (float)circle.Radius,
                        0,
                        TwoPi,
                        layerId,
                        color,
                        destination);
                }
                return true;
            case Ellipse ellipse:
                AppendEllipse(ellipse, layerId, color, destination);
                return true;
            case Spline spline:
                AppendSpline(spline, layerId, color, destination);
                return true;
            case Hatch hatch:
                AppendHatch(hatch, layerId, color, destination, fills);
                return true;
            case Solid solid:
                AppendSolid(solid, layerId, color, destination, fills);
                return true;
            case Leader leader:
                AppendLeader(leader, layerId, color, destination);
                return true;
            case Dimension:
                return true;
            case MText mtext:
                AppendMText(mtext, layerId, color, destination);
                return true;
            case TextEntity text:
                AppendText(text, layerId, color, destination);
                return true;
            case Ray:
            case XLine:
            case ProxyEntity:
            case UnknownEntity:
                return true;
            default:
                return false;
        }
    }

    private static void AppendLwPolyline(
        LwPolyline polyline,
        int layerId,
        CadColorValue color,
        List<LocalSegment> destination)
    {
        var vertices = polyline.Vertices;
        int count = vertices?.Count ?? 0;
        if (count < 2 || vertices is null)
        {
            return;
        }
        double elevation = polyline.Elevation;
        Vector3 normal = CadMath.UsableNormal(polyline.Normal);
        int segmentCount = polyline.IsClosed ? count : count - 1;
        for (int index = 0; index < segmentCount; index++)
        {
            LwPolyline.Vertex current = vertices[index];
            LwPolyline.Vertex next = vertices[(index + 1) % count];
            if (current is null || next is null)
            {
                continue;
            }
            if (!TryLwVertex(current, elevation, polyline.Normal, out Vector3 start)
                || !TryLwVertex(next, elevation, polyline.Normal, out Vector3 end))
            {
                continue;
            }
            float bulge = (float)current.Bulge;
            if (MathF.Abs(bulge) <= BulgeTolerance)
            {
                Add(destination, start, end, layerId, color);
            }
            else
            {
                AppendBulge(
                    new Vector3(
                        (float)current.Location.X,
                        (float)current.Location.Y,
                        (float)elevation),
                    new Vector3(
                        (float)next.Location.X,
                        (float)next.Location.Y,
                        (float)elevation),
                    bulge,
                    normal,
                    layerId,
                    color,
                    destination);
            }
        }
    }

    private static bool TryLwVertex(
        LwPolyline.Vertex vertex,
        double elevation,
        CSMath.XYZ normal,
        out Vector3 world) =>
        CadMath.TryOcsToWcs(vertex.Location.X, vertex.Location.Y, elevation, normal, out world);

    private static void AppendBulge(
        Vector3 startOcs,
        Vector3 endOcs,
        float bulge,
        Vector3 normal,
        int layerId,
        CadColorValue color,
        List<LocalSegment> destination)
    {
        if (!CadMath.IsUsable(startOcs) || !CadMath.IsUsable(endOcs))
        {
            return;
        }
        Vector2 p0 = new(startOcs.X, startOcs.Y);
        Vector2 p1 = new(endOcs.X, endOcs.Y);
        Vector2 chord = p1 - p0;
        float length = chord.Length();
        float sweep = 4 * MathF.Atan(bulge);
        if (length <= float.Epsilon || MathF.Abs(sweep) <= BulgeTolerance)
        {
            Add(
                destination,
                CadMath.OcsToWcs(startOcs, normal),
                CadMath.OcsToWcs(endOcs, normal),
                layerId,
                color);
            return;
        }
        Vector2 midpoint = (p0 + p1) * 0.5f;
        Vector2 left = Vector2.Normalize(new Vector2(-chord.Y, chord.X));
        Vector2 center = midpoint + left * (length / (2 * MathF.Tan(sweep / 2)));
        float radius = Vector2.Distance(center, p0);
        float startAngle = MathF.Atan2(p0.Y - center.Y, p0.X - center.X);
        Vector3 worldCenter = CadMath.OcsToWcs(new Vector3(center, startOcs.Z), normal);
        AppendArc(
            worldCenter,
            normal,
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

    private static void AppendPolyline2D(
        Polyline2D polyline,
        int layerId,
        CadColorValue color,
        List<LocalSegment> destination)
    {
        var vertices = polyline.Vertices;
        int count = vertices?.Count ?? 0;
        if (count < 2 || vertices is null)
        {
            return;
        }
        double elevation = polyline.Elevation;
        Vector3 normal = CadMath.UsableNormal(polyline.Normal);
        int segmentCount = polyline.IsClosed ? count : count - 1;
        for (int index = 0; index < segmentCount; index++)
        {
            Vertex2D current = vertices[index];
            Vertex2D next = vertices[(index + 1) % count];
            if (current is null || next is null)
            {
                continue;
            }
            if (!CadMath.TryOcsToWcs(
                    current.Location.X,
                    current.Location.Y,
                    elevation,
                    polyline.Normal,
                    out Vector3 start)
                || !CadMath.TryOcsToWcs(
                    next.Location.X,
                    next.Location.Y,
                    elevation,
                    polyline.Normal,
                    out Vector3 end))
            {
                continue;
            }
            float bulge = (float)current.Bulge;
            if (MathF.Abs(bulge) <= BulgeTolerance)
            {
                Add(destination, start, end, layerId, color);
            }
            else
            {
                AppendBulge(
                    new Vector3(
                        (float)current.Location.X,
                        (float)current.Location.Y,
                        (float)elevation),
                    new Vector3(
                        (float)next.Location.X,
                        (float)next.Location.Y,
                        (float)elevation),
                    bulge,
                    normal,
                    layerId,
                    color,
                    destination);
            }
        }
    }

    private static void AppendPolyline3D(
        Polyline3D polyline,
        int layerId,
        CadColorValue color,
        List<LocalSegment> destination)
    {
        var vertices = polyline.Vertices;
        int count = vertices?.Count ?? 0;
        if (count < 2 || vertices is null)
        {
            return;
        }
        int segmentCount = polyline.IsClosed ? count : count - 1;
        for (int index = 0; index < segmentCount; index++)
        {
            Vertex3D current = vertices[index];
            Vertex3D next = vertices[(index + 1) % count];
            if (current is null || next is null)
            {
                continue;
            }
            if (!CadMath.TryWorldPoint(current.Location, out Vector3 start)
                || !CadMath.TryWorldPoint(next.Location, out Vector3 end))
            {
                continue;
            }
            Add(destination, start, end, layerId, color);
        }
    }

    private static void AppendEllipse(
        Ellipse ellipse,
        int layerId,
        CadColorValue color,
        List<LocalSegment> destination)
    {
        Vector3 center = CadMath.ToVector(ellipse.Center);
        Vector3 major = CadMath.ToVector(ellipse.MajorAxisEndPoint);
        Vector3 minor = CadMath.ToVector(ellipse.MinorAxisEndPoint);
        if (minor.LengthSquared() <= 1e-12f)
        {
            float minorLength = (float)(ellipse.RadiusRatio * ellipse.MajorAxis);
            if (minorLength > 0 && major.LengthSquared() > 0)
            {
                Vector3 normal = CadMath.UsableNormal(ellipse.Normal);
                CadMath.CreateOcsBasis(normal, out _, out _, out Vector3 axisZ);
                minor = Vector3.Normalize(Vector3.Cross(axisZ, major)) * minorLength;
            }
        }
        CurveTessellator.AppendEllipse(
            center,
            major,
            minor,
            (float)ellipse.StartParameter,
            (float)ellipse.EndParameter,
            ellipse.IsFullEllipse,
            layerId,
            color,
            destination);
    }

    private static void AppendSpline(
        Spline spline,
        int layerId,
        CadColorValue color,
        List<LocalSegment> destination)
    {
        int precision = SplinePrecision(spline);
        if (spline.TryPolygonalVertexes(precision, out List<CSMath.XYZ>? samples)
            && samples.Count >= 2)
        {
            CurveTessellator.AppendChain(
                ToVectors(samples),
                spline.IsClosed && samples.Count > 2,
                layerId,
                color,
                destination);
            return;
        }

        if (spline.ControlPoints is { Count: >= 2 })
        {
            CurveTessellator.AppendNurbs(
                ToVectors(spline.ControlPoints),
                CopyDoubles(spline.Knots),
                CopyDoubles(spline.Weights),
                spline.Degree,
                spline.IsClosed || spline.IsPeriodic,
                layerId,
                color,
                destination);
            return;
        }

        if (spline.FitPoints is { Count: >= 2 })
        {
            CurveTessellator.AppendChain(
                ToVectors(spline.FitPoints),
                spline.IsClosed,
                layerId,
                color,
                destination);
        }
    }

    private static void AppendHatch(
        Hatch hatch,
        int layerId,
        CadColorValue color,
        List<LocalSegment> destination,
        List<LocalTriangle>? fills)
    {
        CadMath.CreateOcsBasis(
            CadMath.UsableNormal(hatch.Normal),
            out Vector3 axisX,
            out Vector3 axisY,
            out Vector3 axisZ);
        Vector3 origin = axisZ * (float)hatch.Elevation;
        if (hatch.Paths is null)
        {
            return;
        }

        bool fill = fills is not null
            && (hatch.IsSolid
                || hatch.PatternType == HatchPatternType.SolidFill
                || hatch.GradientColor?.Enabled == true);
        var loops = fill ? new List<List<Vector3>>() : null;

        foreach (Hatch.BoundaryPath path in hatch.Paths)
        {
            if (path?.Edges is null)
            {
                continue;
            }
            if (fill)
            {
                var loop = new List<Vector3>();
                CollectHatchLoop(path, origin, axisX, axisY, loop);
                if (loop.Count >= 3)
                {
                    loops!.Add(loop);
                }
                continue;
            }
            if (path.IsPolyline)
            {
                foreach (Hatch.BoundaryPath.Edge? edge in path.Edges)
                {
                    if (edge is Hatch.BoundaryPath.Polyline polyline)
                    {
                        AppendHatchPolyline(
                            polyline,
                            origin,
                            axisX,
                            axisY,
                            layerId,
                            color,
                            destination);
                    }
                }
                continue;
            }

            foreach (Hatch.BoundaryPath.Edge? edge in path.Edges)
            {
                if (edge is null)
                {
                    continue;
                }
                switch (edge)
                {
                    case Hatch.BoundaryPath.Line line:
                        if (TryHatchPoint(line.Start, origin, axisX, axisY, out Vector3 hatchStart)
                            && TryHatchPoint(line.End, origin, axisX, axisY, out Vector3 hatchEnd))
                        {
                            Add(destination, hatchStart, hatchEnd, layerId, color);
                        }
                        break;
                    case Hatch.BoundaryPath.Polyline polyline:
                        AppendHatchPolyline(
                            polyline,
                            origin,
                            axisX,
                            axisY,
                            layerId,
                            color,
                            destination);
                        break;
                    case Hatch.BoundaryPath.Arc arc:
                        AppendHatchArc(arc, origin, axisX, axisY, layerId, color, destination);
                        break;
                    case Hatch.BoundaryPath.Ellipse ellipse:
                        AppendHatchEllipse(
                            ellipse,
                            origin,
                            axisX,
                            axisY,
                            layerId,
                            color,
                            destination);
                        break;
                    case Hatch.BoundaryPath.Spline spline:
                        AppendHatchSpline(
                            spline,
                            origin,
                            axisX,
                            axisY,
                            layerId,
                            color,
                            destination);
                        break;
                }
            }
        }

        if (!fill || loops is null || fills is null)
        {
            return;
        }
        ResolveHatchFillColors(hatch, color, out CadColorValue colorA, out CadColorValue colorB);
        IEnumerable<List<Vector3>> selected = hatch.Style == HatchStyleType.Outer
            && loops.Count > 1
            ? [loops.OrderByDescending(static loop => MathF.Abs(LoopArea(loop))).First()]
            : loops;
        foreach (List<Vector3> loop in selected)
        {
            HatchFillTessellator.Append(loop, layerId, colorA, colorB, colorA, fills);
        }
    }

    private static void CollectHatchLoop(
        Hatch.BoundaryPath path,
        Vector3 origin,
        Vector3 axisX,
        Vector3 axisY,
        List<Vector3> loop)
    {
        var edges = new List<LocalSegment>();
        if (path.IsPolyline)
        {
            foreach (Hatch.BoundaryPath.Edge? edge in path.Edges)
            {
                if (edge is Hatch.BoundaryPath.Polyline polyline)
                {
                    AppendHatchPolyline(polyline, origin, axisX, axisY, 0, default, edges);
                }
            }
        }
        else
        {
            foreach (Hatch.BoundaryPath.Edge? edge in path.Edges)
            {
                switch (edge)
                {
                    case Hatch.BoundaryPath.Line line:
                        if (TryHatchPoint(line.Start, origin, axisX, axisY, out Vector3 hatchStart)
                            && TryHatchPoint(line.End, origin, axisX, axisY, out Vector3 hatchEnd))
                        {
                            Add(edges, hatchStart, hatchEnd, 0, default);
                        }
                        break;
                    case Hatch.BoundaryPath.Polyline polyline:
                        AppendHatchPolyline(polyline, origin, axisX, axisY, 0, default, edges);
                        break;
                    case Hatch.BoundaryPath.Arc arc:
                        AppendHatchArc(arc, origin, axisX, axisY, 0, default, edges);
                        break;
                    case Hatch.BoundaryPath.Ellipse ellipse:
                        AppendHatchEllipse(ellipse, origin, axisX, axisY, 0, default, edges);
                        break;
                    case Hatch.BoundaryPath.Spline spline:
                        AppendHatchSpline(spline, origin, axisX, axisY, 0, default, edges);
                        break;
                }
            }
        }
        foreach (LocalSegment segment in edges)
        {
            if (loop.Count == 0)
            {
                loop.Add(segment.Start);
            }
            loop.Add(segment.End);
        }
    }

    private static void ResolveHatchFillColors(
        Hatch hatch,
        CadColorValue fallback,
        out CadColorValue colorA,
        out CadColorValue colorB)
    {
        colorA = fallback;
        colorB = fallback;
        HatchGradientPattern? gradient = hatch.GradientColor;
        if (gradient?.Enabled != true || gradient.Colors is null || gradient.Colors.Count == 0)
        {
            return;
        }
        try
        {
            GradientColor first = gradient.Colors[0];
            colorA = CadColorResolver.FromCadColor(first.Color);
            colorB = gradient.Colors.Count > 1
                ? CadColorResolver.FromCadColor(gradient.Colors[^1].Color)
                : colorA;
        }
        catch (Exception)
        {
            colorA = fallback;
            colorB = fallback;
        }
    }

    private static float LoopArea(IReadOnlyList<Vector3> loop)
    {
        float area = 0;
        for (int i = 0; i < loop.Count; i++)
        {
            Vector3 a = loop[i];
            Vector3 b = loop[(i + 1) % loop.Count];
            area += (a.X * b.Y) - (b.X * a.Y);
        }
        return area * 0.5f;
    }

    private static void AppendSolid(
        Solid solid,
        int layerId,
        CadColorValue color,
        List<LocalSegment> destination,
        List<LocalTriangle>? fills)
    {
        if (!CadMath.TryOcsToWcs(solid.FirstCorner, solid.Normal, out Vector3 a)
            || !CadMath.TryOcsToWcs(solid.SecondCorner, solid.Normal, out Vector3 b)
            || !CadMath.TryOcsToWcs(solid.ThirdCorner, solid.Normal, out Vector3 c))
        {
            return;
        }
        Vector3 d = CadMath.TryOcsToWcs(solid.FourthCorner, solid.Normal, out Vector3 fourth)
            ? fourth
            : c;
        if (fills is not null)
        {
            HatchFillTessellator.Append([a, b, d, c], layerId, color, color, color, fills);
            return;
        }
        Add(destination, a, b, layerId, color);
        Add(destination, b, d, layerId, color);
        Add(destination, d, c, layerId, color);
        Add(destination, c, a, layerId, color);
    }

    private static void AppendLeader(
        Leader leader,
        int layerId,
        CadColorValue color,
        List<LocalSegment> destination)
    {
        var vertices = leader.Vertices;
        int count = vertices?.Count ?? 0;
        if (count < 2 || vertices is null)
        {
            return;
        }
        var points = new List<Vector3>(count);
        foreach (CSMath.XYZ vertex in vertices)
        {
            if (!CadMath.TryWorldPoint(vertex, out Vector3 world))
            {
                continue;
            }
            points.Add(world);
        }
        if (points.Count < 2)
        {
            return;
        }
        Vector3 centroid = Vector3.Zero;
        foreach (Vector3 point in points)
        {
            centroid += point;
        }
        centroid /= points.Count;
        var filtered = new List<Vector3>(points.Count);
        foreach (Vector3 point in points)
        {
            if (CadMath.IsOrigin(point)
                && Vector3.DistanceSquared(point, centroid) > 1f)
            {
                continue;
            }
            filtered.Add(point);
        }
        if (filtered.Count < 2)
        {
            return;
        }
        for (int i = 0; i < filtered.Count - 1; i++)
        {
            Add(destination, filtered[i], filtered[i + 1], layerId, color);
        }
        if (leader.ArrowHeadEnabled && filtered.Count >= 2)
        {
            AppendLeaderArrow(filtered[0], filtered[1], layerId, color, destination);
        }
    }

    private static void AppendLeaderArrow(
        Vector3 tip,
        Vector3 along,
        int layerId,
        CadColorValue color,
        List<LocalSegment> destination)
    {
        Vector3 direction = along - tip;
        if (direction.LengthSquared() <= 1e-8f)
        {
            return;
        }
        direction = Vector3.Normalize(direction);
        Vector3 side = new(-direction.Y, direction.X, 0);
        float size = MathF.Min(Vector3.Distance(tip, along) * 0.15f, 2.5f);
        if (size <= 1e-4f)
        {
            return;
        }
        Vector3 left = tip + direction * size + side * size * 0.35f;
        Vector3 right = tip + direction * size - side * size * 0.35f;
        Add(destination, tip, left, layerId, color);
        Add(destination, tip, right, layerId, color);
        Add(destination, left, right, layerId, color);
    }

    private static void AppendHatchPolyline(
        Hatch.BoundaryPath.Polyline polyline,
        Vector3 origin,
        Vector3 axisX,
        Vector3 axisY,
        int layerId,
        CadColorValue color,
        List<LocalSegment> destination)
    {
        var vertices = polyline.Vertices;
        int count = vertices?.Count ?? 0;
        if (count < 2 || vertices is null)
        {
            return;
        }
        int segmentCount = polyline.IsClosed ? count : count - 1;
        IReadOnlyList<double> bulges = CopyDoubles(polyline.Bulges);
        for (int index = 0; index < segmentCount; index++)
        {
            CSMath.XYZ current = vertices[index];
            CSMath.XYZ next = vertices[(index + 1) % count];
            if (!TryHatchPoint(
                    new CSMath.XYZ(current.X, current.Y, 0),
                    origin,
                    axisX,
                    axisY,
                    out Vector3 start)
                || !TryHatchPoint(
                    new CSMath.XYZ(next.X, next.Y, 0),
                    origin,
                    axisX,
                    axisY,
                    out Vector3 end))
            {
                continue;
            }
            float bulge = bulges.Count == count
                ? (float)bulges[index]
                : (float)current.Z;
            if (MathF.Abs(bulge) <= BulgeTolerance)
            {
                Add(destination, start, end, layerId, color);
            }
            else
            {
                AppendBulge(
                    new Vector3((float)current.X, (float)current.Y, 0),
                    new Vector3((float)next.X, (float)next.Y, 0),
                    bulge,
                    Vector3.Normalize(Vector3.Cross(axisX, axisY)),
                    layerId,
                    color,
                    destination);
            }
        }
    }

    private static void AppendHatchArc(
        Hatch.BoundaryPath.Arc arc,
        Vector3 origin,
        Vector3 axisX,
        Vector3 axisY,
        int layerId,
        CadColorValue color,
        List<LocalSegment> destination)
    {
        if (!TryHatchPoint(arc.Center, origin, axisX, axisY, out Vector3 center))
        {
            return;
        }
        float start = (float)arc.StartAngle;
        float end = (float)arc.EndAngle;
        float sweep = arc.CounterClockWise
            ? PositiveSweep(start, end)
            : -PositiveSweep(end, start);
        AppendArc(
            center,
            Vector3.Normalize(Vector3.Cross(axisX, axisY)),
            (float)arc.Radius,
            start,
            sweep,
            layerId,
            color,
            destination);
    }

    private static void AppendHatchEllipse(
        Hatch.BoundaryPath.Ellipse ellipse,
        Vector3 origin,
        Vector3 axisX,
        Vector3 axisY,
        int layerId,
        CadColorValue color,
        List<LocalSegment> destination)
    {
        if (!TryHatchPoint(ellipse.Center, origin, axisX, axisY, out Vector3 center))
        {
            return;
        }
        Vector3 major = axisX * (float)ellipse.MajorAxisEndPoint.X
            + axisY * (float)ellipse.MajorAxisEndPoint.Y;
        if (major.LengthSquared() <= 1e-12f)
        {
            float length = (float)ellipse.MajorAxis;
            float rotation = (float)ellipse.Rotation;
            major = (axisX * MathF.Cos(rotation) + axisY * MathF.Sin(rotation)) * length;
        }
        float minorLength = (float)(ellipse.RadiusRatio * major.Length());
        if (minorLength <= 0 && ellipse.MinorAxis > 0)
        {
            minorLength = (float)ellipse.MinorAxis;
        }
        Vector3 plane = Vector3.Normalize(Vector3.Cross(axisX, axisY));
        Vector3 minor = major.LengthSquared() > 0
            ? Vector3.Normalize(Vector3.Cross(plane, major)) * minorLength
            : axisY * minorLength;
        if (!ellipse.CounterClockWise)
        {
            minor = -minor;
        }
        float start = (float)ellipse.StartAngle;
        float end = (float)ellipse.EndAngle;
        bool closed = MathF.Abs(PositiveSweep(start, end) - TwoPi) <= 1e-4f
            || MathF.Abs(end - start) <= 1e-4f;
        CurveTessellator.AppendEllipse(
            center,
            major,
            minor,
            start,
            end,
            closed,
            layerId,
            color,
            destination);
    }

    private static void AppendHatchSpline(
        Hatch.BoundaryPath.Spline spline,
        Vector3 origin,
        Vector3 axisX,
        Vector3 axisY,
        int layerId,
        CadColorValue color,
        List<LocalSegment> destination)
    {
        int precision = Math.Clamp(
            CurveTessellator.AdaptiveSegmentCount(
                EstimateHatchSplineLength(spline, origin, axisX, axisY)),
            CurveTessellator.MinSegments,
            CurveTessellator.MaxSegments);
        List<CSMath.XYZ>? samples = null;
        try
        {
            samples = spline.PolygonalVertexes(precision);
        }
        catch (Exception)
        {
            samples = null;
        }
        if (samples is { Count: >= 2 })
        {
            var points = new List<Vector3>(samples.Count);
            foreach (CSMath.XYZ sample in samples)
            {
                if (TryHatchPoint(
                        new CSMath.XYZ(sample.X, sample.Y, 0),
                        origin,
                        axisX,
                        axisY,
                        out Vector3 world))
                {
                    points.Add(world);
                }
            }
            CurveTessellator.AppendChain(
                points,
                spline.IsPeriodic,
                layerId,
                color,
                destination);
            return;
        }

        if (spline.ControlPoints is null || spline.ControlPoints.Count == 0)
        {
            return;
        }
        var controls = new List<Vector3>(spline.ControlPoints.Count);
        var weights = new List<double>(spline.ControlPoints.Count);
        foreach (CSMath.XYZ control in spline.ControlPoints)
        {
            if (!TryHatchPoint(
                    new CSMath.XYZ(control.X, control.Y, 0),
                    origin,
                    axisX,
                    axisY,
                    out Vector3 world))
            {
                continue;
            }
            controls.Add(world);
            weights.Add(control.Z > 0 ? control.Z : 1);
        }
        if (controls.Count >= 2)
        {
            IReadOnlyList<double> splineWeights = CopyDoubles(spline.Weights);
            CurveTessellator.AppendNurbs(
                controls,
                CopyDoubles(spline.Knots),
                splineWeights.Count == controls.Count
                    ? splineWeights
                    : weights,
                spline.Degree,
                spline.IsPeriodic,
                layerId,
                color,
                destination);
        }
    }

    private static void AppendText(
        TextEntity text,
        int layerId,
        CadColorValue color,
        List<LocalSegment> destination)
    {
        string value = text.Value ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }
        if (!CadMath.TryOcsToWcs(text.InsertPoint, text.Normal, out Vector3 insert))
        {
            return;
        }
        bool useAlignment = text.HorizontalAlignment != TextHorizontalAlignment.Left
            || text.VerticalAlignment != TextVerticalAlignmentType.Baseline;
        Vector3 origin = insert;
        if (useAlignment
            && CadMath.TryOcsToWcs(text.AlignmentPoint, text.Normal, out Vector3 aligned)
            && CadMath.PreferExplicitPoint(aligned, insert))
        {
            origin = aligned;
        }
        CreateTextAxes(
            CadMath.UsableNormal(text.Normal),
            (float)text.Rotation,
            out Vector3 axisX,
            out Vector3 axisY);
        float alignX = text.HorizontalAlignment switch
        {
            TextHorizontalAlignment.Center
                or TextHorizontalAlignment.Middle
                or TextHorizontalAlignment.Fit
                or TextHorizontalAlignment.Aligned => 0.5f,
            TextHorizontalAlignment.Right => 1f,
            _ => 0f,
        };
        float alignY = text.VerticalAlignment switch
        {
            TextVerticalAlignmentType.Top => 1f,
            TextVerticalAlignmentType.Middle => 0.5f,
            TextVerticalAlignmentType.Bottom => 0.15f,
            _ => 0f,
        };
        StrokeFont.AppendLabel(
            [value],
            origin,
            axisX,
            axisY,
            (float)Math.Max(text.Height, 1e-4),
            text.WidthFactor > 0
                ? (float)text.WidthFactor
                : text.Style?.Width > 0 ? (float)text.Style.Width : 1f,
            (float)(text.ObliqueAngle != 0 ? text.ObliqueAngle : text.Style?.ObliqueAngle ?? 0),
            alignX,
            alignY,
            0,
            layerId,
            color,
            destination,
            style: text.Style);
    }

    private static void AppendMText(
        MText mtext,
        int layerId,
        CadColorValue color,
        List<LocalSegment> destination)
    {
        string[] lines;
        try
        {
            lines = mtext.GetPlainTextLines() ?? [];
        }
        catch (Exception)
        {
            lines = [];
        }
        if (lines.Length == 0 || lines.All(string.IsNullOrWhiteSpace))
        {
            string plain;
            try
            {
                plain = mtext.PlainText;
            }
            catch (Exception)
            {
                return;
            }
            if (string.IsNullOrWhiteSpace(plain))
            {
                return;
            }
            lines = [plain];
        }
        if (!CadMath.TryOcsToWcs(mtext.InsertPoint, mtext.Normal, out Vector3 origin))
        {
            return;
        }
        CreateTextAxes(
            CadMath.UsableNormal(mtext.Normal),
            (float)mtext.Rotation,
            out Vector3 axisX,
            out Vector3 axisY);
        AttachmentAlign(mtext.AttachmentPoint, out float alignX, out float alignY);
        float wrap = mtext.RectangleWidth > 0 ? (float)mtext.RectangleWidth : 0f;
        float widthFactor = mtext.Style?.Width > 0 ? (float)mtext.Style.Width : 1f;
        float spacing = (float)(mtext.LineSpacing > 0 ? mtext.LineSpacing : 1);
        StrokeFont.AppendLabel(
            lines,
            origin,
            axisX,
            axisY,
            (float)Math.Max(mtext.Height, 1e-4),
            widthFactor,
            (float)(mtext.Style?.ObliqueAngle ?? 0),
            alignX,
            alignY,
            wrap,
            layerId,
            color,
            destination,
            HasMTextFrame(mtext),
            spacing,
            mtext.Style);
    }

    private static bool HasMTextFrame(MText mtext) =>
        (mtext.BackgroundFillFlags & (
            BackgroundFillFlags.UseBackgroundFillColor
            | BackgroundFillFlags.UseDrawingWindowColor
            | BackgroundFillFlags.TextFrame)) != 0;

    private static void CreateTextAxes(
        Vector3 normal,
        float rotation,
        out Vector3 axisX,
        out Vector3 axisY)
    {
        CadMath.CreateOcsBasis(normal, out Vector3 ocsX, out Vector3 ocsY, out _);
        float cos = MathF.Cos(rotation);
        float sin = MathF.Sin(rotation);
        axisX = ocsX * cos + ocsY * sin;
        axisY = -ocsX * sin + ocsY * cos;
    }

    private static void AttachmentAlign(
        AttachmentPointType attachment,
        out float alignX,
        out float alignY)
    {
        alignX = attachment switch
        {
            AttachmentPointType.TopCenter
                or AttachmentPointType.MiddleCenter
                or AttachmentPointType.BottomCenter => 0.5f,
            AttachmentPointType.TopRight
                or AttachmentPointType.MiddleRight
                or AttachmentPointType.BottomRight => 1f,
            _ => 0f,
        };
        alignY = attachment switch
        {
            AttachmentPointType.TopLeft
                or AttachmentPointType.TopCenter
                or AttachmentPointType.TopRight => 1f,
            AttachmentPointType.MiddleLeft
                or AttachmentPointType.MiddleCenter
                or AttachmentPointType.MiddleRight => 0.5f,
            _ => 0f,
        };
    }

    private static int SplinePrecision(Spline spline)
    {
        float length = 0;
        CSMath.XYZ? previous = null;
        foreach (CSMath.XYZ point in (spline.ControlPoints?.Count ?? 0) >= 2
            ? spline.ControlPoints!
            : spline.FitPoints ?? [])
        {
            Vector3 current = CadMath.ToVector(point);
            if (previous is CSMath.XYZ last)
            {
                length += Vector3.Distance(CadMath.ToVector(last), current);
            }
            previous = point;
        }
        return CurveTessellator.AdaptiveSegmentCount(length);
    }

    private static float EstimateHatchSplineLength(
        Hatch.BoundaryPath.Spline spline,
        Vector3 origin,
        Vector3 axisX,
        Vector3 axisY)
    {
        float length = 0;
        Vector3? previous = null;
        foreach (CSMath.XYZ point in spline.ControlPoints ?? [])
        {
            if (!TryHatchPoint(
                    new CSMath.XYZ(point.X, point.Y, 0),
                    origin,
                    axisX,
                    axisY,
                    out Vector3 current))
            {
                continue;
            }
            if (previous is Vector3 last)
            {
                length += Vector3.Distance(last, current);
            }
            previous = current;
        }
        return length;
    }

    private static List<Vector3> ToVectors(IEnumerable<CSMath.XYZ>? points)
    {
        var result = new List<Vector3>();
        if (points is null)
        {
            return result;
        }
        foreach (CSMath.XYZ point in points)
        {
            if (CadMath.TryWorldPoint(point, out Vector3 vector))
            {
                result.Add(vector);
            }
        }
        return result;
    }

    private static List<double> CopyDoubles(IEnumerable<double>? values)
    {
        var result = new List<double>();
        if (values is null)
        {
            return result;
        }
        foreach (double value in values)
        {
            result.Add(value);
        }
        return result;
    }

    private static bool TryHatchPoint(
        CSMath.XY point,
        Vector3 origin,
        Vector3 axisX,
        Vector3 axisY,
        out Vector3 world)
    {
        world = default;
        if (!CadMath.IsUsable(point.X, point.Y, 0))
        {
            return false;
        }
        world = origin + axisX * (float)point.X + axisY * (float)point.Y;
        return CadMath.IsUsable(world);
    }

    private static bool TryHatchPoint(
        CSMath.XYZ point,
        Vector3 origin,
        Vector3 axisX,
        Vector3 axisY,
        out Vector3 world)
    {
        world = default;
        if (!CadMath.IsUsable(point.X, point.Y, 0))
        {
            return false;
        }
        world = origin + axisX * (float)point.X + axisY * (float)point.Y;
        return CadMath.IsUsable(world);
    }

    private static void Add(
        List<LocalSegment> destination,
        Vector3 start,
        Vector3 end,
        int layerId,
        CadColorValue color)
    {
        if (CadMath.IsUsable(start) && CadMath.IsUsable(end) && start != end)
        {
            destination.Add(new LocalSegment(start, end, layerId, color));
        }
    }

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
