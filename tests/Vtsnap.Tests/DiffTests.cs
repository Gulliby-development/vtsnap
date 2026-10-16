using System.Globalization;
using System.Text;

namespace Vtsnap.Tests;

public sealed class DiffTests
{
    [Fact]
    public void Compare_returns_same_for_identical_blank_screens()
    {
        Snapshot a = SnapshotOf(2, 1, new[] { C(' '), C(' ') });
        Snapshot b = SnapshotOf(2, 1, new[] { C(' '), C(' ') });

        Assert.Equal(new Result("same\n", 0), Diff.Compare(a, b));
    }

    [Fact]
    public void Compare_size_mismatch_short_circuits_and_matches_the_eighty_by_twenty_four_example()
    {
        Snapshot a = BlankSnapshot(80, 24) with
        {
            Title = "different",
            CursorRow = 10,
            CursorCol = 11,
            CursorVisible = false
        };
        Snapshot b = BlankSnapshot(20, 3) with { Title = "also different" };

        Assert.Equal(
            new Result("size differs: 80x24 vs 20x3\n1 difference\n", 1),
            Diff.Compare(a, b));
    }

    [Theory]
    [InlineData(2, 1, 3, 1, "2x1 vs 3x1")]
    [InlineData(2, 1, 2, 2, "2x1 vs 2x2")]
    public void Compare_short_circuits_independent_column_and_row_size_mismatches(
        int aCols,
        int aRows,
        int bCols,
        int bRows,
        string sizeText)
    {
        Snapshot a = BlankSnapshot(aCols, aRows);
        Snapshot b = BlankSnapshot(bCols, bRows);

        Assert.Equal(
            new Result($"size differs: {sizeText}\n1 difference\n", 1),
            Diff.Compare(a, b));
    }

    [Fact]
    public void Compare_ignores_different_scrollback_when_screen_rows_match()
    {
        Snapshot a = SnapshotOf(
            3,
            2,
            new[] { C('o'), C('l'), C('d') },
            new[] { C('A'), C(' '), C(' ') },
            new[] { C('B'), C(' '), C(' ') });
        Snapshot b = SnapshotOf(
            3,
            2,
            new[] { C('n'), C('e'), C('w') },
            new[] { C('A'), C(' '), C(' ') },
            new[] { C('B'), C(' '), C(' ') });

        Assert.Equal(new Result("same\n", 0), Diff.Compare(a, b));
    }

    [Fact]
    public void Compare_reports_text_changes_without_reporting_styles_on_the_same_row()
    {
        Snapshot a = SnapshotOf(1, 1, new[] { C('A', Foreground(1)) });
        Snapshot b = SnapshotOf(1, 1, new[] { C('B', BoldStyle) });

        Assert.Equal(
            new Result("row 1: text differs\n- A\n+ B\n1 difference\n", 1),
            Diff.Compare(a, b));
    }

    [Fact]
    public void Compare_trims_only_trailing_spaces_and_still_compares_styles_on_trimmed_blanks()
    {
        Snapshot a = SnapshotOf(
            3,
            2,
            new[] { C('A'), C(' '), C(' ') },
            new[] { C('A'), C(' '), C(' ') });
        Snapshot b = SnapshotOf(
            3,
            2,
            new[] { C('A'), C(' '), C(' ', Foreground(1)) },
            new[] { C('A'), C(' '), C('\t') });

        Assert.Equal(
            new Result(
                "row 1: style differs at columns 3-3\n"
                + "row 2: text differs\n"
                + "- A\n"
                + "+ A \t\n"
                + "2 differences\n",
                1),
            Diff.Compare(a, b));
    }

    [Fact]
    public void Compare_counts_a_supplementary_character_as_one_cell_for_style_columns()
    {
        Snapshot a = SnapshotOf(
            3,
            1,
            new[] { C(0x1F600), C('x', Foreground(1)), C(' ') });
        Snapshot b = SnapshotOf(
            3,
            1,
            new[] { C(0x1F600), C('x', BoldStyle), C(' ') });

        Assert.Equal(
            new Result("row 1: style differs at columns 2-2\n1 difference\n", 1),
            Diff.Compare(a, b));
    }

