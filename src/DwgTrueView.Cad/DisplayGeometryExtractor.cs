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
                AppendLwPolyline(polyline, layerId, color, destination);
                return true;
            case Polyline2D polyline:
                AppendHeavyPolyline(polyline, layerId, color, destination);
                return true;
            case Polyline3D polyline:
                AppendHeavyPolyline(polyline, layerId, color, destination);
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
            case Ellipse ellipse:
                AppendEllipse(ellipse, layerId, color, destination);
                return true;
            case Spline spline:
                AppendSpline(spline, layerId, color, destination);
                return true;
            case Hatch hatch:
                AppendHatch(hatch, layerId, color, destination);
                return true;
            case MText mtext:
                AppendMText(mtext, layerId, color, destination);
                return true;
            case TextEntity text:
                AppendText(text, layerId, color, destination);
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

    private static void AppendHeavyPolyline<TVertex>(
        Polyline<TVertex> polyline,
        int layerId,
        CadColorValue color,
        List<LocalSegment> destination)
        where TVertex : Vertex
    {
        int count = polyline.Vertices.Count;
        if (count < 2)
        {
            return;
        }
        int segmentCount = polyline.IsClosed ? count : count - 1;
        for (int index = 0; index < segmentCount; index++)
        {
            TVertex current = polyline.Vertices[index];
            TVertex next = polyline.Vertices[(index + 1) % count];
            Vector3 start = CadMath.ToVector(current.Location);
            Vector3 end = CadMath.ToVector(next.Location);
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
                Vector3 normal = CadMath.ToVector(ellipse.Normal);
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

        if (spline.ControlPoints.Count >= 2)
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

        if (spline.FitPoints.Count >= 2)
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
        List<LocalSegment> destination)
    {
        CadMath.CreateOcsBasis(
            CadMath.ToVector(hatch.Normal),
            out Vector3 axisX,
            out Vector3 axisY,
            out Vector3 axisZ);
        Vector3 origin = axisZ * (float)hatch.Elevation;
        foreach (Hatch.BoundaryPath path in hatch.Paths)
        {
            if (path.IsPolyline)
            {
                foreach (Hatch.BoundaryPath.Edge edge in path.Edges)
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

            foreach (Hatch.BoundaryPath.Edge edge in path.Edges)
            {
                switch (edge)
                {
                    case Hatch.BoundaryPath.Line line:
                        Add(
                            destination,
                            FromOcs(line.Start, origin, axisX, axisY),
                            FromOcs(line.End, origin, axisX, axisY),
                            layerId,
                            color);
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
        int count = polyline.Vertices.Count;
        if (count < 2)
        {
            return;
        }
        int segmentCount = polyline.IsClosed ? count : count - 1;
        IReadOnlyList<double> bulges = CopyDoubles(polyline.Bulges);
        for (int index = 0; index < segmentCount; index++)
        {
            CSMath.XYZ current = polyline.Vertices[index];
            CSMath.XYZ next = polyline.Vertices[(index + 1) % count];
            Vector3 start = FromOcs(new CSMath.XYZ(current.X, current.Y, 0), origin, axisX, axisY);
            Vector3 end = FromOcs(new CSMath.XYZ(next.X, next.Y, 0), origin, axisX, axisY);
            float bulge = bulges.Count == count
                ? (float)bulges[index]
                : (float)current.Z;
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

    private static void AppendHatchArc(
        Hatch.BoundaryPath.Arc arc,
        Vector3 origin,
        Vector3 axisX,
        Vector3 axisY,
        int layerId,
        CadColorValue color,
        List<LocalSegment> destination)
    {
        float start = (float)arc.StartAngle;
        float end = (float)arc.EndAngle;
        float sweep = arc.CounterClockWise
            ? PositiveSweep(start, end)
            : -PositiveSweep(end, start);
        AppendArc(
            FromOcs(arc.Center, origin, axisX, axisY),
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
        Vector3 center = FromOcs(ellipse.Center, origin, axisX, axisY);
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
        List<CSMath.XYZ> samples = spline.PolygonalVertexes(precision);
        if (samples.Count >= 2)
        {
            var points = new List<Vector3>(samples.Count);
            foreach (CSMath.XYZ sample in samples)
            {
                points.Add(FromOcs(new CSMath.XYZ(sample.X, sample.Y, 0), origin, axisX, axisY));
            }
            CurveTessellator.AppendChain(
                points,
                spline.IsPeriodic,
                layerId,
                color,
                destination);
            return;
        }

        var controls = new List<Vector3>(spline.ControlPoints.Count);
        var weights = new List<double>(spline.ControlPoints.Count);
        foreach (CSMath.XYZ control in spline.ControlPoints)
        {
            controls.Add(FromOcs(new CSMath.XYZ(control.X, control.Y, 0), origin, axisX, axisY));
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
        bool useAlignment = text.HorizontalAlignment != TextHorizontalAlignment.Left
            || text.VerticalAlignment != TextVerticalAlignmentType.Baseline;
        Vector3 origin = CadMath.ToVector(
            useAlignment ? text.AlignmentPoint : text.InsertPoint);
        CreateTextAxes(
            CadMath.ToVector(text.Normal),
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
            text.WidthFactor > 0 ? (float)text.WidthFactor : 1f,
            (float)text.ObliqueAngle,
            alignX,
            alignY,
            0,
            layerId,
            color,
            destination);
    }

    private static void AppendMText(
        MText mtext,
        int layerId,
        CadColorValue color,
        List<LocalSegment> destination)
    {
        string[] lines = mtext.GetPlainTextLines();
        if (lines.Length == 0 || lines.All(string.IsNullOrWhiteSpace))
        {
            string plain = mtext.PlainText;
            if (string.IsNullOrWhiteSpace(plain))
            {
                return;
            }
            lines = [plain];
        }
        Vector3 origin = CadMath.ToVector(mtext.InsertPoint);
        CreateTextAxes(
            CadMath.ToVector(mtext.Normal),
            (float)mtext.Rotation,
            out Vector3 axisX,
            out Vector3 axisY);
        AttachmentAlign(mtext.AttachmentPoint, out float alignX, out float alignY);
        float wrap = mtext.RectangleWidth > 0
            ? (float)mtext.RectangleWidth
            : (float)mtext.HorizontalWidth;
        StrokeFont.AppendLabel(
            lines,
            origin,
            axisX,
            axisY,
            (float)Math.Max(mtext.Height, 1e-4),
            1f,
            0,
            alignX,
            alignY,
            wrap,
            layerId,
            color,
            destination);
    }

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
        foreach (CSMath.XYZ point in spline.ControlPoints.Count >= 2
            ? spline.ControlPoints
            : spline.FitPoints)
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
        foreach (CSMath.XYZ point in spline.ControlPoints)
        {
            Vector3 current = FromOcs(new CSMath.XYZ(point.X, point.Y, 0), origin, axisX, axisY);
            if (previous is Vector3 last)
            {
                length += Vector3.Distance(last, current);
            }
            previous = current;
        }
        return length;
    }

    private static List<Vector3> ToVectors(IEnumerable<CSMath.XYZ> points)
    {
        var result = new List<Vector3>();
        foreach (CSMath.XYZ point in points)
        {
            result.Add(CadMath.ToVector(point));
        }
        return result;
    }

    private static List<double> CopyDoubles(IEnumerable<double> values)
    {
        var result = new List<double>();
        foreach (double value in values)
        {
            result.Add(value);
        }
        return result;
    }

    private static Vector3 FromOcs(
        CSMath.XY point,
        Vector3 origin,
        Vector3 axisX,
        Vector3 axisY) =>
        origin
        + axisX * (float)point.X
        + axisY * (float)point.Y;

    private static Vector3 FromOcs(
        CSMath.XYZ point,
        Vector3 origin,
        Vector3 axisX,
        Vector3 axisY) =>
        origin
        + axisX * (float)point.X
        + axisY * (float)point.Y;

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
