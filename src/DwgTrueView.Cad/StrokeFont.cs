using System.Globalization;
using System.Numerics;

namespace DwgTrueView.Cad;

/// <summary>
/// Lightweight stroke-font outlines for CAD TEXT/MTEXT. Glyphs are unit-height
/// polylines that land in the same GPU line batch as geometry. A text frame is
/// emitted only when the DXF MTEXT group-90 background/frame bits are set.
/// </summary>
internal static class StrokeFont
{
    public const float Advance = 0.55f;
    private const float BoxPadX = 0.08f;
    private const float BoxPadY = 0.18f;

    private static readonly Dictionary<char, string> Glyphs = CreateGlyphs();

    public static void AppendLabel(
        IReadOnlyList<string> lines,
        Vector3 origin,
        Vector3 axisX,
        Vector3 axisY,
        float height,
        float widthFactor,
        float oblique,
        float alignX,
        float alignY,
        float wrapWidth,
        int layerId,
        CadColorValue color,
        List<LocalSegment> destination,
        bool drawFrame = false,
        float lineSpacingFactor = 1f,
        ACadSharp.Tables.TextStyle? style = null,
        List<LocalTriangle>? fills = null)
    {
        if (height <= 1e-6f)
        {
            return;
        }

        if (SystemFontOutlines.TryAppend(
                lines,
                origin,
                axisX,
                axisY,
                height,
                widthFactor,
                oblique,
                alignX,
                alignY,
                wrapWidth,
                lineSpacingFactor,
                style,
                layerId,
                color,
                destination,
                fills))
        {
            if (drawFrame)
            {
                string[] prepared = CadTextLayout.Wrap(
                    lines,
                    wrapWidth,
                    text => Measure(text) * height * widthFactor);
                float maxWidth = wrapWidth;
                foreach (string line in prepared)
                {
                    maxWidth = MathF.Max(maxWidth, Measure(line) * height * widthFactor);
                }
                float lineHeight = height * (5f / 3f) * (lineSpacingFactor > 0 ? lineSpacingFactor : 1f);
                float blockHeight = Math.Max(prepared.Length - 1, 0) * lineHeight + height;
                AppendBox(
                    origin,
                    axisX,
                    axisY,
                    -alignX * maxWidth - BoxPadX * height,
                    -alignY * blockHeight - BoxPadY * height,
                    maxWidth + BoxPadX * 2 * height,
                    blockHeight + BoxPadY * 1.4f * height,
                    height,
                    MathF.Tan(oblique),
                    layerId,
                    color,
                    destination);
            }
            return;
        }

        string[] fallback = CadTextLayout.Wrap(
            lines,
            wrapWidth,
            text => Measure(text) * height * widthFactor);
        if (fallback.Length == 0)
        {
            return;
        }

        float strokeLineHeight = height * (5f / 3f) * (lineSpacingFactor > 0 ? lineSpacingFactor : 1f);
        float strokeMaxWidth = 0;
        foreach (string line in fallback)
        {
            strokeMaxWidth = MathF.Max(strokeMaxWidth, Measure(line) * height * widthFactor);
        }
        if (wrapWidth > 0)
        {
            strokeMaxWidth = MathF.Max(strokeMaxWidth, wrapWidth);
        }

        float strokeBlockHeight = Math.Max(fallback.Length - 1, 0) * strokeLineHeight + height;
        float originX = -alignX * strokeMaxWidth;
        float originY = -alignY * strokeBlockHeight;
        float shear = MathF.Tan(oblique);

        if (drawFrame)
        {
            AppendBox(
                origin,
                axisX,
                axisY,
                originX - BoxPadX * height,
                originY - BoxPadY * height,
                strokeMaxWidth + BoxPadX * 2 * height,
                strokeBlockHeight + BoxPadY * 1.4f * height,
                height,
                shear,
                layerId,
                color,
                destination);
        }

        for (int lineIndex = 0; lineIndex < fallback.Length; lineIndex++)
        {
            string line = fallback[lineIndex];
            float lineWidth = Measure(line) * height * widthFactor;
            float cursorX = originX + alignX * (strokeMaxWidth - lineWidth);
            float cursorY = originY + (fallback.Length - 1 - lineIndex) * strokeLineHeight;
            float x = cursorX;
            foreach (char raw in line)
            {
                char key = CadTextCodec.MapGlyph(raw);
                if (key == ' ')
                {
                    x += Advance * height * widthFactor;
                    continue;
                }
                if (!Glyphs.TryGetValue(key, out string? strokes))
                {
                    AppendUnknown(
                        origin,
                        axisX,
                        axisY,
                        x,
                        cursorY,
                        height,
                        widthFactor,
                        shear,
                        layerId,
                        color,
                        destination);
                    x += Advance * height * widthFactor;
                    continue;
                }
                AppendGlyph(
                    strokes,
                    origin,
                    axisX,
                    axisY,
                    x,
                    cursorY,
                    height,
                    widthFactor,
                    shear,
                    layerId,
                    color,
                    destination);
                x += Advance * height * widthFactor;
            }
        }
    }

