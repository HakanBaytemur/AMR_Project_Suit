using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Numerics;
using ACadSharp.Tables;

namespace DwgTrueView.Cad;

/// <summary>
/// Maps CAD SHX/TTF styles onto Windows fonts (Arial / Times / Consolas) and
/// tessellates glyph outlines into filled triangles (with stroke fallback).
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
        List<LocalSegment> destination,
        List<LocalTriangle>? fills)
    {
        if (height <= 1e-6f)
        {
            return false;
        }
        string familyName = ResolveFamily(style);
        float effectiveWrap = CadTextLayout.EffectiveWrapWidth(
            wrapWidth,
            lines,
            text => Measure(familyName, text) * height * widthFactor);
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
        if (effectiveWrap > 0)
        {
            maxWidth = MathF.Max(maxWidth, effectiveWrap);
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
                char key = CadTextCodec.MapGlyph(raw);
                if (key == ' ')
                {
                    x += 0.33f * height * widthFactor;
                    continue;
                }
                CachedGlyph glyph = GetGlyph(familyName, key);
                bool filled = false;
                if (fills is not null && glyph.Fills.Length > 0)
                {
                    foreach ((Vector2 a, Vector2 b, Vector2 c) in glyph.Fills)
                    {
                        Vector3 ta = MapGlyph(origin, axisX, axisY, x, cursorY, a, height, widthFactor, shear);
                        Vector3 tb = MapGlyph(origin, axisX, axisY, x, cursorY, b, height, widthFactor, shear);
                        Vector3 tc = MapGlyph(origin, axisX, axisY, x, cursorY, c, height, widthFactor, shear);
                        if (CadMath.IsPlausible(ta)
                            && CadMath.IsPlausible(tb)
                            && CadMath.IsPlausible(tc))
                        {
                            fills.Add(new LocalTriangle(ta, tb, tc, layerId, color));
                            filled = true;
                            emitted = true;
                        }
                    }
                }
                if (!filled)
                {
                    foreach ((Vector2 start, Vector2 end) in glyph.Strokes)
                    {
                        Vector3 a = MapGlyph(origin, axisX, axisY, x, cursorY, start, height, widthFactor, shear);
                        Vector3 b = MapGlyph(origin, axisX, axisY, x, cursorY, end, height, widthFactor, shear);
                        if (CadMath.IsPlausible(a) && CadMath.IsPlausible(b) && a != b)
                        {
                            destination.Add(new LocalSegment(a, b, layerId, color));
                            emitted = true;
                        }
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
            char key = CadTextCodec.MapGlyph(raw);
            width += key == ' ' ? 0.33f : GetGlyph(familyName, key).Advance;
        }
        return width;
    }

    public static string ResolveFamily(TextStyle? style)
    {
        string key = $"{style?.Name}|{style?.Filename}";
        return FamilyCache.GetOrAdd(key, _ => MapFamily(style));
    }

    private static Vector3 MapGlyph(
        Vector3 origin,
        Vector3 axisX,
        Vector3 axisY,
        float cursorX,
        float cursorY,
        Vector2 local,
        float height,
        float widthFactor,
        float shear) =>
        Map(
            origin,
            axisX,
            axisY,
            cursorX + (local.X + local.Y * shear) * height * widthFactor,
            cursorY + local.Y * height);

    private static string MapFamily(TextStyle? style)
    {
        string token = $"{style?.Name} {style?.Filename}".ToLowerInvariant();
        if (IsStrokeShx(token))
        {
            return FirstInstalled("Arial", "ISOCPEUR", "Segoe UI", "Tahoma");
        }
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

    private static bool IsStrokeShx(string token) =>
        token.Contains(".shx")
        || token.Contains("txt")
        || token.Contains("simplex")
        || token.Contains("romans")
        || token.Contains("romanc")
        || token.Contains("italicc")
        || token.Contains("standard");

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
                path.Flatten(null, 0.5f);
                if (path.PointCount < 2)
                {
                    return CachedGlyph.Empty;
                }

                PointF[] points = path.PathPoints;
                byte[] types = path.PathTypes;
                var loops = ExtractLoops(points, types, em, ascent);
                var strokes = FlattenStrokes(loops);
                var fills = GlyphFillTessellator.Fill(loops);
                RectangleF bounds = path.GetBounds();
                float advance = bounds.Right <= 0
                    ? 0.55f
                    : (bounds.Right / em) + 0.08f;
                return new CachedGlyph(
                    strokes,
                    fills,
                    Math.Clamp(advance, 0.18f, 1.8f));
            }
            catch (Exception)
            {
                return CachedGlyph.Empty;
            }
        }
    }

    private static List<List<Vector2>> ExtractLoops(
        PointF[] points,
        byte[] types,
        float em,
        float ascent)
    {
        var loops = new List<List<Vector2>>();
        var current = new List<Vector2>();
        Vector2 start = default;
        for (int i = 0; i < points.Length; i++)
        {
            float x = points[i].X / em;
            float y = (ascent - points[i].Y) / em;
            var local = new Vector2(x, y);
            bool figureStart = (types[i] & (byte)PathPointType.PathTypeMask) == (byte)PathPointType.Start;
            bool close = (types[i] & (byte)PathPointType.CloseSubpath) != 0;
            if (figureStart && current.Count > 0)
            {
                CloseLoop(current, start, loops);
                current = [];
            }
            if (figureStart)
            {
                start = local;
                current.Add(local);
                continue;
            }
            if (current.Count == 0 || current[^1] != local)
            {
                current.Add(local);
            }
            if (close)
            {
                CloseLoop(current, start, loops);
                current = [];
            }
        }
        if (current.Count >= 3)
        {
            CloseLoop(current, start, loops);
        }
        return loops;
    }

    private static void CloseLoop(List<Vector2> current, Vector2 start, List<List<Vector2>> loops)
    {
        if (current.Count >= 3)
        {
            if (Vector2.DistanceSquared(current[^1], start) > 1e-10f)
            {
                current.Add(start);
            }
            loops.Add(current);
        }
    }

    private static (Vector2 Start, Vector2 End)[] FlattenStrokes(IReadOnlyList<List<Vector2>> loops)
    {
        var strokes = new List<(Vector2 Start, Vector2 End)>();
        foreach (List<Vector2> loop in loops)
        {
            for (int i = 1; i < loop.Count; i++)
            {
                if (loop[i - 1] != loop[i])
                {
                    strokes.Add((loop[i - 1], loop[i]));
                }
            }
        }
        return strokes.ToArray();
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
        (Vector2 A, Vector2 B, Vector2 C)[] Fills,
        float Advance)
    {
        public static CachedGlyph Empty { get; } = new([], [], 0.55f);
    }
}
