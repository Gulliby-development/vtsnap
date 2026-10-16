using System.Globalization;
using System.Text;

namespace Vtsnap.Tests;

public sealed class AnalysisTests
{
    [Fact]
    public void Findings_returns_no_diagnostics_for_a_clean_replay()
    {
        (Source source, Played played) = ReplayOf(string.Empty);

        Assert.Empty(Analysis.Findings(source, played));
    }

    [Theory]
    [InlineData("\u001b[5n", "VT001", "ignored sequence \"<ESC>[5n\"")]
    [InlineData("\u001b[30;1H", "VT004", "position 30;1 is outside the screen (24 rows, 80 columns)")]
    [InlineData("\u001b[38;5;300m", "VT005", "invalid SGR parameters \"38;5;300\"")]
    public void Findings_preserves_upstream_terminal_diagnostics(
        string input,
        string code,
        string message)
    {
        (Source source, Played played) = ReplayOf(input);

        Assert.Equal(
            new[] { new Finding(0, code, message) },
            Analysis.Findings(source, played));
    }

    [Fact]
    public void Findings_reports_each_invalid_utf8_replacement_at_its_code_point_position()
    {
        byte[] bytes = { 0x61, 0xFF, 0x62 };
        Source source = Replay.Load(bytes, null, null);
        Played played = Replay.Play(source, null);

        Assert.Equal(new[] { 1 }, source.Replaced);
        Assert.Equal(
            new[] { new Finding(1, "VT002", "invalid UTF-8 byte sequence") },
            Analysis.Findings(source, played));
        Assert.Equal(new byte[] { 0x61, 0xFF, 0x62 }, bytes);
    }

    [Theory]
    [InlineData("A\u001b[", "esc", "unterminated escape sequence")]
    [InlineData("A\u001b]unfinished", "osc", "unterminated OSC string")]
    [InlineData("A\u001bPunfinished", "str", "unterminated control string")]
    public void Findings_maps_unterminated_events_to_exact_vt003_messages(
        string input,
        string expectedKind,
        string message)
    {
        (Source source, Played played) = ReplayOf(input);

        Assert.Contains(new UnterminatedEvent(1, expectedKind), played.Events);
        Assert.Contains(new Finding(1, "VT003", message), Analysis.Findings(source, played));
    }

    [Theory]
    [InlineData("\u001b[?1049h", "VT006", "alternate screen is still active at the end")]
    [InlineData("\u001b[?25l", "VT007", "cursor is hidden at the end")]
    [InlineData("\u001b[31m", "VT008", "text style is not reset at the end")]
    [InlineData("\u001b[44m", "VT008", "text style is not reset at the end")]
    [InlineData("\u001b[1m", "VT008", "text style is not reset at the end")]
    [InlineData("\u001b[3;5r", "VT009", "scroll region is not reset at the end")]
    public void Findings_places_dirty_end_state_diagnostics_at_total_code_points(
        string input,
        string code,
        string message)
    {
        (Source source, Played played) = ReplayOf(input);

        Assert.Equal(
            new[] { new Finding(source.TotalCodePoints, code, message) },
            Analysis.Findings(source, played));
    }

    [Fact]
    public void Findings_adds_no_end_state_diagnostics_when_all_state_is_reset()
    {
        const string input = "\u001b[?1049h\u001b[?1049l"
            + "\u001b[?25l\u001b[?25h"
            + "\u001b[31m\u001b[0m"
            + "\u001b[3;5r\u001b[r";
        (Source source, Played played) = ReplayOf(input);

        Assert.DoesNotContain(
            Analysis.Findings(source, played),
            finding => finding.Code is "VT006" or "VT007" or "VT008" or "VT009");
    }

    [Fact]
    public void Findings_keeps_all_jointly_dirty_end_states_in_code_order_at_the_end_position()
    {
        const string input = "\u001b[?1049h\u001b[?25l\u001b[31m\u001b[3;5r";
        (Source source, Played played) = ReplayOf(input);

        Assert.Equal(
            new[]
            {
                new Finding(source.TotalCodePoints, "VT006", "alternate screen is still active at the end"),
                new Finding(source.TotalCodePoints, "VT007", "cursor is hidden at the end"),
                new Finding(source.TotalCodePoints, "VT008", "text style is not reset at the end"),
                new Finding(source.TotalCodePoints, "VT009", "scroll region is not reset at the end")
            },
            Analysis.Findings(source, played));
    }

    [Fact]
    public void Findings_sorts_by_position_and_ordinal_code_preserving_ties_duplicates_and_inputs()
    {
        var terminal = new Terminal(5, 1);
        terminal.Apply(new EscEvent(2, string.Empty, 'c', "<ESC>c"));
        terminal.Apply(new EscEvent(0, string.Empty, 'c', "RAW"));
        terminal.Apply(new EscEvent(2, string.Empty, 'c', "SECOND"));
        Source source = new(
            true,
            5,
            1,
            Array.Empty<CastEvent>(),
            new[] { 2, 2, 0 },
            5);
        VtEvent[] events =
        {
            new UnterminatedEvent(2, "osc"),
            new UnterminatedEvent(0, "esc"),
            new UnterminatedEvent(2, "str")
        };
        var played = new Played(terminal, events);
        Finding[] terminalFindingsBefore = terminal.Findings.ToArray();

        Finding[] expected =
        {
            new(0, "VT001", "ignored sequence \"RAW\""),
            new(0, "VT002", "invalid UTF-8 byte sequence"),
            new(0, "VT003", "unterminated escape sequence"),
            new(2, "VT001", "ignored sequence \"<ESC>c\""),
            new(2, "VT001", "ignored sequence \"SECOND\""),
            new(2, "VT002", "invalid UTF-8 byte sequence"),
            new(2, "VT002", "invalid UTF-8 byte sequence"),
            new(2, "VT003", "unterminated OSC string"),
            new(2, "VT003", "unterminated control string")
        };

        List<Finding> first = Analysis.Findings(source, played);
        List<Finding> second = Analysis.Findings(source, played);

        Assert.Equal(expected, first);
        Assert.Equal(expected, second);
        Assert.NotSame(first, second);
        Assert.Equal(new[] { 2, 2, 0 }, source.Replaced);
        Assert.Equal(terminalFindingsBefore, terminal.Findings);
        Assert.Equal(
            new VtEvent[]
            {
                new UnterminatedEvent(2, "osc"),
                new UnterminatedEvent(0, "esc"),
                new UnterminatedEvent(2, "str")
            },
            played.Events);
    }

