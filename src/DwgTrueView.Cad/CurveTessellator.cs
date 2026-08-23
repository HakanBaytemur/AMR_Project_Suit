using System.Numerics;

namespace DwgTrueView.Cad;

/// <summary>
/// Ingest-time tessellation of NURBS/B-splines and ellipses into line-list
/// segments. Segment density follows a drawing-unit view tolerance and is
/// capped so the GPU batch stays cheap during 60 FPS navigation.
/// </summary>
internal static class CurveTessellator
{
    public const float TwoPi = MathF.PI * 2;
    public const float ViewTolerance = 0.35f;
    public const int MinSegments = 8;
    public const int MaxSegments = 96;

    public static int AdaptiveSegmentCount(
        float characteristicLength,
        float sweepRadians = TwoPi)
    {
        float ratio = Math.Clamp(MathF.Abs(sweepRadians) / TwoPi, 0.05f, 1f);
        float travel = MathF.Max(characteristicLength * ratio, ViewTolerance);
        return Math.Clamp(
            (int)MathF.Ceiling(travel / ViewTolerance),
            MinSegments,
            MaxSegments);
    }

    public static void AppendEllipse(
        Vector3 center,
        Vector3 major,
        Vector3 minor,
        float startParameter,
        float endParameter,
        bool closed,
        int layerId,
        CadColorValue color,
        List<LocalSegment> destination)
    {
        float majorLength = major.Length();
        float minorLength = minor.Length();
        if (majorLength <= float.Epsilon && minorLength <= float.Epsilon)
        {
            return;
        }
        if (minorLength <= float.Epsilon)
        {
            Vector3 start = center + major * MathF.Cos(startParameter);
            Vector3 end = center + major * MathF.Cos(endParameter);
            Add(destination, start, end, layerId, color);
            return;
        }

        float sweep = closed
            ? TwoPi
            : ParameterSweep(startParameter, endParameter);
        int count = AdaptiveSegmentCount(
            TwoPi * MathF.Max(majorLength, minorLength),
            sweep);
        Vector3 previous = EllipsePoint(center, major, minor, startParameter);
        int steps = closed ? count : count;
        for (int index = 1; index <= steps; index++)
        {
            float t = startParameter + sweep * index / count;
            if (!closed && index == count)
            {
                t = startParameter + sweep;
            }
            Vector3 current = EllipsePoint(center, major, minor, t);
            Add(destination, previous, current, layerId, color);
            previous = current;
        }
    }

    public static void AppendNurbs(
        IReadOnlyList<Vector3> controlPoints,
        IReadOnlyList<double> knots,
        IReadOnlyList<double> weights,
        int degree,
        bool closed,
        int layerId,
        CadColorValue color,
        List<LocalSegment> destination)
    {
        if (controlPoints.Count == 0)
        {
            return;
        }
        if (controlPoints.Count == 1 || degree <= 0)
        {
            if (closed && controlPoints.Count == 1)
            {
                return;
            }
            AppendChain(controlPoints, closed, layerId, color, destination);
            return;
        }

        int p = Math.Clamp(degree, 1, 10);
        Vector3[] controls = controlPoints.ToArray();
        double[] knotVector = NormalizeKnots(controls.Length, p, knots);
        double[] weightVector = NormalizeWeights(controls.Length, weights);
        float polygonLength = ControlPolygonLength(controls);
        int count = AdaptiveSegmentCount(
            polygonLength,
            closed ? TwoPi : MathF.PI);
        count = Math.Clamp(count, MinSegments, Math.Max(MinSegments, controls.Length * (p + 1)));
        count = Math.Min(count, MaxSegments);

        double u0 = knotVector[p];
        double u1 = knotVector[controls.Length];
        if (u1 - u0 <= 1e-12)
        {
            AppendChain(controls, closed, layerId, color, destination);
            return;
        }

        if (!TryEvaluate(controls, knotVector, weightVector, p, u0, out Vector3 previous))
        {
            AppendChain(controls, closed, layerId, color, destination);
            return;
        }

        for (int index = 1; index <= count; index++)
        {
            double u = index == count
                ? u1
                : u0 + (u1 - u0) * index / count;
            if (!TryEvaluate(controls, knotVector, weightVector, p, u, out Vector3 current))
            {
                continue;
            }
            Add(destination, previous, current, layerId, color);
            previous = current;
        }

        if (closed)
        {
            if (TryEvaluate(controls, knotVector, weightVector, p, u0, out Vector3 first))
            {
                Add(destination, previous, first, layerId, color);
            }
        }
    }

    public static void AppendChain(
        IReadOnlyList<Vector3> points,
        bool closed,
        int layerId,
        CadColorValue color,
        List<LocalSegment> destination)
    {
        Vector3? first = null;
        Vector3? previous = null;
        foreach (Vector3 point in points)
        {
            if (!IsFinite(point))
            {
                previous = null;
                continue;
            }
            first ??= point;
            if (previous is Vector3 start)
            {
                Add(destination, start, point, layerId, color);
            }
            previous = point;
        }
        if (closed && first is Vector3 closeStart && previous is Vector3 closeEnd)
        {
            Add(destination, closeEnd, closeStart, layerId, color);
        }
    }

