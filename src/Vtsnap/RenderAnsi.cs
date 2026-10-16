using System.Globalization;
using System.Text;

namespace Vtsnap;

public static class RenderAnsi
{
    public static string Render(Snapshot s)
    {
        ArgumentNullException.ThrowIfNull(s);

        int lastLine = -1;
        for (int lineIndex = 0; lineIndex < s.Lines.Count; lineIndex++)
        {
            if (TrimmedLength(s.Lines[lineIndex]) > 0)
            {
                lastLine = lineIndex;
            }
        }

        if (lastLine < 0)
        {
            return string.Empty;
        }

        var result = new StringBuilder();
        for (int lineIndex = 0; lineIndex <= lastLine; lineIndex++)
        {
            Cell[] line = s.Lines[lineIndex];
            int length = TrimmedLength(line);
            Style currentStyle = Style.Default;

            for (int cellIndex = 0; cellIndex < length; cellIndex++)
            {
                Cell cell = line[cellIndex];
                if (cell.Style != currentStyle)
                {
                    AppendStyle(result, cell.Style);
                    currentStyle = cell.Style;
                }

                result.Append(Text.FromCodePoint(cell.Char));
            }

            if (currentStyle != Style.Default)
            {
                result.Append("\u001b[0m");
            }

            result.Append('\n');
        }

        return result.ToString();
    }

    private static int TrimmedLength(Cell[] line)
    {
        int length = line.Length;
        while (length > 0 && line[length - 1] == Cell.Blank)
        {
            length--;
        }

        return length;
    }

    private static void AppendStyle(StringBuilder result, Style style)
    {
        if (style == Style.Default)
        {
            result.Append("\u001b[0m");
            return;
        }

        result.Append("\u001b[0;");
        bool hasCode = false;

        if ((style.Attrs & Attr.Bold) != 0)
        {
            AppendCode(result, 1, ref hasCode);
        }

        if ((style.Attrs & Attr.Dim) != 0)
        {
            AppendCode(result, 2, ref hasCode);
        }

        if ((style.Attrs & Attr.Italic) != 0)
        {
            AppendCode(result, 3, ref hasCode);
        }

        if ((style.Attrs & Attr.Underline) != 0)
        {
            AppendCode(result, 4, ref hasCode);
        }

        if ((style.Attrs & Attr.Inverse) != 0)
        {
            AppendCode(result, 7, ref hasCode);
        }

        if ((style.Attrs & Attr.Strike) != 0)
        {
            AppendCode(result, 9, ref hasCode);
        }

        AppendColor(result, style.Fg, foreground: true, ref hasCode);
        AppendColor(result, style.Bg, foreground: false, ref hasCode);
        result.Append('m');
    }

    private static void AppendColor(StringBuilder result, Color color, bool foreground, ref bool hasCode)
    {
        switch (color.Kind)
        {
            case ColorKind.Default:
                break;
            case ColorKind.Indexed when color.Value < 8:
                AppendCode(result, (foreground ? 30 : 40) + color.Value, ref hasCode);
                break;
            case ColorKind.Indexed when color.Value < 16:
                AppendCode(result, (foreground ? 90 : 100) + color.Value - 8, ref hasCode);
                break;
            case ColorKind.Indexed:
                AppendCode(result, foreground ? 38 : 48, ref hasCode);
                AppendCode(result, 5, ref hasCode);
                AppendCode(result, color.Value, ref hasCode);
                break;
            case ColorKind.Rgb:
                AppendCode(result, foreground ? 38 : 48, ref hasCode);
                AppendCode(result, 2, ref hasCode);
                AppendCode(result, (color.Value >> 16) & 0xFF, ref hasCode);
                AppendCode(result, (color.Value >> 8) & 0xFF, ref hasCode);
                AppendCode(result, color.Value & 0xFF, ref hasCode);
                break;
        }
    }

    private static void AppendCode(StringBuilder result, int code, ref bool hasCode)
    {
        if (hasCode)
        {
            result.Append(';');
        }

        result.Append(code.ToString(CultureInfo.InvariantCulture));
        hasCode = true;
    }
}
