namespace DwgTrueView.Cad;

/// <summary>
/// DXF group-41 MTEXT wrapping. Width 0 means unconstrained: keep authoring
/// breaks (<c>\P</c>) and never insert extra line breaks.
/// </summary>
internal static class CadTextLayout
{
    public const int MaxLines = 64;
    public const int MaxCharacters = 4096;

    public static string[] Wrap(
        IReadOnlyList<string> lines,
        float wrapWidth,
        Func<string, float> measure)
    {
        var result = new List<string>(Math.Min(lines.Count, MaxLines));
        int remaining = MaxCharacters;
        bool wrap = wrapWidth > 1e-6f;

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
                int take = Fit(line, offset, wrapWidth, remaining, measure);
                if (take <= 0)
                {
                    take = Math.Min(1, Math.Min(remaining, line.Length - offset));
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
        int low = 1;
        int high = available;
        int fit = 1;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            float width = measure(line.Substring(offset, mid));
            if (width <= wrapWidth || mid == 1)
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
