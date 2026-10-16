namespace Vtsnap.Tests;

public sealed class VtParserTests
{
    [Fact]
    public void Empty_feed_does_not_advance_the_global_position()
    {
        var parser = new VtParser();

        Assert.Empty(parser.Feed(Array.Empty<int>()));
        Assert.Empty(parser.Feed(Array.Empty<int>()));
        Assert.Equal(new PrintEvent(0, 'A'), Assert.Single(parser.Feed(Text.CodePoints("A"))));
    }

    [Fact]
    public void Ground_emits_prints_and_controls_drops_del_and_keeps_c1_and_supplementary_scalars()
    {
        var parser = new VtParser();

        List<VtEvent> events = parser.Feed(Text.CodePoints("A\n\u007f\u0085\U0001F600"));

        Assert.Equal(4, events.Count);
        Assert.Equal(new PrintEvent(0, 'A'), events[0]);
        Assert.Equal(new ControlEvent(1, '\n'), events[1]);
        Assert.Equal(new PrintEvent(3, 0x85), events[2]);
        Assert.Equal(new PrintEvent(4, 0x1F600), events[3]);
    }

    [Theory]
    [InlineData("\u0018", 0x18)]
    [InlineData("\u001a", 0x1A)]
    public void Ground_emits_cancel_characters_as_controls(string input, int codePoint)
    {
        Assert.Equal(
            new ControlEvent(0, codePoint),
            Assert.Single(new VtParser().Feed(Text.CodePoints(input))));
    }

    [Fact]
    public void Escape_event_preserves_its_start_position_final_and_raw_spelling()
    {
        VtEvent actual = Assert.Single(new VtParser().Feed(Text.CodePoints("\u001b7")));

        AssertEsc(actual, 0, "", '7', "<ESC>7");
    }

    [Fact]
    public void Escape_event_preserves_intermediates()
    {
        VtEvent actual = Assert.Single(new VtParser().Feed(Text.CodePoints("\u001b(B")));

        AssertEsc(actual, 0, "(", 'B', "<ESC>(B");
    }

    [Fact]
    public void A_bracket_after_an_escape_intermediate_is_an_escape_final_not_a_csi_introducer()
    {
        VtEvent actual = Assert.Single(new VtParser().Feed(Text.CodePoints("\u001b([")));

        AssertEsc(actual, 0, "(", '[', "<ESC>([");
    }

    [Theory]
    [InlineData("\u001bPpayload\u001b\\", "<ESC>P")]
    [InlineData("\u001bXpayload\u001b\\", "<ESC>X")]
    [InlineData("\u001b^payload\u001b\\", "<ESC>^")]
    [InlineData("\u001b_payload\u001b\\", "<ESC>_")]
    public void Each_control_string_introducer_emits_its_string_event_on_st(string input, string raw)
    {
        VtEvent actual = Assert.Single(new VtParser().Feed(Text.CodePoints(input)));

        AssertString(actual, 0, raw);
    }

    [Fact]
    public void Csi_without_marker_or_parameter_text_has_empty_values()
    {
        VtEvent actual = Assert.Single(new VtParser().Feed(Text.CodePoints("\u001b[m")));

        AssertCsi(actual, 0, '\0', Array.Empty<int>(), "", 'm', false, "<ESC>[m");
    }

    [Theory]
    [InlineData("\u001b[<m", '<', "<ESC>[<m")]
    [InlineData("\u001b[=m", '=', "<ESC>[=m")]
    [InlineData("\u001b[>m", '>', "<ESC>[>m")]
    [InlineData("\u001b[?m", '?', "<ESC>[?m")]
    public void Csi_accepts_each_marker_only_immediately_after_the_introducer(
        string input,
        char marker,
        string raw)
    {
        VtEvent actual = Assert.Single(new VtParser().Feed(Text.CodePoints(input)));

        AssertCsi(actual, 0, marker, Array.Empty<int>(), "", 'm', false, raw);
    }

