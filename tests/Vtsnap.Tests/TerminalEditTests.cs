namespace Vtsnap.Tests;

public sealed class TerminalEditTests
{
    [Fact]
    public void Erase_display_mode_zero_erases_from_the_cursor_through_the_screen_end()
    {
        var terminal = TerminalWithRows(4, 3, "ABCD\r\nEFGH\r\nIJKL");
        ApplyStream(terminal, "\u001b[2;2H\u001b[0J");

        Assert.Equal(new[] { "ABCD", "E   ", "    " }, Rows(terminal.Snapshot(false)));
        Assert.Equal(1, terminal.Snapshot(false).CursorRow);
        Assert.Equal(1, terminal.Snapshot(false).CursorCol);
    }

    [Fact]
    public void Erase_display_mode_one_includes_the_cursor_and_keeps_following_cells()
    {
        var terminal = TerminalWithRows(4, 3, "ABCD\r\nEFGH\r\nIJKL");
        ApplyStream(terminal, "\u001b[2;2H\u001b[1J");

        Assert.Equal(new[] { "    ", "  GH", "IJKL" }, Rows(terminal.Snapshot(false)));
        Assert.Equal(1, terminal.Snapshot(false).CursorRow);
        Assert.Equal(1, terminal.Snapshot(false).CursorCol);
    }

    [Fact]
    public void Erase_display_mode_two_blanks_the_screen_without_moving_the_cursor()
    {
        var terminal = TerminalWithRows(4, 2, "ABCD\r\nEFGH");
        ApplyStream(terminal, "\u001b[2;3H\u001b[1;3;44m\u001b[2J");

        Snapshot snapshot = terminal.Snapshot(false);
        Cell expected = BackgroundBlank(4);

        Assert.Equal(new[] { "    ", "    " }, Rows(snapshot));
        Assert.Equal(expected, snapshot.Lines[0][0]);
        Assert.Equal(expected, snapshot.Lines[1][3]);
        Assert.Equal(new Color(ColorKind.Indexed, 4), terminal.CurrentStyle.Bg);
        Assert.Equal(Attr.Bold | Attr.Italic, terminal.CurrentStyle.Attrs);
        Assert.Equal(1, snapshot.CursorRow);
        Assert.Equal(2, snapshot.CursorCol);
    }

    [Fact]
    public void Erase_display_mode_three_clears_screen_and_existing_scrollback()
    {
        var terminal = TerminalWithRows(1, 2, "A\nB\nC");
        Assert.Equal(new[] { "A", "B", "C" }, Rows(terminal.Snapshot(true)));

        ApplyStream(terminal, "\u001b[44m\u001b[3J");

        Snapshot snapshot = terminal.Snapshot(true);
        Assert.Equal(2, snapshot.Rows);
        Assert.Equal(new[] { " ", " " }, Rows(snapshot));
        Assert.Equal(BackgroundBlank(4), snapshot.Lines[0][0]);
        Assert.Equal(BackgroundBlank(4), snapshot.Lines[1][0]);
        Assert.Equal(1, snapshot.CursorRow);
        Assert.Equal(0, snapshot.CursorCol);
    }

    [Fact]
    public void Erase_display_with_an_omitted_or_empty_parameter_uses_mode_zero()
    {
        var omitted = TerminalWithRows(3, 2, "ABC\r\nDEF");
        ApplyStream(omitted, "\u001b[2;2H\u001b[J");

        var empty = TerminalWithRows(3, 2, "ABC\r\nDEF");
        ApplyStream(empty, "\u001b[2;2H\u001b[;J");

        Assert.Equal(new[] { "ABC", "D  " }, Rows(omitted.Snapshot(false)));
        Assert.Equal(new[] { "ABC", "D  " }, Rows(empty.Snapshot(false)));
        Assert.Empty(omitted.Findings);
        Assert.Empty(empty.Findings);
    }

    [Fact]
    public void Erase_line_mode_zero_and_omitted_mode_erase_from_the_cursor_through_the_line_end()
    {
        var explicitZero = TerminalWithRows(4, 1, "ABCD");
        ApplyStream(explicitZero, "\u001b[1;2H\u001b[0K");

        var omitted = TerminalWithRows(4, 1, "ABCD");
        ApplyStream(omitted, "\u001b[1;2H\u001b[K");

        var empty = TerminalWithRows(4, 1, "ABCD");
        ApplyStream(empty, "\u001b[1;2H\u001b[;K");

        Assert.Equal("A   ", Row(explicitZero.Snapshot(false), 0));
        Assert.Equal("A   ", Row(omitted.Snapshot(false), 0));
        Assert.Equal("A   ", Row(empty.Snapshot(false), 0));
        Assert.Equal(0, explicitZero.Snapshot(false).CursorRow);
        Assert.Equal(1, explicitZero.Snapshot(false).CursorCol);
    }

