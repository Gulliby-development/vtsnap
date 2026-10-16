namespace Vtsnap;

public static class RenderText
{
    public static string Render(Snapshot s)
    {
        ArgumentNullException.ThrowIfNull(s);

        int lastNonEmptyLine = -1;
        for (int lineIndex = s.Lines.Count - 1; lineIndex >= 0; lineIndex--)
        {
            Cell[] line = s.Lines[lineIndex];
            if (line.Any(cell => cell.Char != 32))
            {
                lastNonEmptyLine = lineIndex;
                break;
            }
        }

        if (lastNonEmptyLine < 0)
        {
            return string.Empty;
        }

        var result = new System.Text.StringBuilder();
        for (int lineIndex = 0; lineIndex <= lastNonEmptyLine; lineIndex++)
        {
            Cell[] line = s.Lines[lineIndex];
            int lastNonSpaceCell = line.Length - 1;
            while (lastNonSpaceCell >= 0 && line[lastNonSpaceCell].Char == 32)
            {
                lastNonSpaceCell--;
            }

            for (int cellIndex = 0; cellIndex <= lastNonSpaceCell; cellIndex++)
            {
                result.Append(Text.FromCodePoint(line[cellIndex].Char));
            }

            result.Append('\n');
        }

        return result.ToString();
    }
}