    [Fact]
    public void Csi_splits_empty_parameters_and_saturates_each_decimal_value()
    {
        VtEvent actual = Assert.Single(new VtParser().Feed(Text.CodePoints("\u001b[?;0;;65536m")));

        AssertCsi(actual, 0, '?', new[] { -1, 0, -1, 65535 }, "", 'm', false, "<ESC>[?;0;;65536m");
    }

    [Fact]
    public void Csi_keeps_leading_zero_spelling_while_parsing_the_numeric_value()
    {
        VtEvent actual = Assert.Single(new VtParser().Feed(Text.CodePoints("\u001b[00031m")));

        AssertCsi(actual, 0, '\0', new[] { 31 }, "", 'm', false, "<ESC>[00031m");
    }

    [Fact]
    public void Csi_saturates_values_at_65535_without_overflow_for_long_fields()
    {
        VtEvent actual = Assert.Single(new VtParser().Feed(
            Text.CodePoints("\u001b[65535;65536;999999999999999999999999999999999999999999m")));

        AssertCsi(
            actual,
            0,
            '\0',
            new[] { 65535, 65535, 65535 },
            "",
            'm',
            false,
            "<ESC>[65535;65536;999999999999999999999999999999999999999999m");
    }

    [Fact]
    public void Csi_colon_marks_the_sequence_malformed_but_parameters_and_raw_continue()
    {
        VtEvent actual = Assert.Single(new VtParser().Feed(Text.CodePoints("\u001b[1;:;2m")));

        AssertCsi(actual, 0, '\0', new[] { 1, -1, 2 }, "", 'm', true, "<ESC>[1;:;2m");
    }

    [Fact]
    public void A_marker_after_parameter_text_is_malformed_and_not_a_parameter()
    {
        VtEvent actual = Assert.Single(new VtParser().Feed(Text.CodePoints("\u001b[1?2m")));

        AssertCsi(actual, 0, '\0', new[] { 12 }, "", 'm', true, "<ESC>[1?2m");
    }

    [Fact]
    public void Parameters_after_intermediates_are_malformed_and_intermediates_are_retained()
    {
        VtEvent actual = Assert.Single(new VtParser().Feed(Text.CodePoints("\u001b[1 $2m")));

        AssertCsi(actual, 0, '\0', new[] { 12 }, " $", 'm', true, "<ESC>[1 $2m");
    }

    [Fact]
    public void Escape_controls_are_emitted_but_not_added_to_raw_and_del_is_dropped()
    {
        var parser = new VtParser();

        List<VtEvent> events = parser.Feed(Text.CodePoints("\u001b\u00017\u007f"));

        Assert.Equal(2, events.Count);
        Assert.Equal(new ControlEvent(1, 0x01), events[0]);
        AssertEsc(events[1], 0, "", '7', "<ESC>7");
    }

    [Fact]
    public void Csi_controls_are_emitted_del_is_dropped_and_raw_keeps_the_remaining_digits()
    {
        var parser = new VtParser();

        List<VtEvent> events = parser.Feed(Text.CodePoints("\u001b[1\n2\u007fm"));

        Assert.Equal(2, events.Count);
        Assert.Equal(new ControlEvent(3, '\n'), events[0]);
        AssertCsi(events[1], 0, '\0', new[] { 12 }, "", 'm', false, "<ESC>[12m");
    }

    [Fact]
    public void Escape_cancelled_by_c1_reprocesses_that_scalar_in_ground()
    {
        List<VtEvent> events = new VtParser().Feed(Text.CodePoints("\u001b\u0085X"));

        Assert.Equal(2, events.Count);
        Assert.Equal(new PrintEvent(1, 0x85), events[0]);
        Assert.Equal(new PrintEvent(2, 'X'), events[1]);
    }

