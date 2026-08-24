using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Numerics;
using ACadSharp.Tables;

namespace DwgTrueView.Cad;

/// <summary>
/// Maps CAD SHX/TTF styles onto Windows fonts (Arial / Times / Consolas) and
/// tessellates glyph outlines into the same GPU line batch as geometry.
/// </summary>
internal static class SystemFontOutlines
{
    private static readonly object GdiLock = new();
    private static readonly ConcurrentDictionary<string, string> FamilyCache = new(
        StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<(string Family, char Glyph), CachedGlyph> GlyphCache = new();

    public static bool TryAppend(
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
        float lineSpacingFactor,
        TextStyle? style,
        int layerId,
        CadColorValue color,
        List<LocalSegment> destination)
    {
        if (height <= 1e-6f)
        {
            return false;
        }
        string familyName = ResolveFamily(style);
        string[] prepared = CadTextLayout.Wrap(
            lines,
            wrapWidth,
            text => Measure(familyName, text) * height * widthFactor);
        if (prepared.Length == 0)
        {
            return false;
        }

        float lineHeight = height * (5f / 3f) * (lineSpacingFactor > 0 ? lineSpacingFactor : 1f);
        float maxWidth = 0;
        foreach (string line in prepared)
        {
            maxWidth = MathF.Max(maxWidth, Measure(familyName, line) * height * widthFactor);
        }
        if (wrapWidth > 0)
        {
            maxWidth = MathF.Max(maxWidth, wrapWidth);
        }

        float blockHeight = Math.Max(prepared.Length - 1, 0) * lineHeight + height;
        float originX = -alignX * maxWidth;
        float originY = -alignY * blockHeight;
        float shear = MathF.Tan(oblique);
        bool emitted = false;

        for (int lineIndex = 0; lineIndex < prepared.Length; lineIndex++)
        {
            string line = prepared[lineIndex];
            float lineWidth = Measure(familyName, line) * height * widthFactor;
            float cursorX = originX + alignX * (maxWidth - lineWidth);
            float cursorY = originY + (prepared.Length - 1 - lineIndex) * lineHeight;
            float x = cursorX;
            foreach (char raw in line)
            {
                char key = StrokeFont.NormalizePublic(raw);
                if (key == ' ')
                {
                    x += 0.33f * height * widthFactor;
                    continue;
                }
                CachedGlyph glyph = GetGlyph(familyName, key);
                foreach ((Vector2 start, Vector2 end) in glyph.Strokes)
                {
                    Vector3 a = Map(
                        origin,
                        axisX,
                        axisY,
                        x + (start.X + start.Y * shear) * height * widthFactor,
                        cursorY + start.Y * height);
                    Vector3 b = Map(
                        origin,
                        axisX,
                        axisY,
                        x + (end.X + end.Y * shear) * height * widthFactor,
                        cursorY + end.Y * height);
                    if (CadMath.IsUsable(a)
                        && CadMath.IsUsable(b)
                        && a != b
                        && !CadMath.IsCorruptOriginRay(a, b))
                    {
                        destination.Add(new LocalSegment(a, b, layerId, color));
                        emitted = true;
                    }
                }
                x += glyph.Advance * height * widthFactor;
            }
        }
        return emitted || prepared.All(string.IsNullOrWhiteSpace);
    }

    public static float Measure(string familyName, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }
        float width = 0;
        foreach (char raw in text)
        {
            char key = StrokeFont.NormalizePublic(raw);
            width += key == ' ' ? 0.33f : GetGlyph(familyName, key).Advance;
        }
        return width;
    }

    public static string ResolveFamily(TextStyle? style)
    {
        string key = $"{style?.Name}|{style?.Filename}";
        return FamilyCache.GetOrAdd(key, _ => MapFamily(style));
    }

    private static string MapFamily(TextStyle? style)
    {
        string token = $"{style?.Name} {style?.Filename}".ToLowerInvariant();
        if (token.Contains("times") || token.Contains("romant") || token.Contains("romand"))
        {
            return FirstInstalled("Times New Roman", "Georgia", "Arial");
        }
        if (token.Contains("cour") || token.Contains("mono") || token.Contains("consol"))
        {
            return FirstInstalled("Consolas", "Courier New", "Arial");
        }
        if (token.Contains("isocp") || token.Contains("isoct"))
        {
            return FirstInstalled("ISOCPEUR", "Arial", "Segoe UI");
        }
        return FirstInstalled("Arial", "Segoe UI", "Tahoma");
    }

    private static string FirstInstalled(params string[] names)
    {
        lock (GdiLock)
        {
            foreach (string name in names)
            {
                try
                {
                    using var family = new FontFamily(name);
                    if (family.Name.Length > 0)
                    {
                        return family.Name;
                    }
                }
                catch (ArgumentException)
                {
                }
            }
        }
        return "Arial";
    }

    private static CachedGlyph GetGlyph(string familyName, char glyph) =>
        GlyphCache.GetOrAdd((familyName, glyph), static key => BuildGlyph(key.Family, key.Glyph));

    private static CachedGlyph BuildGlyph(string familyName, char glyph)
    {
        lock (GdiLock)
        {
            try
            {
                using var family = new FontFamily(familyName);
                FontStyle fontStyle = FontStyle.Regular;
                if (!family.IsStyleAvailable(fontStyle))
                {
                    fontStyle = FontStyle.Bold;
                }
                float em = family.GetEmHeight(fontStyle);
                float ascent = family.GetCellAscent(fontStyle);
                if (em <= 0)
                {
                    return CachedGlyph.Empty;
                }
                using var path = new GraphicsPath();
                path.AddString(
                    glyph.ToString(),
                    family,
                    (int)fontStyle,
                    em,
                    PointF.Empty,
                    StringFormat.GenericTypographic);
                path.Flatten(null, 0.4f);
                if (path.PointCount < 2)
                {
                    return CachedGlyph.Empty;
                }

                var strokes = new List<(Vector2 Start, Vector2 End)>();
                PointF[] points = path.PathPoints;
                byte[] types = path.PathTypes;
                Vector2? previous = null;
                for (int i = 0; i < points.Length; i++)
                {
                    float x = points[i].X / em;
                    float y = (ascent - points[i].Y) / em;
                    var current = new Vector2(x, y);
                    bool start = (types[i] & (byte)PathPointType.PathTypeMask) == (byte)PathPointType.Start;
                    if (start)
                    {
                        previous = current;
                        continue;
                    }
                    if (previous is Vector2 from && from != current)
                    {
                        strokes.Add((from, current));
                    }
                    previous = current;
                }
                RectangleF bounds = path.GetBounds();
                float advance = bounds.Width <= 0 ? 0.55f : (bounds.Width / em) + 0.06f;
                return new CachedGlyph(strokes.ToArray(), Math.Clamp(advance, 0.12f, 1.4f));
            }
            catch (Exception)
            {
                return CachedGlyph.Empty;
            }
        }
    }

    private static Vector3 Map(
        Vector3 origin,
        Vector3 axisX,
        Vector3 axisY,
        float x,
        float y) =>
        origin + axisX * x + axisY * y;

    private readonly record struct CachedGlyph(
        (Vector2 Start, Vector2 End)[] Strokes,
        float Advance)
    {
        public static CachedGlyph Empty { get; } = new([], 0.55f);
    }
}
