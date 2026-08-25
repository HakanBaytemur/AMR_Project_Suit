using System.Globalization;
using System.Text;
using ACadSharp.Entities;

namespace DwgTrueView.Cad;

/// <summary>
/// Strips DXF MTEXT format codes and TEXT overcodes so SHX/TTF mappers see
/// real Unicode characters, not <c>\W1;</c> width prefixes or <c>%%d</c> escapes.
/// ASCII letters are left unchanged — there is no shape-number offset.
/// </summary>
internal static class CadTextCodec
{
    public static string[] PlainLines(MText mtext)
    {
        string raw;
        try
        {
            raw = mtext.PlainText;
        }
        catch (Exception)
        {
            raw = mtext.Value ?? string.Empty;
        }
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = mtext.Value ?? string.Empty;
        }
        return SplitLines(ToPlain(raw));
    }

    public static string Plain(TextEntity text) =>
        ToPlain(text.Value ?? string.Empty);

    public static string ToPlain(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }
        string stripped = StripMText(value);
        return ExpandOvercodes(stripped);
    }

    public static char MapGlyph(char value)
    {
        if (value <= 127)
        {
            return value;
        }
        return char.ToUpperInvariant(value) switch
        {
            'Ç' => 'C',
            'Ğ' => 'G',
            'İ' or 'I' => 'I',
            'Ö' => 'O',
            'Ş' => 'S',
            'Ü' => 'U',
            _ => value,
        };
    }

    public static string[] SplitLines(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }
        return text.Replace('\t', ' ')
            .Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
    }

    private static string StripMText(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char current = value[i];
            if (current is '{' or '}')
            {
                continue;
            }
            if (current != '\\' || i + 1 >= value.Length)
            {
                builder.Append(current);
                continue;
            }

            char command = value[++i];
            switch (command)
            {
                case '\\':
                    builder.Append('\\');
                    continue;
                case '{':
                    builder.Append('{');
                    continue;
                case '}':
                    builder.Append('}');
                    continue;
                case '~':
                    builder.Append(' ');
                    continue;
                case 'P':
                    builder.Append('\n');
                    continue;
                default:
                    i = SkipFormatCode(value, i);
                    continue;
            }
        }
        return builder.ToString();
    }

    private static int SkipFormatCode(string value, int commandIndex)
    {
        for (int i = commandIndex + 1; i < value.Length; i++)
        {
            char current = value[i];
            if (current == ';')
            {
                return i;
            }
            if (current == '\\')
            {
                return i - 1;
            }
        }
        return value.Length - 1;
    }

    private static string ExpandOvercodes(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '%' && i + 2 < value.Length && value[i + 1] == '%')
            {
                char code = char.ToLowerInvariant(value[i + 2]);
                switch (code)
                {
                    case 'c':
                        builder.Append('Ø');
                        i += 2;
                        continue;
                    case 'd':
                        builder.Append('°');
                        i += 2;
                        continue;
                    case 'p':
                        builder.Append('±');
                        i += 2;
                        continue;
                    case '%':
                        builder.Append('%');
                        i += 2;
                        continue;
                }
                if (i + 4 < value.Length
                    && int.TryParse(
                        value.AsSpan(i + 2, 3),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int number)
                    && number is > 0 and < 256)
                {
                    builder.Append((char)number);
                    i += 4;
                    continue;
                }
            }
            builder.Append(value[i]);
        }
        return builder.ToString();
    }
}