    [Fact]
    public void Compare_includes_trailing_styled_blanks_and_spans_noncontiguous_style_changes()
    {
        Snapshot a = SnapshotOf(
            5,
            1,
            new[]
            {
                C('x', Foreground(1)),
                C(' '),
                C(' '),
                C(' '),
                C(' ', Foreground(2))
            });
        Snapshot b = SnapshotOf(
            5,
            1,
            new[]
            {
                C('x'),
                C(' '),
                C(' '),
                C(' '),
                C(' ')
            });

        Assert.Equal(
            new Result("row 1: style differs at columns 1-5\n1 difference\n", 1),
            Diff.Compare(a, b));
    }

    [Fact]
    public void Compare_reports_one_column_for_a_single_style_difference()
    {
        Snapshot a = SnapshotOf(3, 1, new[] { C(' '), C(' '), C(' ', Foreground(1)) });
        Snapshot b = SnapshotOf(3, 1, new[] { C(' '), C(' '), C(' ') });

        Assert.Equal(
            new Result("row 1: style differs at columns 3-3\n1 difference\n", 1),
            Diff.Compare(a, b));
    }

    [Fact]
    public void Compare_reports_multiple_text_rows_in_ascending_order()
    {
        Snapshot a = SnapshotOf(
            1,
            3,
            new[] { C('a') },
            new[] { C('b') },
            new[] { C('c') });
        Snapshot b = SnapshotOf(
            1,
            3,
            new[] { C('A') },
            new[] { C('B') },
            new[] { C('C') });

        Assert.Equal(
            new Result(
                "row 1: text differs\n- a\n+ A\n"
                + "row 2: text differs\n- b\n+ B\n"
                + "row 3: text differs\n- c\n+ C\n"
                + "3 differences\n",
                1),
            Diff.Compare(a, b));
    }

    [Theory]
    [InlineData(2, 2, true, 3, 2, true, "cursor: row 3, column 3 vs row 4, column 3\n")]
    [InlineData(2, 2, true, 2, 3, true, "cursor: row 3, column 3 vs row 3, column 4\n")]
    [InlineData(2, 2, true, 2, 2, false, "cursor: row 3, column 3 vs row 3, column 3\n")]
    public void Compare_reports_cursor_row_column_and_visibility_changes(
        int aRow,
        int aCol,
        bool aVisible,
        int bRow,
        int bCol,
        bool bVisible,
        string diagnostic)
    {
        Snapshot a = BlankSnapshot(4, 4) with
        {
            CursorRow = aRow,
            CursorCol = aCol,
            CursorVisible = aVisible
        };
        Snapshot b = BlankSnapshot(4, 4) with
        {
            CursorRow = bRow,
            CursorCol = bCol,
            CursorVisible = bVisible
        };

        Assert.Equal(new Result(diagnostic + "1 difference\n", 1), Diff.Compare(a, b));
    }

    [Fact]
    public void Compare_json_quotes_titles_with_quotes_backslashes_controls_and_supplementary_characters()
    {
        Snapshot a = SnapshotOf(1, 1, new[] { C(' ') }) with { Title = "a\"\\\n\t😀" };
        Snapshot b = SnapshotOf(1, 1, new[] { C(' ') }) with { Title = "z\"\\\n\t😀" };

        Assert.Equal(
            new Result(
                "title: \"a\\\"\\\\\\n\\t😀\" vs \"z\\\"\\\\\\n\\t😀\"\n1 difference\n",
                1),
            Diff.Compare(a, b));
    }

    [Fact]
    public void Compare_orders_rows_before_cursor_and_title_and_counts_each_diagnostic_once()
    {
        Snapshot a = SnapshotOf(
            2,
            2,
            new[] { C(' '), C(' ') },
            new[] { C('A'), C(' ') }) with
        { Title = "old" };
        Snapshot b = SnapshotOf(
            2,
            2,
            new[] { C(' '), C(' ') },
            new[] { C('B'), C(' ') }) with
        {
            Title = "new",
            CursorRow = 1,
            CursorCol = 1
        };

        Assert.Equal(
            new Result(
                "row 2: text differs\n- A\n+ B\n"
                + "cursor: row 1, column 1 vs row 2, column 2\n"
                + "title: \"old\" vs \"new\"\n"
                + "3 differences\n",
                1),
            Diff.Compare(a, b));
    }

