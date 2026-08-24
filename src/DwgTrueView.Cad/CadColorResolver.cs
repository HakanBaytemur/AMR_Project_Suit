using ACadSharp;

namespace DwgTrueView.Cad;

internal enum CadColorKind : byte
{
    ByLayer,
    ByBlock,
    Aci,
    TrueColor,
}

internal readonly record struct CadColorValue(CadColorKind Kind, int Value)
{
    public static CadColorValue ByLayer => new(CadColorKind.ByLayer, 0);
    public static CadColorValue ByBlock => new(CadColorKind.ByBlock, 0);
    public static CadColorValue Aci(int index) => new(CadColorKind.Aci, index);
    public static CadColorValue Rgb(int rgb) => new(CadColorKind.TrueColor, rgb);
}

internal static class CadColorResolver
{
    /// <summary>
    /// AutoCAD ACI 7 on a dark model-space canvas: white. Black is the
    /// matching paper-space / light-canvas counterpart.
    /// </summary>
    public const int DefaultForegroundArgb = unchecked((int)0xFFFFFFFF);
    public const int DefaultBackgroundArgb = unchecked((int)0xFF000000);
    public const int ForegroundArgb = DefaultForegroundArgb;

    public static CadColorValue FromCadColor(Color color)
    {
        try
        {
            if (color.IsByLayer || color.Index == 257)
            {
                return CadColorValue.ByLayer;
            }
            if (color.IsByBlock)
            {
                return CadColorValue.ByBlock;
            }
            if (color.IsTrueColor)
            {
                return CadColorValue.Rgb(color.R << 16 | color.G << 8 | color.B);
            }
            return color.Index is >= 1 and <= 255
                ? CadColorValue.Aci(color.Index)
                : CadColorValue.ByLayer;
        }
        catch (Exception)
        {
            return CadColorValue.ByLayer;
        }
    }

    public static int Resolve(
        CadColorValue value,
        CadColorValue layerColor,
        CadColorValue blockColor)
    {
        CadColorValue current = value;
        for (int depth = 0; depth < 4; depth++)
        {
            switch (current.Kind)
            {
                case CadColorKind.TrueColor:
                    return unchecked((int)0xFF000000) | current.Value;
                case CadColorKind.Aci:
                    return AciArgb(current.Value);
                case CadColorKind.ByLayer:
                    current = layerColor;
                    break;
                case CadColorKind.ByBlock:
                    current = blockColor;
                    break;
            }
        }
        return ForegroundArgb;
    }

    public static int AciArgb(int index)
    {
        if (index is < 1 or > 255)
        {
            return ForegroundArgb;
        }
        ReadOnlySpan<byte> rgb = Color.GetIndexRGB((byte)index);
        return unchecked((int)0xFF000000)
            | rgb[0] << 16
            | rgb[1] << 8
            | rgb[2];
    }
}