    [Fact]
    public void Csi_cancelled_by_supplementary_scalar_reprocesses_it_in_ground()
    {
        List<VtEvent> events = new VtParser().Feed(Text.CodePoints("\u001b[31\U0001F600"));

        Assert.Equal(new PrintEvent(4, 0x1F600), Assert.Single(events));
    }

    [Fact]
    public void Split_csi_emits_nothing_until_its_final_and_keeps_the_original_position()
    {
        var parser = new VtParser();

        Assert.Empty(parser.Feed(Text.CodePoints("\u001b")));
        Assert.Empty(parser.Feed(Text.CodePoints("[3")));
        VtEvent actual = Assert.Single(parser.Feed(Text.CodePoints("1m")));

        AssertCsi(actual, 0, '\0', new[] { 31 }, "", 'm', false, "<ESC>[31m");
    }

    [Fact]
    public void Split_escape_keeps_intermediates_across_feed_calls()
    {
        var parser = new VtParser();

        Assert.Empty(parser.Feed(Text.CodePoints("\u001b(")));
        VtEvent actual = Assert.Single(parser.Feed(Text.CodePoints("B")));

        AssertEsc(actual, 0, "(", 'B', "<ESC>(B");
    }

    [Fact]
    public void Restarting_with_a_new_escape_discards_the_old_sequence_and_uses_the_new_position()
    {
        List<VtEvent> events = new VtParser().Feed(Text.CodePoints("x\u001b[31\u001b7"));

        Assert.Equal(2, events.Count);
        Assert.Equal(new PrintEvent(0, 'x'), events[0]);
        AssertEsc(events[1], 5, "", '7', "<ESC>7");
    }

    [Theory]
    [InlineData("\u001b", "\u0018", 2)]
    [InlineData("\u001b[", "\u0018", 3)]
    [InlineData("\u001b]", "\u0018", 3)]
    [InlineData("\u001bP", "\u0018", 3)]
    [InlineData("\u001b", "\u001a", 2)]
    [InlineData("\u001b[", "\u001a", 3)]
    [InlineData("\u001b]", "\u001a", 3)]
    [InlineData("\u001bP", "\u001a", 3)]
    public void Can_and_sub_cancel_every_sequence_kind_without_a_sequence_event(
        string sequencePrefix,
        string cancellation,
        int followingPosition)
    {
        var parser = new VtParser();

        Assert.Empty(parser.Feed(Text.CodePoints(sequencePrefix)));
        Assert.Empty(parser.Feed(Text.CodePoints(cancellation)));
        Assert.Equal(
            new PrintEvent(followingPosition, 'Z'),
            Assert.Single(parser.Feed(Text.CodePoints("Z"))));
    }

    [Fact]
    public void Osc_bel_terminates_and_excludes_the_terminator_from_text_and_raw()
    {
        VtEvent actual = Assert.Single(new VtParser().Feed(Text.CodePoints("\u001b]0;title\u0007")));

        AssertOsc(actual, 0, "0;title", "<ESC>]0;title");
    }

    [Fact]
    public void Osc_st_terminator_is_recognized_across_feed_calls()
    {
        var parser = new VtParser();

        Assert.Empty(parser.Feed(Text.CodePoints("\u001b]0;title\u001b")));
        VtEvent actual = Assert.Single(parser.Feed(Text.CodePoints("\\")));

        AssertOsc(actual, 0, "0;title", "<ESC>]0;title");
    }

    [Fact]
    public void Osc_emits_embedded_controls_but_preserves_c1_and_supplementary_text()
    {
        List<VtEvent> events = new VtParser().Feed(
            Text.CodePoints("\u001b]A\u0001B\u007f\u0085\U0001F600\u0007"));

        Assert.Equal(2, events.Count);
        Assert.Equal(new ControlEvent(3, 0x01), events[0]);
        AssertOsc(events[1], 0, "AB\u0085\U0001F600", "<ESC>]AB\u0085\U0001F600");
    }