    public static bool TryEvaluate(
        ReadOnlySpan<Vector3> controls,
        ReadOnlySpan<double> knots,
        ReadOnlySpan<double> weights,
        int degree,
        double parameter,
        out Vector3 point)
    {
        point = default;
        int n = controls.Length;
        if (n == 0 || degree < 1 || knots.Length < n + degree + 1)
        {
            return false;
        }

        double u0 = knots[degree];
        double u1 = knots[n];
        double u = Math.Clamp(parameter, u0, u1);
        int span = FindSpan(n, degree, u, knots);

        Span<double> homogeneousX = stackalloc double[degree + 1];
        Span<double> homogeneousY = stackalloc double[degree + 1];
        Span<double> homogeneousZ = stackalloc double[degree + 1];
        Span<double> homogeneousW = stackalloc double[degree + 1];
        for (int j = 0; j <= degree; j++)
        {
            int i = span - degree + j;
            if ((uint)i >= (uint)n)
            {
                return false;
            }
            double weight = i < weights.Length ? weights[i] : 1;
            if (weight <= 0 || !double.IsFinite(weight))
            {
                weight = 1;
            }
            Vector3 control = controls[i];
            homogeneousX[j] = control.X * weight;
            homogeneousY[j] = control.Y * weight;
            homogeneousZ[j] = control.Z * weight;
            homogeneousW[j] = weight;
        }

        for (int r = 1; r <= degree; r++)
        {
            for (int j = degree; j >= r; j--)
            {
                int knotIndex = span - degree + j;
                double denom = knots[knotIndex + degree + 1 - r] - knots[knotIndex];
                double alpha = Math.Abs(denom) <= 1e-14
                    ? 0
                    : (u - knots[knotIndex]) / denom;
                alpha = Math.Clamp(alpha, 0, 1);
                homogeneousX[j] = (1 - alpha) * homogeneousX[j - 1] + alpha * homogeneousX[j];
                homogeneousY[j] = (1 - alpha) * homogeneousY[j - 1] + alpha * homogeneousY[j];
                homogeneousZ[j] = (1 - alpha) * homogeneousZ[j - 1] + alpha * homogeneousZ[j];
                homogeneousW[j] = (1 - alpha) * homogeneousW[j - 1] + alpha * homogeneousW[j];
            }
        }

        double w = homogeneousW[degree];
        if (Math.Abs(w) <= 1e-14 || !double.IsFinite(w))
        {
            return false;
        }
        point = new Vector3(
            (float)(homogeneousX[degree] / w),
            (float)(homogeneousY[degree] / w),
            (float)(homogeneousZ[degree] / w));
        return float.IsFinite(point.X)
            && float.IsFinite(point.Y)
            && float.IsFinite(point.Z);
    }

    private static int FindSpan(
        int controlCount,
        int degree,
        double u,
        ReadOnlySpan<double> knots)
    {
        if (u >= knots[controlCount])
        {
            return controlCount - 1;
        }
        if (u <= knots[degree])
        {
            return degree;
        }
        int low = degree;
        int high = controlCount;
        int mid = (low + high) / 2;
        while (u < knots[mid] || u >= knots[mid + 1])
        {
            if (u < knots[mid])
            {
                high = mid;
            }
            else
            {
                low = mid;
            }
            mid = (low + high) / 2;
        }
        return mid;
    }

    private static double[] NormalizeKnots(
        int controlCount,
        int degree,
        IReadOnlyList<double> knots)
    {
        int expected = controlCount + degree + 1;
        if (knots.Count == expected && IsMonotone(knots))
        {
            var copy = new double[expected];
            for (int index = 0; index < expected; index++)
            {
                copy[index] = knots[index];
            }
            return copy;
        }

        var generated = new double[expected];
        int interior = controlCount - degree;
        for (int index = 0; index <= degree; index++)
        {
            generated[index] = 0;
            generated[expected - 1 - index] = 1;
        }
        for (int index = 1; index < interior; index++)
        {
            generated[degree + index] = (double)index / interior;
        }
        return generated;
    }

    private static double[] NormalizeWeights(
        int controlCount,
        IReadOnlyList<double> weights)
    {
        var result = new double[controlCount];
        bool usable = weights.Count == controlCount;
        for (int index = 0; index < controlCount; index++)
        {
            double weight = usable ? weights[index] : 1;
            result[index] = weight > 0 && double.IsFinite(weight) ? weight : 1;
        }
        return result;
    }

    private static bool IsMonotone(IReadOnlyList<double> knots)
    {
        for (int index = 1; index < knots.Count; index++)
        {
            if (knots[index] + 1e-14 < knots[index - 1] || !double.IsFinite(knots[index]))
            {
                return false;
            }
        }
        return double.IsFinite(knots[0]);
    }

    private static float ControlPolygonLength(ReadOnlySpan<Vector3> controls)
    {
        float length = 0;
        for (int index = 1; index < controls.Length; index++)
        {
            length += Vector3.Distance(controls[index - 1], controls[index]);
        }
        return length;
    }

    private static Vector3 EllipsePoint(
        Vector3 center,
        Vector3 major,
        Vector3 minor,
        float parameter) =>
        center
        + major * MathF.Cos(parameter)
        + minor * MathF.Sin(parameter);

    private static float ParameterSweep(float start, float end)
    {
        float sweep = end - start;
        while (sweep <= 0)
        {
            sweep += TwoPi;
        }
        while (sweep > TwoPi)
        {
            sweep -= TwoPi;
        }
        return sweep;
    }

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
}