    [Fact]
    public void Compare_returns_the_exact_a_log_and_b_log_worked_example()
    {
        const string aLog = "one\ntwo \u001b[31mred\u001b[0m\nthree\n";
        const string bLog = "one\ntwo \u001b[32mred\u001b[0m\nthrEE\n";
        Source aSource = Replay.Load(Encoding.UTF8.GetBytes(aLog), 10, 4);
        Source bSource = Replay.Load(Encoding.UTF8.GetBytes(bLog), 10, 4);
        Snapshot a = Replay.Play(aSource, null).Terminal.Snapshot(false);
        Snapshot b = Replay.Play(bSource, null).Terminal.Snapshot(false);

        const string expected =
            "row 2: style differs at columns 5-7\nrow 3: text differs\n- three\n+ thrEE\n2 differences\n";

        Assert.Equal(new Result(expected, 1), Diff.Compare(a, b));
    }

    [Fact]
    public void Findings_and_RenderCheck_match_the_messy_log_worked_example()
    {
        const string messyLog = "\u001b[?1049h\u001b[2J\u001b[3;5HMenu\u001b[?25l"
            + "\u001b[31mred\u001b[40;1Hlate\u001b[5n\u001b[?2004h"
            + "\u001b[38;5;300m!";
        Source source = Replay.Load(Encoding.UTF8.GetBytes(messyLog), 80, 24);
        Played played = Replay.Play(source, null);
        IReadOnlyList<Finding> findings = Analysis.Findings(source, played);
        /*
messy.log:36: VT004 position 40;1 is outside the screen (24 rows, 80 columns)
messy.log:47: VT001 ignored sequence "<ESC>[5n"
messy.log:59: VT005 invalid SGR parameters "38;5;300"
messy.log:71: VT006 alternate screen is still active at the end
messy.log:71: VT007 cursor is hidden at the end
messy.log:71: VT008 text style is not reset at the end
6 findings
*/
        const string expected = "messy.log:36: VT004 position 40;1 is outside the screen (24 rows, 80 columns)\nmessy.log:47: VT001 ignored sequence \"<ESC>[5n\"\nmessy.log:59: VT005 invalid SGR parameters \"38;5;300\"\nmessy.log:71: VT006 alternate screen is still active at the end\nmessy.log:71: VT007 cursor is hidden at the end\nmessy.log:71: VT008 text style is not reset at the end\n6 findings\n";

        Assert.Equal(new Result(expected, 1), Analysis.RenderCheck("messy.log", findings));
    }

    [Fact]
    public void Compare_is_repeatable_nonmutating_and_uses_invariant_numeric_output()
    {
        Cell[][] aLines =
        {
            new[] { C(' '), C(' ') },
            new[] { C('A'), C(' ') }
        };
        Cell[][] bLines =
        {
            new[] { C(' '), C(' ') },
            new[] { C('B'), C(' ') }
        };
        Snapshot a = SnapshotOf(2, 2, aLines) with { Title = "old" };
        Snapshot b = SnapshotOf(2, 2, bLines) with
        {
            Title = "new",
            CursorRow = 1,
            CursorCol = 1
        };
        Cell[][] originalALines = { aLines[0].ToArray(), aLines[1].ToArray() };
        Cell[][] originalBLines = { bLines[0].ToArray(), bLines[1].ToArray() };
        CultureInfo originalCulture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-EG");

            Result first = Diff.Compare(a, b);
            Result second = Diff.Compare(a, b);

            Assert.Equal(first, second);
            Assert.Equal(
                new Result(
                    "row 2: text differs\n- A\n+ B\n"
                    + "cursor: row 1, column 1 vs row 2, column 2\n"
                    + "title: \"old\" vs \"new\"\n"
                    + "3 differences\n",
                    1),
                first);
            Assert.Same(aLines, a.Lines);
            Assert.Same(bLines, b.Lines);
            Assert.Equal(originalALines, a.Lines);
            Assert.Equal(originalBLines, b.Lines);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    private static Snapshot BlankSnapshot(int cols, int rows) =>
        Replay.Play(Replay.Load(Array.Empty<byte>(), cols, rows), null).Terminal.Snapshot(false);

    private static Snapshot SnapshotOf(int cols, int rows, params Cell[][] lines) =>
        new(cols, rows, string.Empty, 0, 0, true, lines);

    private static Cell C(int codePoint, Style? style = null) =>
        new(codePoint, style ?? Style.Default);

    private static Style Foreground(int index) =>
        new(new Color(ColorKind.Indexed, index), Color.Default, Attr.None);

    private static Style BoldStyle =>
        new(Color.Default, Color.Default, Attr.Bold);
}
