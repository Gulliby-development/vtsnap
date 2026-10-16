using System.Globalization;
using System.Text;

namespace Vtsnap;

public static class Diff
{
    public static Result Compare(Snapshot a, Snapshot b) =>
        Compare(a, b, ignoreStyle: false);

    public static Result Compare(Snapshot a, Snapshot b, bool ignoreStyle)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        if (a.Cols != b.Cols || a.Rows != b.Rows)
        {
            string sizeText = "size differs: "
                + Size(a.Cols, a.Rows)
                + " vs "
                + Size(b.Cols, b.Rows)
                + "\n1 difference\n";
            return new Result(sizeText, 1);
        }

        var output = new StringBuilder();
        int differences = 0;
        int aScreenOffset = a.Lines.Count - a.Rows;
        int bScreenOffset = b.Lines.Count - b.Rows;

        for (int row = 0; row < a.Rows; row++)
        {
            Cell[] aLine = a.Lines[aScreenOffset + row];
            Cell[] bLine = b.Lines[bScreenOffset + row];
            string aText = RowText(aLine);
            string bText = RowText(bLine);

            if (!string.Equals(aText, bText, StringComparison.Ordinal))
            {
                output.Append("row ");
                output.Append(Number(row + 1));
                output.Append(": text differs\n- ");
                output.Append(aText);
                output.Append("\n+ ");
                output.Append(bText);
                output.Append('\n');
                differences++;
                continue;
            }

            if (!ignoreStyle)
            {
                int firstStyleDifference = -1;
                int lastStyleDifference = -1;
                for (int column = 0; column < a.Cols; column++)
                {
                    if (aLine[column].Style == bLine[column].Style)
                    {
                        continue;
                    }

                    if (firstStyleDifference < 0)
                    {
                        firstStyleDifference = column;
                    }

                    lastStyleDifference = column;
                }

                if (firstStyleDifference >= 0)
                {
                    output.Append("row ");
                    output.Append(Number(row + 1));
                    output.Append(": style differs at columns ");
                    output.Append(Number(firstStyleDifference + 1));
                    output.Append('-');
                    output.Append(Number(lastStyleDifference + 1));
                    output.Append('\n');
                    differences++;
                }
            }
        }

        if (a.CursorRow != b.CursorRow
            || a.CursorCol != b.CursorCol
            || a.CursorVisible != b.CursorVisible)
        {
            output.Append("cursor: row ");
            output.Append(Number(a.CursorRow + 1));
            output.Append(", column ");
            output.Append(Number(a.CursorCol + 1));
            output.Append(" vs row ");
            output.Append(Number(b.CursorRow + 1));
            output.Append(", column ");
            output.Append(Number(b.CursorCol + 1));
            output.Append('\n');
            differences++;
        }

        if (!string.Equals(a.Title, b.Title, StringComparison.Ordinal))
        {
            output.Append("title: ");
            output.Append(Text.JsonString(a.Title));
            output.Append(" vs ");
            output.Append(Text.JsonString(b.Title));
            output.Append('\n');
            differences++;
        }

        if (differences == 0)
        {
            output.Append("same\n");
            return new Result(output.ToString(), 0);
        }

        output.Append(Text.Plural(differences, "difference", "differences"));
        output.Append('\n');
        return new Result(output.ToString(), 1);
    }

    private static string RowText(Cell[] line)
    {
        int length = line.Length;
        while (length > 0 && line[length - 1].Char == 0x20)
        {
            length--;
        }

        var text = new StringBuilder(length);
        for (int column = 0; column < length; column++)
        {
            text.Append(Text.FromCodePoint(line[column].Char));
        }

        return text.ToString();
    }

    private static string Size(int cols, int rows) =>
        Number(cols) + "x" + Number(rows);

    private static string Number(int value) =>
        value.ToString(CultureInfo.InvariantCulture);
}