    internal static char NormalizePublic(char value) => CadTextCodec.MapGlyph(value);

    public static float Measure(string text)
    {
        int count = 0;
        foreach (char character in text)
        {
            if (character != '\0')
            {
                count++;
            }
        }
        return count * Advance;
    }

    private static void AppendGlyph(
        string strokes,
        Vector3 origin,
        Vector3 axisX,
        Vector3 axisY,
        float cursorX,
        float cursorY,
        float height,
        float widthFactor,
        float shear,
        int layerId,
        CadColorValue color,
        List<LocalSegment> destination)
    {
        Vector3? previous = null;
        foreach (string token in strokes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token == "|")
            {
                previous = null;
                continue;
            }
            int comma = token.IndexOf(',');
            if (comma <= 0)
            {
                continue;
            }
            if (!float.TryParse(
                    token.AsSpan(0, comma),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float gx)
                || !float.TryParse(
                    token.AsSpan(comma + 1),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float gy))
            {
                continue;
            }
            Vector3 current = Map(
                origin,
                axisX,
                axisY,
                cursorX + (gx + gy * shear) * height * widthFactor,
                cursorY + gy * height);
            if (previous is Vector3 start)
            {
                Add(destination, start, current, layerId, color);
            }
            previous = current;
        }
    }

    private static void AppendUnknown(
        Vector3 origin,
        Vector3 axisX,
        Vector3 axisY,
        float cursorX,
        float cursorY,
        float height,
        float widthFactor,
        float shear,
        int layerId,
        CadColorValue color,
        List<LocalSegment> destination)
    {
        AppendGlyph(
            "0.1,0.15 0.45,0.15 0.45,0.85 0.1,0.85 0.1,0.15",
            origin,
            axisX,
            axisY,
            cursorX,
            cursorY,
            height,
            widthFactor,
            shear,
            layerId,
            color,
            destination);
    }

    private static void AppendBox(
        Vector3 origin,
        Vector3 axisX,
        Vector3 axisY,
        float x,
        float y,
        float width,
        float height,
        float scale,
        float shear,
        int layerId,
        CadColorValue color,
        List<LocalSegment> destination)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }
        Vector3 Corner(float lx, float ly) =>
            Map(origin, axisX, axisY, lx + ly * shear * scale, ly);
        Vector3 a = Corner(x, y);
        Vector3 b = Corner(x + width, y);
        Vector3 c = Corner(x + width, y + height);
        Vector3 d = Corner(x, y + height);
        Add(destination, a, b, layerId, color);
        Add(destination, b, c, layerId, color);
        Add(destination, c, d, layerId, color);
        Add(destination, d, a, layerId, color);
    }

    private static Vector3 Map(
        Vector3 origin,
        Vector3 axisX,
        Vector3 axisY,
        float x,
        float y) =>
        origin + axisX * x + axisY * y;

    private static void Add(
        List<LocalSegment> destination,
        Vector3 start,
        Vector3 end,
        int layerId,
        CadColorValue color)
    {
        if (start != end
            && float.IsFinite(start.X)
            && float.IsFinite(start.Y)
            && float.IsFinite(end.X)
            && float.IsFinite(end.Y))
        {
            destination.Add(new LocalSegment(start, end, layerId, color));
        }
    }

    private static Dictionary<char, string> CreateGlyphs()
    {
        var glyphs = new Dictionary<char, string>
        {
            ['0'] = "0.08,0.12 0.47,0.12 0.47,0.88 0.08,0.88 0.08,0.12 | 0.47,0.12 0.08,0.88",
            ['1'] = "0.16,0.72 0.32,0.88 0.32,0.12 | 0.12,0.12 0.5,0.12",
            ['2'] = "0.08,0.88 0.47,0.88 0.47,0.52 0.08,0.52 0.08,0.12 0.47,0.12",
            ['3'] = "0.08,0.88 0.47,0.88 0.47,0.12 0.08,0.12 | 0.16,0.5 0.47,0.5",
            ['4'] = "0.08,0.88 0.08,0.5 0.47,0.5 | 0.4,0.88 0.4,0.12",
            ['5'] = "0.47,0.88 0.08,0.88 0.08,0.52 0.47,0.52 0.47,0.12 0.08,0.12",
            ['6'] = "0.47,0.88 0.08,0.88 0.08,0.12 0.47,0.12 0.47,0.5 0.08,0.5",
            ['7'] = "0.08,0.88 0.47,0.88 0.2,0.12",
            ['8'] = "0.08,0.12 0.47,0.12 0.47,0.88 0.08,0.88 0.08,0.12 | 0.08,0.5 0.47,0.5",
            ['9'] = "0.08,0.12 0.47,0.12 0.47,0.88 0.08,0.88 0.08,0.5 0.47,0.5",
            ['A'] = "0.08,0.12 0.275,0.88 0.47,0.12 | 0.16,0.42 0.4,0.42",
            ['B'] = "0.08,0.12 0.08,0.88 0.38,0.88 0.47,0.76 0.47,0.6 0.38,0.5 0.08,0.5 | 0.38,0.5 0.47,0.38 0.47,0.24 0.38,0.12 0.08,0.12",
            ['C'] = "0.47,0.8 0.36,0.88 0.16,0.88 0.08,0.72 0.08,0.28 0.16,0.12 0.36,0.12 0.47,0.2",
            ['D'] = "0.08,0.12 0.08,0.88 0.34,0.88 0.47,0.7 0.47,0.3 0.34,0.12 0.08,0.12",
            ['E'] = "0.47,0.88 0.08,0.88 0.08,0.12 0.47,0.12 | 0.08,0.5 0.38,0.5",
            ['F'] = "0.08,0.12 0.08,0.88 0.47,0.88 | 0.08,0.5 0.36,0.5",
            ['G'] = "0.47,0.8 0.36,0.88 0.16,0.88 0.08,0.72 0.08,0.28 0.16,0.12 0.4,0.12 0.47,0.24 0.47,0.46 0.3,0.46",
            ['H'] = "0.08,0.12 0.08,0.88 | 0.47,0.12 0.47,0.88 | 0.08,0.5 0.47,0.5",
            ['I'] = "0.12,0.88 0.43,0.88 | 0.275,0.88 0.275,0.12 | 0.12,0.12 0.43,0.12",
            ['J'] = "0.16,0.88 0.47,0.88 0.47,0.28 0.36,0.12 0.16,0.12 0.08,0.24",
            ['K'] = "0.08,0.12 0.08,0.88 | 0.47,0.88 0.08,0.5 0.47,0.12",
            ['L'] = "0.08,0.88 0.08,0.12 0.47,0.12",
            ['M'] = "0.08,0.12 0.08,0.88 0.275,0.46 0.47,0.88 0.47,0.12",
            ['N'] = "0.08,0.12 0.08,0.88 0.47,0.12 0.47,0.88",
            ['O'] = "0.16,0.12 0.39,0.12 0.47,0.28 0.47,0.72 0.39,0.88 0.16,0.88 0.08,0.72 0.08,0.28 0.16,0.12",
            ['P'] = "0.08,0.12 0.08,0.88 0.38,0.88 0.47,0.74 0.47,0.58 0.38,0.46 0.08,0.46",
            ['Q'] = "0.16,0.12 0.39,0.12 0.47,0.28 0.47,0.72 0.39,0.88 0.16,0.88 0.08,0.72 0.08,0.28 0.16,0.12 | 0.3,0.34 0.47,0.08",
            ['R'] = "0.08,0.12 0.08,0.88 0.38,0.88 0.47,0.74 0.47,0.58 0.38,0.46 0.08,0.46 | 0.28,0.46 0.47,0.12",
            ['S'] = "0.47,0.78 0.36,0.88 0.14,0.88 0.08,0.74 0.14,0.56 0.41,0.44 0.47,0.28 0.4,0.12 0.14,0.12 0.08,0.22",
            ['T'] = "0.08,0.88 0.47,0.88 | 0.275,0.88 0.275,0.12",
            ['U'] = "0.08,0.88 0.08,0.28 0.16,0.12 0.39,0.12 0.47,0.28 0.47,0.88",
            ['V'] = "0.08,0.88 0.275,0.12 0.47,0.88",
            ['W'] = "0.08,0.88 0.16,0.12 0.275,0.5 0.39,0.12 0.47,0.88",
            ['X'] = "0.08,0.88 0.47,0.12 | 0.47,0.88 0.08,0.12",
            ['Y'] = "0.08,0.88 0.275,0.5 0.47,0.88 | 0.275,0.5 0.275,0.12",
            ['Z'] = "0.08,0.88 0.47,0.88 0.08,0.12 0.47,0.12",
            ['-'] = "0.1,0.5 0.45,0.5",
            ['+'] = "0.1,0.5 0.45,0.5 | 0.275,0.22 0.275,0.78",
            ['='] = "0.1,0.38 0.45,0.38 | 0.1,0.62 0.45,0.62",
            ['_'] = "0.06,0.08 0.5,0.08",
            ['.'] = "0.24,0.12 0.32,0.12 0.32,0.22 0.24,0.22 0.24,0.12",
            [','] = "0.28,0.22 0.22,0.04",
            [':'] = "0.24,0.28 0.32,0.28 | 0.24,0.72 0.32,0.72",
            [';'] = "0.28,0.72 0.32,0.72 | 0.3,0.28 0.22,0.08",
            ['!'] = "0.275,0.88 0.275,0.34 | 0.275,0.16 0.275,0.12",
            ['?'] = "0.12,0.72 0.16,0.88 0.4,0.88 0.47,0.72 0.36,0.52 0.275,0.4 | 0.275,0.16 0.275,0.12",
            ['/'] = "0.1,0.12 0.45,0.88",
            ['\\'] = "0.1,0.88 0.45,0.12",
            ['*'] = "0.275,0.28 0.275,0.72 | 0.12,0.38 0.43,0.62 | 0.12,0.62 0.43,0.38",
            ['#'] = "0.18,0.12 0.18,0.88 | 0.38,0.12 0.38,0.88 | 0.08,0.36 0.47,0.36 | 0.08,0.64 0.47,0.64",
            ['%'] = "0.08,0.12 0.47,0.88 | 0.12,0.76 0.22,0.76 0.22,0.88 0.12,0.88 0.12,0.76 | 0.34,0.12 0.44,0.12 0.44,0.24 0.34,0.24 0.34,0.12",
            ['('] = "0.4,0.88 0.2,0.68 0.2,0.32 0.4,0.12",
            [')'] = "0.16,0.88 0.36,0.68 0.36,0.32 0.16,0.12",
            ['['] = "0.4,0.88 0.18,0.88 0.18,0.12 0.4,0.12",
            [']'] = "0.16,0.88 0.38,0.88 0.38,0.12 0.16,0.12",
            ['<'] = "0.44,0.8 0.12,0.5 0.44,0.2",
            ['>'] = "0.12,0.8 0.44,0.5 0.12,0.2",
            ['\''] = "0.275,0.88 0.275,0.68",
            ['"'] = "0.2,0.88 0.2,0.68 | 0.36,0.88 0.36,0.68",
        };

        foreach (var pair in glyphs.ToArray())
        {
            if (char.IsLetter(pair.Key))
            {
                glyphs[char.ToLowerInvariant(pair.Key)] = pair.Value;
            }
        }
        return glyphs;
    }
}
