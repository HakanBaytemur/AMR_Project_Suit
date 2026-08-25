namespace DwgTrueView.Cad;

/// <summary>
/// DXF group-41 MTEXT wrapping. Width 0, undefined, or smaller than a single
/// glyph means unconstrained: keep authoring breaks (<c>\P</c>) and never
/// insert extra line breaks. That prevents multi-digit numbers such as
/// <c>158</c> from stacking into one character per line.
/// </summary>
internal static class CadTextLayout
{
    public const int MaxLines = 64;
    public const int MaxCharacters = 4096;

    public static bool IsUnconstrained(float wrapWidth, float minGlyphWidth)
    {
        if (!float.IsFinite(wrapWidth) || wrapWidth <= 1e-6f)
        {
            return true;
        }
        return minGlyphWidth > 1e-6f && wrapWidth < minGlyphWidth;
    }

    public static float EffectiveWrapWidth(
        float wrapWidth,
        IReadOnlyList<string> lines,
        Func<string, float> measure)
    {
        if (!float.IsFinite(wrapWidth) || wrapWidth <= 1e-6f)
        {
            return 0f;
        }
        float minGlyph = MinGlyphWidth(lines, measure);
        return IsUnconstrained(wrapWidth, minGlyph) ? 0f : wrapWidth;
    }

    public static string[] Wrap(
        IReadOnlyList<string> lines,
        float wrapWidth,
        Func<string, float> measure)
    {
        var result = new List<string>(Math.Min(lines.Count, MaxLines));
        int remaining = MaxCharacters;
        float effective = EffectiveWrapWidth(wrapWidth, lines, measure);
        bool wrap = effective > 1e-6f;

        foreach (string raw in lines)
        {
            if (result.Count >= MaxLines || remaining <= 0)
            {
                break;
            }
            string line = (raw ?? string.Empty).Replace('\t', ' ');
            if (!wrap)
            {
                if (line.Length > remaining)
                {
                    line = line[..remaining];
                }
                result.Add(line);
                remaining -= line.Length;
                continue;
            }

            int offset = 0;
            while (offset < line.Length && result.Count < MaxLines && remaining > 0)
            {
                int take = Fit(line, offset, effective, remaining, measure);
                if (take <= 0)
                {
                    take = Math.Min(remaining, line.Length - offset);
                }
                result.Add(line.Substring(offset, take).TrimEnd());
                remaining -= take;
                offset += take;
                while (offset < line.Length && line[offset] == ' ')
                {
                    offset++;
                }
            }
        }
        return result.ToArray();
    }

    private static float MinGlyphWidth(IReadOnlyList<string> lines, Func<string, float> measure)
    {
        foreach (string raw in lines)
        {
            foreach (char character in raw ?? string.Empty)
            {
                if (char.IsWhiteSpace(character))
                {
                    continue;
                }
                float width = measure(character.ToString());
                if (float.IsFinite(width) && width > 0)
                {
                    return width;
                }
            }
        }
        return 0f;
    }

    private static int Fit(
        string line,
        int offset,
        float wrapWidth,
        int remaining,
        Func<string, float> measure)
    {
        int available = Math.Min(remaining, line.Length - offset);
        if (available <= 0)
        {
            return 0;
        }

        float first = measure(line.Substring(offset, 1));
        if (!float.IsFinite(first) || first > wrapWidth)
        {
            // A box narrower than one glyph is not a real wrap column.
            return available;
        }

        int low = 1;
        int high = available;
        int fit = 1;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            float width = measure(line.Substring(offset, mid));
            if (width <= wrapWidth)
            {
                fit = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }
        int space = line.LastIndexOf(' ', offset + fit - 1, fit);
        if (space >= offset + 1 && fit < available)
        {
            fit = space - offset;
        }
        return Math.Max(1, fit);
    }
}
