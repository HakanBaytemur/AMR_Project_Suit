using System.Numerics;
using ACadSharp.Entities;

namespace DwgTrueView.Cad;

internal static class CadMath
{
    private const float AxisTolerance = 1f / 64f;

    public static Matrix4x4 InsertTransform(
        Insert insert,
        Vector3 blockBasePoint,
        Vector2 arrayOffset)
    {
        Vector3 insertion = ToVector(insert.InsertPoint);
        Vector3 normal = ToVector(insert.Normal);
        Vector3 scale = new(
            (float)insert.XScale,
            (float)insert.YScale,
            (float)insert.ZScale);
        float rotation = (float)insert.Rotation;
        CreateOcsBasis(normal, out Vector3 xAxis, out Vector3 yAxis, out Vector3 zAxis);
        var ocsToWorld = new Matrix4x4(
            xAxis.X, xAxis.Y, xAxis.Z, 0,
            yAxis.X, yAxis.Y, yAxis.Z, 0,
            zAxis.X, zAxis.Y, zAxis.Z, 0,
            0, 0, 0, 1);

        float cos = MathF.Cos(rotation);
        float sin = MathF.Sin(rotation);
        Vector2 rotatedOffset = new(
            arrayOffset.X * cos - arrayOffset.Y * sin,
            arrayOffset.X * sin + arrayOffset.Y * cos);
        Vector3 worldOffset = xAxis * rotatedOffset.X + yAxis * rotatedOffset.Y;

        return Matrix4x4.CreateTranslation(-blockBasePoint)
            * Matrix4x4.CreateScale(scale)
            * Matrix4x4.CreateRotationZ(rotation)
            * ocsToWorld
            * Matrix4x4.CreateTranslation(insertion + worldOffset);
    }

    public static void CreateOcsBasis(
        Vector3 normal,
        out Vector3 xAxis,
        out Vector3 yAxis,
        out Vector3 zAxis)
    {
        zAxis = normal.LengthSquared() <= float.Epsilon
            ? Vector3.UnitZ
            : Vector3.Normalize(normal);
        xAxis = MathF.Abs(zAxis.X) < AxisTolerance
            && MathF.Abs(zAxis.Y) < AxisTolerance
            ? Vector3.Normalize(Vector3.Cross(Vector3.UnitY, zAxis))
            : Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, zAxis));
        yAxis = Vector3.Normalize(Vector3.Cross(zAxis, xAxis));
    }

    public static Vector3 ToVector(CSMath.XYZ point) =>
        new((float)point.X, (float)point.Y, (float)point.Z);
}