    [Fact]
    public void Erase_line_mode_one_includes_the_cursor_and_keeps_following_cells()
    {
        var terminal = TerminalWithRows(4, 1, "ABCD");
        ApplyStream(terminal, "\u001b[1;2H\u001b[1K");

        Assert.Equal("  CD", Row(terminal.Snapshot(false), 0));
    }

    [Fact]
    public void Erase_line_mode_two_uses_background_only_fill_and_preserves_cursor()
    {
        var terminal = TerminalWithRows(4, 1, "ABCD");
        ApplyStream(terminal, "\u001b[1;3H\u001b[1;44m\u001b[2K");

        Snapshot snapshot = terminal.Snapshot(false);
        Assert.Equal("    ", Row(snapshot, 0));
        Assert.Equal(BackgroundBlank(4), snapshot.Lines[0][0]);
        Assert.Equal(BackgroundBlank(4), snapshot.Lines[0][3]);
        Assert.Equal(0, snapshot.CursorRow);
        Assert.Equal(2, snapshot.CursorCol);
        Assert.Equal(Attr.Bold, terminal.CurrentStyle.Attrs);
    }

    [Fact]
    public void Unsupported_erase_modes_leave_the_screen_unchanged_and_report_raw_sequences()
    {
        var terminal = TerminalWithRows(3, 1, "ABC");
        Snapshot before = terminal.Snapshot(false);

        terminal.Apply(new CsiEvent(11, '\0', new[] { 4 }, string.Empty, 'J', false, "<ESC>[4J"));
        terminal.Apply(new CsiEvent(12, '\0', new[] { 3 }, string.Empty, 'K', false, "<ESC>[3K"));

        Assert.Equal(before.Lines, terminal.Snapshot(false).Lines);
        Assert.Equal(
            new[]
            {
                new Finding(11, "VT001", "ignored sequence \"<ESC>[4J\""),
                new Finding(12, "VT001", "ignored sequence \"<ESC>[3K\"")
            },
            terminal.Findings);
    }

    [Fact]
    public void Insert_rows_changes_only_the_region_and_homes_the_cursor_column()
    {
        var terminal = TerminalWithRows(4, 4, "AAAA\r\nBBBB\r\nCCCC\r\nDDDD");
        ApplyStream(terminal, "\u001b[2;4r\u001b[3;3H\u001b[L");

        Assert.Equal(new[] { "AAAA", "BBBB", "    ", "CCCC" }, Rows(terminal.Snapshot(true)));
        Assert.Equal(2, terminal.Snapshot(false).CursorRow);
        Assert.Equal(0, terminal.Snapshot(false).CursorCol);
        Assert.Empty(terminal.Findings);
    }

    [Fact]
    public void Delete_rows_changes_only_the_region_and_homes_the_cursor_column()
    {
        var terminal = TerminalWithRows(4, 4, "AAAA\r\nBBBB\r\nCCCC\r\nDDDD");
        ApplyStream(terminal, "\u001b[2;4r\u001b[2;3H\u001b[M");

        Assert.Equal(new[] { "AAAA", "CCCC", "DDDD", "    " }, Rows(terminal.Snapshot(false)));
        Assert.Equal(1, terminal.Snapshot(false).CursorRow);
        Assert.Equal(0, terminal.Snapshot(false).CursorCol);
        Assert.Empty(terminal.Findings);
    }

    [Fact]
    public void Zero_row_edit_count_uses_the_default_of_one_row()
    {
        var terminal = TerminalWithRows(1, 3, "A\nB\nC");
        ApplyStream(terminal, "\u001b[2;1H\u001b[0L");

        Assert.Equal(new[] { "A", " ", "B" }, Rows(terminal.Snapshot(false)));
        Assert.Equal(1, terminal.Snapshot(false).CursorRow);
        Assert.Equal(0, terminal.Snapshot(false).CursorCol);
    }

    [Fact]
    public void Insert_rows_caps_a_large_count_to_the_remaining_region_height()
    {
        var terminal = TerminalWithRows(1, 4, "A\nB\nC\nD");
        ApplyStream(terminal, "\u001b[2;4r\u001b[2;1H\u001b[65535L");

        Assert.Equal(new[] { "A", " ", " ", " " }, Rows(terminal.Snapshot(true)));
        Assert.Equal(1, terminal.Snapshot(false).CursorRow);
        Assert.Equal(0, terminal.Snapshot(false).CursorCol);
        Assert.Empty(terminal.Findings);
    }