    [Fact]
    public void Osc_ended_by_a_non_st_escape_restarts_parsing_at_that_escape()
    {
        List<VtEvent> events = new VtParser().Feed(Text.CodePoints("\u001b]x\u001b[31m"));

        Assert.Equal(2, events.Count);
        AssertOsc(events[0], 0, "x", "<ESC>]x");
        AssertCsi(events[1], 3, '\0', new[] { 31 }, "", 'm', false, "<ESC>[31m");
    }

    [Fact]
    public void Osc_ended_by_an_escape_final_emits_both_sequence_events()
    {
        List<VtEvent> events = new VtParser().Feed(Text.CodePoints("\u001b]x\u001bA"));

        Assert.Equal(2, events.Count);
        AssertOsc(events[0], 0, "x", "<ESC>]x");
        AssertEsc(events[1], 3, "", 'A', "<ESC>A");
    }

    [Fact]
    public void Osc_finish_with_pending_escape_emits_the_osc_and_unterminated_escape()
    {
        var parser = new VtParser();

        Assert.Empty(parser.Feed(Text.CodePoints("\u001b]title\u001b")));
        List<VtEvent> events = parser.Finish();

        Assert.Equal(2, events.Count);
        AssertOsc(events[0], 0, "title", "<ESC>]title");
        Assert.Equal(new UnterminatedEvent(7, "esc"), events[1]);
    }

    [Theory]
    [InlineData("\u001bPpayload\u001b\\", "<ESC>P")]
    [InlineData("\u001bXpayload\u001b\\", "<ESC>X")]
    [InlineData("\u001b^payload\u001b\\", "<ESC>^")]
    [InlineData("\u001b_payload\u001b\\", "<ESC>_")]
    public void Control_strings_ignore_payload_and_emit_only_the_introductory_raw(string input, string raw)
    {
        VtEvent actual = Assert.Single(new VtParser().Feed(Text.CodePoints(input)));

        AssertString(actual, 0, raw);
    }

    [Fact]
    public void String_payload_controls_execute_bel_does_not_terminate_and_st_does()
    {
        List<VtEvent> events = new VtParser().Feed(
            Text.CodePoints("\u001bPa\u0085\U0001F600\n\u0007b\u001b\\"));

        Assert.Equal(3, events.Count);
        Assert.Equal(new ControlEvent(5, '\n'), events[0]);
        Assert.Equal(new ControlEvent(6, 0x07), events[1]);
        AssertString(events[2], 0, "<ESC>P");
    }

    [Fact]
    public void String_payload_c1_and_supplementary_scalars_remain_suppressed()
    {
        var parser = new VtParser();

        List<VtEvent> events = parser.Feed(Text.CodePoints("\u001bPa\u0085\U0001F600"));

        Assert.Empty(events);
        Assert.Equal(new UnterminatedEvent(0, "str"), Assert.Single(parser.Finish()));
    }

    [Fact]
    public void String_ended_by_a_non_st_escape_restarts_parsing_at_that_escape()
    {
        List<VtEvent> events = new VtParser().Feed(Text.CodePoints("\u001bPpayload\u001b[31m"));

        AssertCsi(Assert.Single(events), 9, '\0', new[] { 31 }, "", 'm', false, "<ESC>[31m");
    }

    [Fact]
    public void String_st_terminator_is_recognized_across_feed_calls()
    {
        var parser = new VtParser();

        Assert.Empty(parser.Feed(Text.CodePoints("\u001bPpayload\u001b")));
        AssertString(Assert.Single(parser.Feed(Text.CodePoints("\\"))), 0, "<ESC>P");
    }

    [Fact]
    public void Finish_reports_an_open_escape_or_csi_as_an_unterminated_escape()
    {
        var escapeParser = new VtParser();
        var csiParser = new VtParser();

        Assert.Empty(escapeParser.Feed(Text.CodePoints("\u001b")));
        Assert.Empty(csiParser.Feed(Text.CodePoints("\u001b[")));
        Assert.Equal(new UnterminatedEvent(0, "esc"), Assert.Single(escapeParser.Finish()));
        Assert.Equal(new UnterminatedEvent(0, "esc"), Assert.Single(csiParser.Finish()));
    }

