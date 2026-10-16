using System.Globalization;
using System.Text;

namespace Vtsnap;

public static class Analysis
{
    public static List<Finding> Findings(Source source, Played played)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(played);

        var findings = new List<Finding>(played.Terminal.Findings);
        foreach (int position in source.Replaced)
        {
            findings.Add(new Finding(position, "VT002", "invalid UTF-8 byte sequence"));
        }

        foreach (VtEvent ev in played.Events)
        {
            if (ev is not UnterminatedEvent unterminated)
            {
                continue;
            }

            string? message = unterminated.What switch
            {
                "esc" => "unterminated escape sequence",
                "osc" => "unterminated OSC string",
                "str" => "unterminated control string",
                _ => null
            };

            if (message is not null)
            {
                findings.Add(new Finding(unterminated.Pos, "VT003", message));
            }
        }

        Terminal terminal = played.Terminal;
        int endPosition = source.TotalCodePoints;
        if (terminal.AltScreen)
        {
            findings.Add(new Finding(
                endPosition,
                "VT006",
                "alternate screen is still active at the end"));
        }

        if (!terminal.CursorVisible)
        {
            findings.Add(new Finding(endPosition, "VT007", "cursor is hidden at the end"));
        }

        if (terminal.CurrentStyle != Style.Default)
        {
            findings.Add(new Finding(endPosition, "VT008", "text style is not reset at the end"));
        }

        if (!terminal.RegionIsFull)
        {
            findings.Add(new Finding(endPosition, "VT009", "scroll region is not reset at the end"));
        }

        return Sort(findings);
    }

    public static Result RenderCheck(string display, IReadOnlyList<Finding> findings)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(findings);

        List<Finding> sorted = Sort(findings);
        if (sorted.Count == 0)
        {
            return new Result("ok\n", 0);
        }

        var output = new StringBuilder();
        foreach (Finding finding in sorted)
        {
            output.Append(display);
            output.Append(':');
            output.Append(finding.Pos.ToString(CultureInfo.InvariantCulture));
            output.Append(": ");
            output.Append(finding.Code);
            output.Append(' ');
            output.Append(finding.Message);
            output.Append('\n');
        }

        output.Append(Text.Plural(sorted.Count, "finding", "findings"));
        output.Append('\n');
        return new Result(output.ToString(), 1);
    }

    private static List<Finding> Sort(IEnumerable<Finding> findings) =>
        findings
            .OrderBy(finding => finding.Pos)
            .ThenBy(finding => finding.Code, StringComparer.Ordinal)
            .ToList();
}