    [Fact]
    public void Delete_rows_caps_a_large_count_to_the_remaining_region_height()
    {
        var terminal = TerminalWithRows(1, 4, "A\nB\nC\nD");
        ApplyStream(terminal, "\u001b[2;4r\u001b[2;1H\u001b[65535M");

        Assert.Equal(new[] { "A", " ", " ", " " }, Rows(terminal.Snapshot(false)));
        Assert.Equal(1, terminal.Snapshot(false).CursorRow);
        Assert.Equal(0, terminal.Snapshot(false).CursorCol);
    }

    [Fact]
    public void Row_edits_outside_the_scroll_region_do_nothing_and_preserve_the_cursor()
    {
        var terminal = TerminalWithRows(3, 3, "AAA\r\nBBB\r\nCCC");
        ApplyStream(terminal, "\u001b[2;3r\u001b[1;2H");
        Snapshot before = terminal.Snapshot(false);

        terminal.Apply(new CsiEvent(22, '\0', Array.Empty<int>(), string.Empty, 'L', false, "<ESC>[L"));
        terminal.Apply(new CsiEvent(23, '\0', new[] { 2 }, string.Empty, 'M', false, "<ESC>[2M"));

        Assert.Equal(before.Lines, terminal.Snapshot(false).Lines);
        Assert.Equal(before.CursorRow, terminal.Snapshot(false).CursorRow);
        Assert.Equal(before.CursorCol, terminal.Snapshot(false).CursorCol);
        Assert.Empty(terminal.Findings);
    }

    [Fact]
    public void Inserted_rows_use_background_only_blank_cells()
    {
        var terminal = TerminalWithRows(2, 3, "AA\r\nBB\r\nCC");
        ApplyStream(terminal, "\u001b[2;3r\u001b[2;1H\u001b[1;3;44m\u001b[L");

        Snapshot snapshot = terminal.Snapshot(false);
        Assert.Equal(new[] { "AA", "  ", "BB" }, Rows(snapshot));
        Assert.Equal(BackgroundBlank(4), snapshot.Lines[1][0]);
        Assert.Equal(BackgroundBlank(4), snapshot.Lines[1][1]);
        Assert.Equal(new Style(Color.Default, new Color(ColorKind.Indexed, 4), Attr.Bold | Attr.Italic), terminal.CurrentStyle);
    }

    [Fact]
    public void Scroll_up_moves_region_rows_and_appends_removed_main_rows_to_scrollback()
    {
        var terminal = TerminalWithRows(1, 3, "A\nB\nC");
        ApplyStream(terminal, "\u001b[1;1H\u001b[S");

        Snapshot snapshot = terminal.Snapshot(true);
        Assert.Equal(3, snapshot.Rows);
        Assert.Equal(new[] { "A", "B", "C", " " }, Rows(snapshot));
        Assert.Equal(0, snapshot.CursorRow);
        Assert.Equal(0, snapshot.CursorCol);
        Assert.Empty(terminal.Findings);
    }

    [Fact]
    public void Scroll_down_moves_rows_down_without_appending_to_existing_scrollback()
    {
        var terminal = TerminalWithRows(1, 2, "A\nB\nC");
        Assert.Equal(new[] { "A", "B", "C" }, Rows(terminal.Snapshot(true)));

        ApplyStream(terminal, "\u001b[1;1H\u001b[T");

        Snapshot snapshot = terminal.Snapshot(true);
        Assert.Equal(new[] { "A", " ", "B" }, Rows(snapshot));
        Assert.Equal(2, snapshot.Rows);
        Assert.Equal(0, snapshot.CursorRow);
        Assert.Equal(0, snapshot.CursorCol);
    }

    [Fact]
    public void Scroll_up_caps_the_count_at_region_height_and_keeps_removed_rows_in_order()
    {
        var terminal = TerminalWithRows(1, 3, "A\nB\nC");
        ApplyStream(terminal, "\u001b[1;1H\u001b[65535S");

        Assert.Equal(new[] { "A", "B", "C", " ", " ", " " }, Rows(terminal.Snapshot(true)));
        Assert.Equal(new[] { " ", " ", " " }, Rows(terminal.Snapshot(false)));
    }

