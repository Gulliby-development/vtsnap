using System.Globalization;
using System.Text;

namespace Vtsnap;

public static class RenderJson
{
    public static string Render(Snapshot s)
    {
        ArgumentNullException.ThrowIfNull(s);

        int firstScreenLine = s.Lines.Count - s.Rows;
        var result = new StringBuilder();
        result.Append("{\"cols\":");
        AppendNumber(result, s.Cols);
        result.Append(",\"rows\":");
        AppendNumber(result, s.Rows);
        result.Append(",\"title\":");
        result.Append(Text.JsonString(s.Title));
        result.Append(",\"cursor\":{\"row\":");
        AppendNumber(result, s.CursorRow + 1);
        result.Append(",\"col\":");
        AppendNumber(result, s.CursorCol + 1);
        result.Append(",\"visible\":");
        result.Append(s.CursorVisible ? "true" : "false");
        result.Append("},\"lines\":[\n");

        for (int rowIndex = 0; rowIndex < s.Rows; rowIndex++)
        {
            if (rowIndex > 0)
            {
                result.Append(",\n");
            }

            Cell[] line = s.Lines[firstScreenLine + rowIndex];
            result.Append("{\"text\":");
            result.Append(Text.JsonString(RenderRowText(line)));
            result.Append(",\"spans\":[");
            AppendSpans(result, line);
            result.Append("]}");
        }

        result.Append("\n]}\n");
        return result.ToString();
    }

    private static string RenderRowText(Cell[] line)
    {
        int length = line.Length;
        while (length > 0 && line[length - 1].Char == 32)
        {
            length--;
        }

        var text = new StringBuilder(length);
        for (int cellIndex = 0; cellIndex < length; cellIndex++)
        {
            text.Append(Text.FromCodePoint(line[cellIndex].Char));
        }

        return text.ToString();
    }

    private static void AppendSpans(StringBuilder result, Cell[] line)
    {
        int length = line.Length;
        while (length > 0 && line[length - 1] == Cell.Blank)
        {
            length--;
        }

        bool firstSpan = true;
        int cellIndex = 0;
        while (cellIndex < length)
        {
            Style style = line[cellIndex].Style;
            if (style == Style.Default)
            {
                cellIndex++;
                continue;
            }

            int start = cellIndex;
            cellIndex++;
            while (cellIndex < length && line[cellIndex].Style == style)
            {
                cellIndex++;
            }

            if (!firstSpan)
            {
                result.Append(',');
            }

            firstSpan = false;
            result.Append("{\"col\":");
            AppendNumber(result, start + 1);
            result.Append(",\"len\":");
            AppendNumber(result, cellIndex - start);
            result.Append(",\"fg\":");
            AppendColor(result, style.Fg);
            result.Append(",\"bg\":");
            AppendColor(result, style.Bg);
            result.Append(",\"attrs\":[");
            AppendAttributes(result, style.Attrs);
            result.Append("]}");
        }
    }

    private static void AppendColor(StringBuilder result, Color color)
    {
        switch (color.Kind)
        {
            case ColorKind.Default:
                result.Append("null");
                break;
            case ColorKind.Indexed:
                AppendNumber(result, color.Value);
                break;
            case ColorKind.Rgb:
                result.Append(Text.JsonString("#" + color.Value.ToString("x6", CultureInfo.InvariantCulture)));
                break;
        }
    }

    private static void AppendAttributes(StringBuilder result, Attr attrs)
    {
        bool firstAttribute = true;
        AppendAttribute(result, attrs, Attr.Bold, "bold", ref firstAttribute);
        AppendAttribute(result, attrs, Attr.Dim, "dim", ref firstAttribute);
        AppendAttribute(result, attrs, Attr.Italic, "italic", ref firstAttribute);
        AppendAttribute(result, attrs, Attr.Underline, "underline", ref firstAttribute);
        AppendAttribute(result, attrs, Attr.Inverse, "inverse", ref firstAttribute);
        AppendAttribute(result, attrs, Attr.Strike, "strike", ref firstAttribute);
    }

    private static void AppendAttribute(
        StringBuilder result,
        Attr attrs,
        Attr attribute,
        string name,
        ref bool firstAttribute)
    {
        if ((attrs & attribute) == 0)
        {
            return;
        }

        if (!firstAttribute)
        {
            result.Append(',');
        }

        firstAttribute = false;
        result.Append(Text.JsonString(name));
    }

    private static void AppendNumber(StringBuilder result, int value)
    {
        result.Append(value.ToString(CultureInfo.InvariantCulture));
    }
}
