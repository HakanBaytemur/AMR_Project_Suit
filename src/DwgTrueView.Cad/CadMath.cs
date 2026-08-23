using System.Numerics;
using ACadSharp.Entities;
using ACadSharp.Tables;

namespace DwgTrueView.Cad;

/// <summary>
/// DWG/DXF coordinate transformer. Missing vertices are never replaced with
/// the origin; OCS points use the AutoCAD Arbitrary Axis Algorithm; INSERT
/// placement is Translation × Rotation × Scale × BasePointOffset.
/// </summary>
internal static class CadMath
{
    private const float AxisTolerance = 1f / 64f;
    private const float OriginEpsilon = 1e-8f;

    public static Matrix4x4 InsertTransform(
        Insert insert,
        Vector3 blockBasePoint,
        Vector2 arrayOffset)
    {
        if (!TryInsertTransform(insert, blockBasePoint, arrayOffset, out Matrix4x4 transform))
        {
            return Matrix4x4.Identity;
        }
        return transform;
    }

    public static bool TryInsertTransform(
        Insert insert,
        Vector3 blockBasePoint,
        Vector2 arrayOffset,
        out Matrix4x4 transform)
    {
        transform = default;
        if (!TryPoint(insert.InsertPoint, out Vector3 insertion))
        {
            return false;
        }

        Vector3 scale = new(
            (float)insert.XScale,
            (float)insert.YScale,
            (float)insert.ZScale);
        if (!IsUsable(scale)
            || scale.X == 0
            || scale.Y == 0
            || scale.Z == 0)
        {
            return false;
        }

        Vector3 normal = UsableNormal(insert.Normal);
        CreateOcsBasis(normal, out Vector3 xAxis, out Vector3 yAxis, out Vector3 zAxis);
        Matrix4x4 ocsToWorld = OcsToWorldMatrix(xAxis, yAxis, zAxis);

        float rotation = (float)insert.Rotation;
        if (!float.IsFinite(rotation))
        {
            return false;
        }

        float cos = MathF.Cos(rotation);
        float sin = MathF.Sin(rotation);
        Vector3 ocsTranslation = insertion + new Vector3(
            arrayOffset.X * cos - arrayOffset.Y * sin,
            arrayOffset.X * sin + arrayOffset.Y * cos,
            0);

        transform = Matrix4x4.CreateTranslation(-blockBasePoint)
            * Matrix4x4.CreateScale(scale)
            * Matrix4x4.CreateRotationZ(rotation)
            * Matrix4x4.CreateTranslation(ocsTranslation)
            * ocsToWorld;
        return true;
    }

    public static Vector3 BlockBasePoint(BlockRecord? block)
    {
        if (block?.BlockEntity is null
            || !TryPoint(block.BlockEntity.BasePoint, out Vector3 origin))
        {
            return Vector3.Zero;
        }
        return origin;
    }

    public static void CreateOcsBasis(
        Vector3 normal,
        out Vector3 xAxis,
        out Vector3 yAxis,
        out Vector3 zAxis)
    {
        zAxis = !IsUsable(normal) || normal.LengthSquared() <= float.Epsilon
            ? Vector3.UnitZ
            : Vector3.Normalize(normal);
        xAxis = MathF.Abs(zAxis.X) < AxisTolerance
            && MathF.Abs(zAxis.Y) < AxisTolerance
            ? Vector3.Normalize(Vector3.Cross(Vector3.UnitY, zAxis))
            : Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, zAxis));
        yAxis = Vector3.Normalize(Vector3.Cross(zAxis, xAxis));
    }

    public static bool TryOcsToWcs(
        CSMath.XYZ point,
        CSMath.XYZ normal,
        out Vector3 world) =>
        TryOcsToWcs(point.X, point.Y, point.Z, ToVector(normal), out world);

    public static bool TryOcsToWcs(
        CSMath.XY point,
        double elevation,
        CSMath.XYZ normal,
        out Vector3 world) =>
        TryOcsToWcs(point.X, point.Y, elevation, ToVector(normal), out world);

    public static bool TryOcsToWcs(
        double x,
        double y,
        double z,
        Vector3 normal,
        out Vector3 world)
    {
        world = default;
        if (!IsUsable(x, y, z))
        {
            return false;
        }
        CreateOcsBasis(UsableNormal(normal), out Vector3 axisX, out Vector3 axisY, out Vector3 axisZ);
        world = axisX * (float)x + axisY * (float)y + axisZ * (float)z;
        return IsUsable(world);
    }

    public static bool TryOcsToWcs(
        double x,
        double y,
        double z,
        CSMath.XYZ normal,
        out Vector3 world) =>
        TryOcsToWcs(x, y, z, ToVector(normal), out world);

    public static Vector3 OcsToWcs(Vector3 ocs, Vector3 normal)
    {
        CreateOcsBasis(UsableNormal(normal), out Vector3 axisX, out Vector3 axisY, out Vector3 axisZ);
        return axisX * ocs.X + axisY * ocs.Y + axisZ * ocs.Z;
    }

    public static bool TryPoint(CSMath.XYZ point, out Vector3 vector)
    {
        vector = ToVector(point);
        return IsUsable(vector);
    }

    public static bool TryPoint(CSMath.XY point, double elevation, out Vector3 vector)
    {
        vector = new Vector3((float)point.X, (float)point.Y, (float)elevation);
        return IsUsable(vector);
    }

    public static bool TryWorldPoint(CSMath.XYZ point, out Vector3 vector) =>
        TryPoint(point, out vector);

    public static Vector3 ToVector(CSMath.XYZ point) =>
        new((float)point.X, (float)point.Y, (float)point.Z);

    public static Vector3 UsableNormal(CSMath.XYZ normal) =>
        UsableNormal(ToVector(normal));

    public static Vector3 UsableNormal(Vector3 normal) =>
        IsUsable(normal) && normal.LengthSquared() > float.Epsilon
            ? Vector3.Normalize(normal)
            : Vector3.UnitZ;

    public static bool IsUsable(Vector3 point) =>
        float.IsFinite(point.X)
        && float.IsFinite(point.Y)
        && float.IsFinite(point.Z);

    public static bool IsUsable(double x, double y, double z) =>
        double.IsFinite(x) && double.IsFinite(y) && double.IsFinite(z);

    public static bool IsOrigin(Vector3 point) =>
        point.LengthSquared() <= OriginEpsilon;

    public static bool PreferExplicitPoint(Vector3 preferred, Vector3 fallback) =>
        IsUsable(preferred) && !(IsOrigin(preferred) && !IsOrigin(fallback));

    private static Matrix4x4 OcsToWorldMatrix(
        Vector3 xAxis,
        Vector3 yAxis,
        Vector3 zAxis) =>
        new(
            xAxis.X, xAxis.Y, xAxis.Z, 0,
            yAxis.X, yAxis.Y, yAxis.Z, 0,
            zAxis.X, zAxis.Y, zAxis.Z, 0,
            0, 0, 0, 1);
}