    [Fact]
    public void Scrolling_a_top_anchored_partial_region_records_only_its_removed_rows()
    {
        var terminal = TerminalWithRows(1, 4, "A\nB\nC\nD");
        ApplyStream(terminal, "\u001b[1;2r\u001b[2;1H\n");

        Snapshot snapshot = terminal.Snapshot(true);
        Assert.Equal(4, snapshot.Rows);
        Assert.Equal(new[] { "A", "B", " ", "C", "D" }, Rows(snapshot));
        Assert.Equal(1, snapshot.CursorRow);
        Assert.Equal(0, snapshot.CursorCol);
    }

    [Fact]
    public void Scrolling_a_lower_region_does_not_record_scrollback()
    {
        var terminal = TerminalWithRows(1, 4, "A\nB\nC\nD");
        ApplyStream(terminal, "\u001b[2;4r\u001b[4;1H\n");

        Snapshot snapshot = terminal.Snapshot(true);
        Assert.Equal(4, snapshot.Rows);
        Assert.Equal(new[] { "A", "C", "D", " " }, Rows(snapshot));
        Assert.Equal(3, snapshot.CursorRow);
        Assert.Equal(0, snapshot.CursorCol);
    }

    [Fact]
    public void Alternate_screen_snapshots_never_include_main_scrollback()
    {
        var terminal = TerminalWithRows(1, 2, "A\nB\nC");
        Assert.Equal(new[] { "A", "B", "C" }, Rows(terminal.Snapshot(true)));

        ApplyStream(terminal, "\u001b[?47hX");
        Assert.True(terminal.AltScreen);
        Assert.Equal(new[] { " ", "X" }, Rows(terminal.Snapshot(true)));

        ApplyStream(terminal, "\u001b[?47l");
        Assert.False(terminal.AltScreen);
        Assert.Equal(new[] { "A", "B", "C" }, Rows(terminal.Snapshot(true)));
    }

    [Fact]
    public void Snapshots_copy_screen_and_scrollback_rows_and_remain_detached_after_edits()
    {
        var terminal = TerminalWithRows(1, 2, "A\nB\nC");
        Snapshot original = terminal.Snapshot(true);
        Assert.Equal(new[] { "A", "B", "C" }, Rows(original));

        original.Lines[0][0] = new Cell('x', Style.Default);
        original.Lines[1][0] = new Cell('y', Style.Default);
        terminal.Apply(new CsiEvent(8, '\0', new[] { 1, 1 }, string.Empty, 'H', false, "<ESC>[1;1H"));
        terminal.Apply(new PrintEvent(9, 'Z'));

        Snapshot current = terminal.Snapshot(true);
        Assert.Equal(new[] { "A", "Z", "C" }, Rows(current));
        Assert.NotSame(original.Lines[0], current.Lines[0]);
        Assert.NotSame(original.Lines[1], current.Lines[1]);
        Assert.Equal(new[] { "x", "y", "C" }, Rows(original));
    }

    [Fact]
    public void Raw_ignored_sequence_and_invalid_sgr_parameters_are_reported_exactly()
    {
        var terminal = new Terminal(2, 1);

        terminal.Apply(new EscEvent(7, string.Empty, 'c', "RAW"));
        terminal.Apply(new CsiEvent(11, '\0', new[] { 38, 5, 300 }, string.Empty, 'm', false, "<ESC>[38;5;300m"));
        terminal.Apply(new CsiEvent(15, '\0', new[] { 38, 5, -1 }, string.Empty, 'm', false, "<ESC>[38;5;m"));

        Assert.Equal(
            new[]
            {
                new Finding(7, "VT001", "ignored sequence \"RAW\""),
                new Finding(11, "VT005", "invalid SGR parameters \"38;5;300\""),
                new Finding(15, "VT005", "invalid SGR parameters \"38;5;-1\"")
            },
            terminal.Findings);
    }

    private static Terminal TerminalWithRows(int cols, int rows, string stream)
    {
        var terminal = new Terminal(cols, rows);
        ApplyStream(terminal, stream);
        return terminal;
    }

    private static void ApplyStream(Terminal terminal, string stream)
    {
        var parser = new VtParser();
        foreach (VtEvent ev in parser.Feed(Text.CodePoints(stream)))
        {
            terminal.Apply(ev);
        }
    }

    private static Cell BackgroundBlank(int background) =>
        new(' ', new Style(Color.Default, new Color(ColorKind.Indexed, background), Attr.None));

    private static string Row(Snapshot snapshot, int row) =>
        string.Concat(snapshot.Lines[row].Select(cell => Text.FromCodePoint(cell.Char)));

    private static string[] Rows(Snapshot snapshot) =>
        snapshot.Lines.Select((_, row) => Row(snapshot, row)).ToArray();
}