    [Fact]
    public void RenderCheck_returns_exact_ok_text_and_success_code_for_no_findings()
    {
        Result result = Analysis.RenderCheck("report.log", Array.Empty<Finding>());

        Assert.Equal(new Result("ok\n", 0), result);
    }

    [Fact]
    public void RenderCheck_uses_verbatim_stdin_display_and_exact_singular_output()
    {
        Result result = Analysis.RenderCheck(
            "<stdin>",
            new[] { new Finding(1, "VT002", "invalid UTF-8 byte sequence") });

        Assert.Equal(
            new Result("<stdin>:1: VT002 invalid UTF-8 byte sequence\n1 finding\n", 1),
            result);
    }

    [Fact]
    public void RenderCheck_sorts_a_copy_stably_and_formats_numbers_invariantly()
    {
        List<Finding> findings = new()
        {
            new(4, "VT003", "last"),
            new(1, "VT002", "replacement"),
            new(1, "VT001", "first"),
            new(1, "VT001", "second")
        };
        Finding[] original = findings.ToArray();
        CultureInfo originalCulture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-EG");

            Result result = Analysis.RenderCheck("path/to/log", findings);

            Assert.Equal(
                new Result(
                    "path/to/log:1: VT001 first\n"
                    + "path/to/log:1: VT001 second\n"
                    + "path/to/log:1: VT002 replacement\n"
                    + "path/to/log:4: VT003 last\n"
                    + "4 findings\n",
                    1),
                result);
            Assert.Equal(original, findings);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void RenderCheck_preserves_every_duplicate_and_uses_plural_count()
    {
        Result result = Analysis.RenderCheck(
            "a.log",
            new[]
            {
                new Finding(3, "VT002", "invalid UTF-8 byte sequence"),
                new Finding(3, "VT002", "invalid UTF-8 byte sequence")
            });

        Assert.Equal(
            new Result(
                "a.log:3: VT002 invalid UTF-8 byte sequence\n"
                + "a.log:3: VT002 invalid UTF-8 byte sequence\n"
                + "2 findings\n",
                1),
            result);
    }

    [Fact]
    public void Findings_and_RenderCheck_match_the_messy_log_worked_example()
    {
        const string messyLog = "\u001b[?1049h\u001b[2J\u001b[3;5HMenu\u001b[?25l"
            + "\u001b[31mred\u001b[40;1Hlate\u001b[5n\u001b[?2004h"
            + "\u001b[38;5;300m!";
        (Source source, Played played) = ReplayOf(messyLog);
        List<Finding> findings = Analysis.Findings(source, played);

        Result result = Analysis.RenderCheck("messy.log", findings);

        Assert.Equal(
            new Result(
                "messy.log:36: VT004 position 40;1 is outside the screen (24 rows, 80 columns)\n"
                + "messy.log:47: VT001 ignored sequence \"<ESC>[5n\"\n"
                + "messy.log:59: VT005 invalid SGR parameters \"38;5;300\"\n"
                + "messy.log:71: VT006 alternate screen is still active at the end\n"
                + "messy.log:71: VT007 cursor is hidden at the end\n"
                + "messy.log:71: VT008 text style is not reset at the end\n"
                + "6 findings\n",
                1),
            result);
        Assert.Equal(71, source.TotalCodePoints);
    }

    [Fact]
    public void Analysis_and_diff_check_outputs_match_the_a_log_and_b_log_worked_example()
    {
        const string aLog = "one\ntwo \u001b[31mred\u001b[0m\nthree\n";
        const string bLog = "one\ntwo \u001b[32mred\u001b[0m\nthrEE\n";
        Source aSource = Replay.Load(Encoding.UTF8.GetBytes(aLog), 10, 4);
        Source bSource = Replay.Load(Encoding.UTF8.GetBytes(bLog), 10, 4);
        Snapshot a = Replay.Play(aSource, null).Terminal.Snapshot(false);
        Snapshot b = Replay.Play(bSource, null).Terminal.Snapshot(false);

        Result result = Diff.Compare(a, b);

        Assert.Equal(
            new Result(
                "row 2: style differs at columns 5-7\n"
                + "row 3: text differs\n"
                + "- three\n"
                + "+ thrEE\n"
                + "2 differences\n",
                1),
            result);
    }

    private static (Source Source, Played Played) ReplayOf(string input)
    {
        Source source = Replay.Load(Encoding.UTF8.GetBytes(input), null, null);
        return (source, Replay.Play(source, null));
    }
}
