using System.Globalization;

namespace Vtsnap.Tests;

public sealed class TerminalTests
{
    [Fact]
    public void New_terminal_starts_with_a_blank_screen_and_default_state()
    {
        var terminal = new Terminal(1, 1);

        Assert.False(terminal.AltScreen);
        Assert.True(terminal.CursorVisible);
        Assert.Equal(Style.Default, terminal.CurrentStyle);
        Assert.True(terminal.RegionIsFull);
        Assert.Empty(terminal.Findings);

        Snapshot snapshot = terminal.Snapshot(withScrollback: true);
        Assert.Equal(1, snapshot.Cols);
        Assert.Equal(1, snapshot.Rows);
        Assert.Equal(string.Empty, snapshot.Title);
        Assert.Equal(0, snapshot.CursorRow);
        Assert.Equal(0, snapshot.CursorCol);
        Assert.True(snapshot.CursorVisible);
        Assert.Equal(new[] { Cell.Blank }, snapshot.Lines[0]);
    }

    [Fact]
    public void Terminal_accepts_the_maximum_supported_dimensions()
    {
        var terminal = new Terminal(500, 500);
        Snapshot snapshot = terminal.Snapshot(withScrollback: false);

        Assert.Equal(500, snapshot.Cols);
        Assert.Equal(500, snapshot.Rows);
        Assert.Equal(500, snapshot.Lines.Count);
        Assert.Equal(500, snapshot.Lines[0].Length);
        Assert.Equal(Cell.Blank, snapshot.Lines[499][499]);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(501, 1)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 501)]
    public void Terminal_rejects_dimensions_outside_the_supported_range(int cols, int rows)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Terminal(cols, rows));
    }

    [Fact]
    public void Every_printable_code_point_uses_one_cell_including_c1_combining_and_supplementary_values()
    {
        var terminal = new Terminal(4, 1);

        Apply(terminal, "A\u0301\u0085\U0001F600");

        Assert.Equal(new[] { 'A', 0x0301, 0x0085, 0x1F600 }, terminal.Snapshot(false).Lines[0].Select(cell => cell.Char));
        Assert.Equal(0, terminal.Snapshot(false).CursorRow);
        Assert.Equal(3, terminal.Snapshot(false).CursorCol);
        Assert.Empty(terminal.Findings);
    }

    [Fact]
    public void Printing_at_the_last_column_defers_wrap_until_the_next_print()
    {
        var terminal = new Terminal(3, 2);

        Apply(terminal, "ABC");

        Assert.Equal("ABC", Row(terminal.Snapshot(false), 0));
        Assert.Equal("   ", Row(terminal.Snapshot(false), 1));
        Assert.Equal(0, terminal.Snapshot(false).CursorRow);
        Assert.Equal(2, terminal.Snapshot(false).CursorCol);

        Apply(terminal, "D");

        Assert.Equal("ABC", Row(terminal.Snapshot(false), 0));
        Assert.Equal("D  ", Row(terminal.Snapshot(false), 1));
        Assert.Equal(1, terminal.Snapshot(false).CursorRow);
        Assert.Equal(1, terminal.Snapshot(false).CursorCol);
    }

    [Fact]
    public void Disabling_autowrap_while_wrap_is_pending_overwrites_the_last_column()
    {
        var terminal = new Terminal(3, 2);

        Apply(terminal, "ABC\u001b[?7lD");

        Assert.Equal("ABD", Row(terminal.Snapshot(false), 0));
        Assert.Equal("   ", Row(terminal.Snapshot(false), 1));
        Assert.Equal(0, terminal.Snapshot(false).CursorRow);
        Assert.Equal(2, terminal.Snapshot(false).CursorCol);
        Assert.Empty(terminal.Findings);
    }

    [Fact]
    public void Backspace_stops_at_the_first_column_and_clears_pending_wrap()
    {
        var terminal = new Terminal(3, 1);

        Apply(terminal, "ABC");
        terminal.Apply(new ControlEvent(3, 0x08));
        terminal.Apply(new PrintEvent(4, 'D'));

        Assert.Equal("ADC", Row(terminal.Snapshot(false), 0));
        Assert.Equal(2, terminal.Snapshot(false).CursorCol);

        terminal.Apply(new CsiEvent(5, '\0', new[] { 1, 1 }, string.Empty, 'H', false, "<ESC>[1;1H"));
        terminal.Apply(new ControlEvent(6, 0x08));
        Assert.Equal(0, terminal.Snapshot(false).CursorCol);
    }

    [Fact]
    public void Horizontal_tab_moves_to_the_next_eight_column_stop()
    {
        var terminal = new Terminal(20, 1);

        Apply(terminal, "\u0009X");

        Assert.Equal('X', terminal.Snapshot(false).Lines[0][8].Char);
        Assert.Equal(9, terminal.Snapshot(false).CursorCol);
    }

    [Fact]
    public void Horizontal_tab_clamps_to_the_last_column()
    {
        var terminal = new Terminal(10, 1);

        Apply(terminal, "\u001b[1;10H\u0009X");

        Assert.Equal('X', terminal.Snapshot(false).Lines[0][9].Char);
        Assert.Equal(9, terminal.Snapshot(false).CursorCol);
    }

    [Theory]
    [InlineData(0x0A)]
    [InlineData(0x0B)]
    [InlineData(0x0C)]
    public void Line_feed_vertical_tab_and_form_feed_home_the_column_and_index(int control)
    {
        var terminal = new Terminal(5, 3);
        Apply(terminal, "\u001b[1;4H");
        terminal.Apply(new ControlEvent(4, control));

        Assert.Equal(1, terminal.Snapshot(false).CursorRow);
        Assert.Equal(0, terminal.Snapshot(false).CursorCol);
    }

    [Fact]
    public void Carriage_return_homes_the_column_without_moving_rows()
    {
        var terminal = new Terminal(5, 3);

        Apply(terminal, "\u001b[2;4H\r");

        Assert.Equal(1, terminal.Snapshot(false).CursorRow);
        Assert.Equal(0, terminal.Snapshot(false).CursorCol);
    }

    [Fact]
    public void Other_control_events_do_nothing_without_findings()
    {
        var terminal = new Terminal(5, 2);
        Apply(terminal, "\u001b[2;3H");
        Snapshot before = terminal.Snapshot(false);

        terminal.Apply(new ControlEvent(7, 0x01));

        Assert.Equal(before.CursorRow, terminal.Snapshot(false).CursorRow);
        Assert.Equal(before.CursorCol, terminal.Snapshot(false).CursorCol);
        Assert.Equal(before.Lines, terminal.Snapshot(false).Lines);
        Assert.Empty(terminal.Findings);
    }

    [Fact]
    public void Index_at_the_bottom_of_the_region_scrolls_up_and_records_main_scrollback()
    {
        var terminal = new Terminal(1, 3);
        Apply(terminal, "A\u001b[2;1HB\u001b[3;1HC\u001bD");

        Snapshot snapshot = terminal.Snapshot(withScrollback: true);
        Assert.Equal(3, snapshot.Rows);
        Assert.Equal(new[] { "A", "B", "C", " " }, Rows(snapshot));
        Assert.Equal(2, snapshot.CursorRow);
        Assert.Equal(0, snapshot.CursorCol);
    }

    [Fact]
    public void Reverse_index_at_the_top_of_the_region_scrolls_down_without_scrollback()
    {
        var terminal = new Terminal(1, 3);
        Apply(terminal, "A\u001b[2;1HB\u001b[3;1HC\u001b[1;1H\u001bM");

        Snapshot snapshot = terminal.Snapshot(withScrollback: true);
        Assert.Equal(new[] { " ", "A", "B" }, Rows(snapshot));
        Assert.Equal(0, snapshot.CursorRow);
        Assert.Empty(terminal.Findings);
    }

    [Fact]
    public void Escape_E_homes_the_column_and_indexes()
    {
        var terminal = new Terminal(5, 3);

        Apply(terminal, "\u001b[2;4H\u001bE");

        Assert.Equal(2, terminal.Snapshot(false).CursorRow);
        Assert.Equal(0, terminal.Snapshot(false).CursorCol);
    }

    [Fact]
    public void Supported_escape_no_ops_do_not_create_findings()
    {
        var terminal = new Terminal(3, 2);

        terminal.Apply(new EscEvent(0, string.Empty, '=', "<ESC>="));
        terminal.Apply(new EscEvent(1, string.Empty, '>', "<ESC>>"));
        terminal.Apply(new EscEvent(2, "(", 'B', "<ESC>(B"));
        terminal.Apply(new EscEvent(3, ")", '0', "<ESC>)0"));
        terminal.Apply(new EscEvent(4, "*", '0', "<ESC>*0"));
        terminal.Apply(new EscEvent(5, "+", '0', "<ESC>+0"));

        Assert.Empty(terminal.Findings);
        Assert.False(terminal.AltScreen);
        Assert.True(terminal.RegionIsFull);
    }

    [Fact]
    public void Unsupported_escape_and_control_string_events_are_reported_exactly()
    {
        var terminal = new Terminal(2, 1);

        terminal.Apply(new EscEvent(12, string.Empty, 'c', "<ESC>c"));
        terminal.Apply(new StringEvent(18, "<ESC>Pdata<ESC>"));
        terminal.Apply(new EscEvent(19, string.Empty, 'c', "RAW"));

        Assert.Equal(
            new[]
            {
                new Finding(12, "VT001", "ignored sequence \"<ESC>c\""),
                new Finding(18, "VT001", "ignored sequence \"<ESC>Pdata<ESC>\""),
                new Finding(19, "VT001", "ignored sequence \"RAW\"")
            },
            terminal.Findings);
    }

    [Fact]
    public void Unterminated_events_are_ignored()
    {
        var terminal = new Terminal(2, 1);

        terminal.Apply(new UnterminatedEvent(7, "esc"));
        terminal.Apply(new UnterminatedEvent(9, "osc"));
        terminal.Apply(new UnterminatedEvent(12, "str"));

        Assert.Empty(terminal.Findings);
    }

    [Fact]
    public void Relative_cursor_motion_uses_one_for_missing_and_zero_parameters_and_clamps()
    {
        var terminal = new Terminal(5, 4);
        terminal.Apply(new CsiEvent(0, '\0', Array.Empty<int>(), string.Empty, 'B', false, "<ESC>[B"));
        terminal.Apply(new CsiEvent(1, '\0', new[] { 0 }, string.Empty, 'C', false, "<ESC>[0C"));
        terminal.Apply(new CsiEvent(2, '\0', new[] { 65535 }, string.Empty, 'B', false, "<ESC>[65535B"));
        terminal.Apply(new CsiEvent(3, '\0', new[] { 65535 }, string.Empty, 'C', false, "<ESC>[65535C"));
        terminal.Apply(new CsiEvent(4, '\0', new[] { 65535 }, string.Empty, 'A', false, "<ESC>[65535A"));
        terminal.Apply(new CsiEvent(5, '\0', new[] { 65535 }, string.Empty, 'D', false, "<ESC>[65535D"));

        Assert.Equal(0, terminal.Snapshot(false).CursorRow);
        Assert.Equal(0, terminal.Snapshot(false).CursorCol);
        Assert.Empty(terminal.Findings);
    }

    [Fact]
    public void Absolute_cursor_motion_defaults_zero_values_and_clamps_with_invariant_diagnostics()
    {
        var terminal = new Terminal(80, 24);

        terminal.Apply(new CsiEvent(7, '\0', new[] { 40, 1 }, string.Empty, 'H', false, "<ESC>[40;1H"));
        terminal.Apply(new CsiEvent(8, '\0', new[] { 90 }, string.Empty, 'G', false, "<ESC>[90G"));
        terminal.Apply(new CsiEvent(9, '\0', new[] { 30 }, string.Empty, 'd', false, "<ESC>[30d"));

        Assert.Equal(23, terminal.Snapshot(false).CursorRow);
        Assert.Equal(79, terminal.Snapshot(false).CursorCol);
        Assert.Equal(
            new[]
            {
                new Finding(7, "VT004", "position 40;1 is outside the screen (24 rows, 80 columns)"),
                new Finding(8, "VT004", "column 90 is outside the screen (80 columns)"),
                new Finding(9, "VT004", "row 30 is outside the screen (24 rows)")
            },
            terminal.Findings);
    }

    [Fact]
    public void Absolute_cursor_findings_use_singular_dimension_names()
    {
        var terminal = new Terminal(1, 1);

        terminal.Apply(new CsiEvent(0, '\0', new[] { 2, 2 }, string.Empty, 'f', false, "<ESC>[2;2f"));
        terminal.Apply(new CsiEvent(1, '\0', new[] { 2 }, string.Empty, 'G', false, "<ESC>[2G"));
        terminal.Apply(new CsiEvent(2, '\0', new[] { 2 }, string.Empty, 'd', false, "<ESC>[2d"));

        Assert.Equal(
            new[]
            {
                new Finding(0, "VT004", "position 2;2 is outside the screen (1 row, 1 column)"),
                new Finding(1, "VT004", "column 2 is outside the screen (1 column)"),
                new Finding(2, "VT004", "row 2 is outside the screen (1 row)")
            },
            terminal.Findings);
    }

    [Fact]
    public void Absolute_cursor_defaults_to_first_row_and_column()
    {
        var terminal = new Terminal(4, 3);

        terminal.Apply(new CsiEvent(0, '\0', Array.Empty<int>(), string.Empty, 'H', false, "<ESC>[H"));
        terminal.Apply(new CsiEvent(1, '\0', new[] { 0, -1 }, string.Empty, 'f', false, "<ESC>[0;-1f"));

        Assert.Equal(0, terminal.Snapshot(false).CursorRow);
        Assert.Equal(0, terminal.Snapshot(false).CursorCol);
        Assert.Empty(terminal.Findings);
    }

    [Fact]
    public void Unsupported_csi_markers_malformed_sequences_and_intermediates_are_ignored()
    {
        var terminal = new Terminal(2, 1);

        terminal.Apply(new CsiEvent(1, '>', Array.Empty<int>(), string.Empty, 'c', false, "<ESC>[>c"));
        terminal.Apply(new CsiEvent(2, '\0', Array.Empty<int>(), " ", 'A', false, "<ESC>[ A"));
        terminal.Apply(new CsiEvent(3, '\0', new[] { 1 }, string.Empty, 'A', true, "<ESC>[1:A"));
        terminal.Apply(new CsiEvent(4, '?', new[] { 7 }, string.Empty, 'h', true, "<ESC>[??7h"));

        Assert.Equal(
            new[]
            {
                new Finding(1, "VT001", "ignored sequence \"<ESC>[>c\""),
                new Finding(2, "VT001", "ignored sequence \"<ESC>[ A\""),
                new Finding(3, "VT001", "ignored sequence \"<ESC>[1:A\""),
                new Finding(4, "VT001", "ignored sequence \"<ESC>[??7h\"")
            },
            terminal.Findings);
    }

    [Fact]
    public void Sgr_updates_current_style_and_invalid_parameters_raise_only_vt005()
    {
        var terminal = new Terminal(2, 1);
        CultureInfo originalCulture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            terminal.Apply(new CsiEvent(12, '\0', new[] { 1, 38, 5, 300, 0 }, string.Empty, 'm', false, "<ESC>[1;38;5;300;0m"));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }

        Assert.Equal(new Style(Color.Default, Color.Default, Attr.Bold), terminal.CurrentStyle);
        Assert.Equal(
            new[] { new Finding(12, "VT005", "invalid SGR parameters \"1;38;5;300;0\"") },
            terminal.Findings);
    }

    [Fact]
    public void Escape_save_and_restore_share_cursor_and_style_state()
    {
        var terminal = new Terminal(5, 3);

        Apply(terminal, "\u001b[2;3H\u001b[31m\u001b7\u001b[1;1H\u001b[34m\u001b8X");

        Snapshot snapshot = terminal.Snapshot(false);
        Assert.Equal(1, snapshot.CursorRow);
        Assert.Equal(3, snapshot.CursorCol);
        Assert.Equal(new Color(ColorKind.Indexed, 1), terminal.CurrentStyle.Fg);
        Assert.Equal(new Cell('X', terminal.CurrentStyle), snapshot.Lines[1][2]);
        Assert.Empty(terminal.Findings);
    }

    [Fact]
    public void Csi_save_and_restore_use_the_same_slot_as_escape_save_and_restore()
    {
        var terminal = new Terminal(5, 3);

        Apply(terminal, "\u001b[2;4H\u001b[32m\u001b[s\u001b[1;1H\u001b[35m\u001b8X");

        Assert.Equal(1, terminal.Snapshot(false).CursorRow);
        Assert.Equal(4, terminal.Snapshot(false).CursorCol);
        Assert.Equal(new Color(ColorKind.Indexed, 2), terminal.CurrentStyle.Fg);
        Assert.Equal(new Cell('X', terminal.CurrentStyle), terminal.Snapshot(false).Lines[1][3]);
        Assert.Empty(terminal.Findings);
    }

    [Fact]
    public void Parameterized_csi_save_and_restore_are_ignored()
    {
        var terminal = new Terminal(3, 2);

        terminal.Apply(new CsiEvent(5, '\0', new[] { 1 }, string.Empty, 's', false, "<ESC>[1s"));
        terminal.Apply(new CsiEvent(8, '\0', new[] { 1 }, string.Empty, 'u', false, "<ESC>[1u"));

        Assert.Equal(
            new[]
            {
                new Finding(5, "VT001", "ignored sequence \"<ESC>[1s\""),
                new Finding(8, "VT001", "ignored sequence \"<ESC>[1u\"")
            },
            terminal.Findings);
    }

    [Fact]
    public void Valid_scroll_region_caps_the_bottom_homes_cursor_and_can_be_reset()
    {
        var terminal = new Terminal(5, 4);
        Apply(terminal, "\u001b[3;4H\u001b[2;65535r");

        Assert.False(terminal.RegionIsFull);
        Assert.Equal(0, terminal.Snapshot(false).CursorRow);
        Assert.Equal(0, terminal.Snapshot(false).CursorCol);

        Apply(terminal, "\u001b[r");

        Assert.True(terminal.RegionIsFull);
        Assert.Equal(0, terminal.Snapshot(false).CursorRow);
        Assert.Equal(0, terminal.Snapshot(false).CursorCol);
        Assert.Empty(terminal.Findings);
    }

    [Fact]
    public void Invalid_scroll_regions_leave_region_and_cursor_unchanged_without_findings()
    {
        var terminal = new Terminal(5, 4);
        Apply(terminal, "\u001b[3;2H");
        Snapshot before = terminal.Snapshot(false);

        Apply(terminal, "\u001b[3;3r");
        Apply(terminal, "\u001b[5;2r");

        Assert.True(terminal.RegionIsFull);
        Assert.Equal(before.CursorRow, terminal.Snapshot(false).CursorRow);
        Assert.Equal(before.CursorCol, terminal.Snapshot(false).CursorCol);
        Assert.Empty(terminal.Findings);
    }

    [Fact]
    public void Private_visibility_and_autowrap_modes_are_global_and_known_no_ops_are_silent()
    {
        var terminal = new Terminal(3, 2);

        Apply(terminal, "\u001b[?25l\u001b[?7l\u001b[?1;12;1000;1002;1003;1004;1006;2004h");

        Assert.False(terminal.CursorVisible);
        Assert.Empty(terminal.Findings);
        Apply(terminal, "\u001b[?25h\u001b[?7h");
        Assert.True(terminal.CursorVisible);
    }

    [Fact]
    public void Unknown_private_mode_is_reported_once_while_known_modes_still_apply()
    {
        var terminal = new Terminal(3, 2);

        terminal.Apply(new CsiEvent(14, '?', new[] { 7, 999, 25 }, string.Empty, 'l', false, "<ESC>[?7;999;25l"));

        Assert.False(terminal.CursorVisible);
        Assert.DoesNotContain(terminal.Findings, finding => finding.Code != "VT001");
        Assert.Equal(
            new[] { new Finding(14, "VT001", "ignored sequence \"<ESC>[?7;999;25l\"") },
            terminal.Findings);
    }

    [Fact]
    public void Alternate_screen_is_blank_and_restores_the_shared_saved_cursor_and_style()
    {
        var terminal = new Terminal(4, 2);
        Apply(terminal, "main\u001b[2;3H\u001b[31m\u001b[?1049h");

        Assert.True(terminal.AltScreen);
        Assert.Equal(new[] { "    ", "    " }, Rows(terminal.Snapshot(true)));
        Assert.Equal(1, terminal.Snapshot(false).CursorRow);
        Assert.Equal(2, terminal.Snapshot(false).CursorCol);

        Apply(terminal, "\u001b[32mALT\u001b[?1049lX");

        Snapshot main = terminal.Snapshot(true);
        Assert.False(terminal.AltScreen);
        Assert.Equal("main", Row(main, 0));
        Assert.Equal(new Cell('X', terminal.CurrentStyle), main.Lines[1][2]);
        Assert.Equal(new Color(ColorKind.Indexed, 1), terminal.CurrentStyle.Fg);
        Assert.Equal(1, main.CursorRow);
        Assert.Equal(3, main.CursorCol);
        Assert.Empty(terminal.Findings);
    }

    [Fact]
    public void Repeated_alternate_screen_settings_do_not_resave_clear_or_restore()
    {
        var terminal = new Terminal(4, 2);
        Apply(terminal, "main\u001b[2;3H\u001b[31m\u001b[?47h");

        Apply(terminal, "alt\u001b[1;1H\u001b[32m\u001b[?1047h");
        Assert.Equal("  al", Row(terminal.Snapshot(false), 0));
        Assert.Equal("t   ", Row(terminal.Snapshot(false), 1));

        Apply(terminal, "\u001b[?1049l");
        Assert.Equal("main", Row(terminal.Snapshot(false), 0));
        Assert.Equal(new Color(ColorKind.Indexed, 1), terminal.CurrentStyle.Fg);
        Assert.Equal(1, terminal.Snapshot(false).CursorRow);
        Assert.Equal(2, terminal.Snapshot(false).CursorCol);

        Apply(terminal, "\u001b[?1049l");
        Assert.Equal("main", Row(terminal.Snapshot(false), 0));
        Assert.Equal(1, terminal.Snapshot(false).CursorRow);
        Assert.Equal(2, terminal.Snapshot(false).CursorCol);
    }

    [Fact]
    public void Title_is_global_across_alternate_screen_transitions_and_accepts_empty_text()
    {
        var terminal = new Terminal(3, 2);
        Apply(terminal, "\u001b]0;first;with;semicolons\u0007\u001b[?47h\u001b]2;\u0007");

        Assert.Equal(string.Empty, terminal.Snapshot(false).Title);
        Apply(terminal, "\u001b[?47l");

        Assert.Equal(string.Empty, terminal.Snapshot(false).Title);
        Assert.Empty(terminal.Findings);
    }

    [Fact]
    public void Unsupported_osc_and_string_events_produce_findings_in_arising_order()
    {
        var terminal = new Terminal(3, 1);

        terminal.Apply(new OscEvent(20, "1;ignored", "<ESC>]1;ignored"));
        terminal.Apply(new OscEvent(21, "bad", "<ESC>]bad"));
        terminal.Apply(new StringEvent(22, "<ESC>^data<ESC>"));

        Assert.Equal(
            new[]
            {
                new Finding(20, "VT001", "ignored sequence \"<ESC>]1;ignored\""),
                new Finding(21, "VT001", "ignored sequence \"<ESC>]bad\""),
                new Finding(22, "VT001", "ignored sequence \"<ESC>^data<ESC>\"")
            },
            terminal.Findings);
    }

    [Fact]
    public void Findings_keep_arising_order_even_when_event_positions_are_out_of_order()
    {
        var terminal = new Terminal(2, 1);

        terminal.Apply(new EscEvent(8, string.Empty, 'c', "<ESC>c"));
        terminal.Apply(new EscEvent(2, string.Empty, 'c', "<ESC>c"));

        Assert.Equal(new[] { 8, 2 }, terminal.Findings.Select(finding => finding.Pos));
    }

    [Theory]
    [InlineData("\b", 0, 1)]
    [InlineData("\t", 0, 2)]
    [InlineData("\n", 1, 0)]
    [InlineData("\u000b", 1, 0)]
    [InlineData("\u000c", 1, 0)]
    [InlineData("\r", 0, 0)]
    [InlineData("\u001bD", 1, 2)]
    [InlineData("\u001bE", 1, 0)]
    [InlineData("\u001bM", 0, 2)]
    [InlineData("\u001b8", 0, 0)]
    [InlineData("\u001b[A", 0, 2)]
    [InlineData("\u001b[B", 1, 2)]
    [InlineData("\u001b[C", 0, 2)]
    [InlineData("\u001b[D", 0, 1)]
    [InlineData("\u001b[1G", 0, 0)]
    [InlineData("\u001b[1d", 0, 2)]
    [InlineData("\u001b[1;1H", 0, 0)]
    [InlineData("\u001b[1;1f", 0, 0)]
    [InlineData("\u001b[u", 0, 0)]
    [InlineData("\u001b[1;1L", 0, 0)]
    [InlineData("\u001b[1;1M", 0, 0)]
    [InlineData("\u001b[1;2r", 0, 0)]
    [InlineData("\u001b[?47h", 0, 2)]
    [InlineData("\u001b[?1047h", 0, 2)]
    [InlineData("\u001b[?1049h", 0, 2)]
    [InlineData("\u001b[?47l", 0, 2)]
    [InlineData("\u001b[?1047l", 0, 2)]
    [InlineData("\u001b[?1049l", 0, 2)]
    public void Specified_cursor_and_screen_events_clear_pending_wrap(string sequence, int expectedRow, int expectedCol)
    {
        var terminal = new Terminal(3, 2);
        Apply(terminal, "ABC");
        Apply(terminal, sequence);
        Apply(terminal, "X");

        Assert.Equal('X', terminal.Snapshot(false).Lines[expectedRow][expectedCol].Char);
    }

    [Theory]
    [InlineData("\u001b[0J")]
    [InlineData("\u001b[0K")]
    [InlineData("\u001b[S")]
    [InlineData("\u001b[T")]
    [InlineData("\u001b7")]
    [InlineData("\u001b[s")]
    [InlineData("\u001b[31m")]
    [InlineData("\u001b]2;title\u0007")]
    [InlineData("\u001b[?25l")]
    [InlineData("\u001b[5n")]
    [InlineData("\u001b[1u")]
    [InlineData("\u001b[?7l\u001b[?7h")]
    public void Unlisted_events_preserve_pending_wrap(string sequence)
    {
        var terminal = new Terminal(3, 2);
        Apply(terminal, "ABC");
        Apply(terminal, sequence);
        Apply(terminal, "X");

        Assert.Equal('X', terminal.Snapshot(false).Lines[1][0].Char);
    }

    [Fact]
    public void Valid_in_region_row_edits_clear_wrap_but_outside_edits_preserve_it()
    {
        var valid = new Terminal(3, 2);
        Apply(valid, "ABC\u001b[1;2L");
        Apply(valid, "X");

        Assert.Equal('X', valid.Snapshot(false).Lines[0][0].Char);

        var outside = new Terminal(3, 3);
        Apply(outside, "\u001b[2;3r\u001b[1;1HABC\u001b[LX");

        Assert.Equal('X', outside.Snapshot(false).Lines[1][0].Char);
    }

    [Fact]
    public void The_exact_edit_log_stream_has_the_expected_screen_and_end_state()
    {
        var terminal = new Terminal(10, 5);
        Apply(
            terminal,
            "0123456789\r\nabcdef\u001b[2;1H\u001b[L\u001b[44m\u001b[K\u001b[0m\u001b[3;4r\u001b[4;1H\n"
                + "X\u001b[?1049hALT\u001b[?1049lY\u001b7\u001b[1;1HZ\u001b8!");

        Snapshot snapshot = terminal.Snapshot(withScrollback: false);

        Assert.Equal(new[] { "Z123456789", "          ", "          ", "XY!       ", "          " }, Rows(snapshot));
        Assert.False(terminal.AltScreen);
        Assert.True(terminal.CursorVisible);
        Assert.Equal(Style.Default, terminal.CurrentStyle);
        Assert.False(terminal.RegionIsFull);
        Assert.Empty(terminal.Findings);
    }

    private static void Apply(Terminal terminal, string stream)
    {
        var parser = new VtParser();
        foreach (VtEvent ev in parser.Feed(Text.CodePoints(stream)))
        {
            terminal.Apply(ev);
        }
    }

    private static string Row(Snapshot snapshot, int row) =>
        string.Concat(snapshot.Lines[row].Select(cell => Text.FromCodePoint(cell.Char)));

    private static string[] Rows(Snapshot snapshot) =>
        snapshot.Lines.Select((_, row) => Row(snapshot, row)).ToArray();
}