    [Fact]
    public void Finish_reports_open_osc_and_control_string_with_their_distinct_kinds()
    {
        var oscParser = new VtParser();
        var stringParser = new VtParser();

        Assert.Empty(oscParser.Feed(Text.CodePoints("\u001b]title")));
        Assert.Empty(stringParser.Feed(Text.CodePoints("\u001bPpayload")));
        Assert.Equal(new UnterminatedEvent(0, "osc"), Assert.Single(oscParser.Finish()));
        Assert.Equal(new UnterminatedEvent(0, "str"), Assert.Single(stringParser.Finish()));
    }

    [Fact]
    public void String_finish_with_pending_escape_reports_only_the_pending_escape()
    {
        var parser = new VtParser();

        Assert.Empty(parser.Feed(Text.CodePoints("\u001bPpayload\u001b")));
        Assert.Equal(new UnterminatedEvent(9, "esc"), Assert.Single(parser.Finish()));
    }

    [Fact]
    public void Finish_clears_pending_state_but_retains_positions_for_later_feed_calls()
    {
        var parser = new VtParser();

        Assert.Equal(new PrintEvent(0, 'A'), Assert.Single(parser.Feed(Text.CodePoints("A\u001b["))));
        Assert.Equal(new UnterminatedEvent(1, "esc"), Assert.Single(parser.Finish()));
        Assert.Empty(parser.Finish());
        Assert.Equal(new PrintEvent(3, 'B'), Assert.Single(parser.Feed(Text.CodePoints("B"))));
    }

    [Fact]
    public void Finish_is_empty_for_ground_completed_and_cancelled_sequences()
    {
        var parser = new VtParser();

        List<VtEvent> events = parser.Feed(
            Text.CodePoints("A\u001b[31m\u001b]x\u0007\u001bPz\u001b\\\u001b[\u0018"));

        Assert.Equal(4, events.Count);
        Assert.IsType<PrintEvent>(events[0]);
        Assert.IsType<CsiEvent>(events[1]);
        Assert.IsType<OscEvent>(events[2]);
        Assert.IsType<StringEvent>(events[3]);
        Assert.Empty(parser.Finish());
    }

    private static void AssertEsc(VtEvent actual, int pos, string intermediates, char final, string raw)
    {
        EscEvent ev = Assert.IsType<EscEvent>(actual);
        Assert.Equal(pos, ev.Pos);
        Assert.Equal(intermediates, ev.Intermediates);
        Assert.Equal(final, ev.Final);
        Assert.Equal(raw, ev.Raw);
    }

    private static void AssertCsi(
        VtEvent actual,
        int pos,
        char marker,
        int[] parameters,
        string intermediates,
        char final,
        bool malformed,
        string raw)
    {
        CsiEvent ev = Assert.IsType<CsiEvent>(actual);
        Assert.Equal(pos, ev.Pos);
        Assert.Equal(marker, ev.Marker);
        Assert.Equal(parameters, ev.Params);
        Assert.Equal(intermediates, ev.Intermediates);
        Assert.Equal(final, ev.Final);
        Assert.Equal(malformed, ev.Malformed);
        Assert.Equal(raw, ev.Raw);
    }

    private static void AssertOsc(VtEvent actual, int pos, string text, string raw)
    {
        OscEvent ev = Assert.IsType<OscEvent>(actual);
        Assert.Equal(pos, ev.Pos);
        Assert.Equal(text, ev.Text);
        Assert.Equal(raw, ev.Raw);
    }

    private static void AssertString(VtEvent actual, int pos, string raw)
    {
        StringEvent ev = Assert.IsType<StringEvent>(actual);
        Assert.Equal(pos, ev.Pos);
        Assert.Equal(raw, ev.Raw);
    }
}
