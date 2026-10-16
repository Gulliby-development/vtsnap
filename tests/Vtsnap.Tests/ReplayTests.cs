using System.Text;

namespace Vtsnap.Tests;

public sealed class ReplayTests
{
    [Fact]
    public void Load_raw_bytes_keeps_replacement_code_points_and_indices()
    {
        Source source = Replay.Load(new byte[] { 0x61, 0xFF, 0x62 }, null, null);

        Assert.False(source.IsCast);
        Assert.Equal(80, source.Cols);
        Assert.Equal(24, source.Rows);
        Assert.Equal(new[] { 1 }, source.Replaced);
        Assert.Equal(3, source.TotalCodePoints);
        CastEvent chunk = Assert.Single(source.Chunks);
        Assert.Equal(0, chunk.Micros);
        Assert.Equal(new[] { 0x61, 0xFFFD, 0x62 }, chunk.CodePoints);
    }

    [Fact]
    public void Load_raw_bytes_preserves_a_utf8_bom_as_a_code_point()
    {
        Source source = Replay.Load(new byte[] { 0xEF, 0xBB, 0xBF, 0x41 }, null, null);

        Assert.False(source.IsCast);
        Assert.Empty(source.Replaced);
        Assert.Equal(2, source.TotalCodePoints);
        Assert.Equal(new[] { 0xFEFF, 0x41 }, Assert.Single(source.Chunks).CodePoints);
    }

    [Fact]
    public void Load_empty_raw_input_keeps_one_empty_time_zero_chunk()
    {
        Source source = Replay.Load(Array.Empty<byte>(), null, null);

        Assert.False(source.IsCast);
        Assert.Equal(0, source.TotalCodePoints);
        CastEvent chunk = Assert.Single(source.Chunks);
        Assert.Equal(0, chunk.Micros);
        Assert.Empty(chunk.CodePoints);
    }

    [Fact]
    public void Load_raw_dimensions_use_defaults_and_independent_explicit_overrides()
    {
        Source defaults = Replay.Load(Encoding.UTF8.GetBytes("x"), null, null);
        Source overrides = Replay.Load(Encoding.UTF8.GetBytes("x"), 1, 500);

        Assert.Equal((80, 24), (defaults.Cols, defaults.Rows));
        Assert.Equal((1, 500), (overrides.Cols, overrides.Rows));
    }

    [Theory]
    [InlineData(1, 500)]
    [InlineData(500, 1)]
    public void Load_accepts_explicit_dimensions_at_both_supported_boundaries(int cols, int rows)
    {
        Source source = Replay.Load(Array.Empty<byte>(), cols, rows);

        Assert.Equal(cols, source.Cols);
        Assert.Equal(rows, source.Rows);
    }

    [Theory]
    [InlineData(0, null, "cols")]
    [InlineData(501, null, "cols")]
    [InlineData(null, 0, "rows")]
    [InlineData(null, 501, "rows")]
    public void Load_rejects_explicit_dimensions_outside_one_through_five_hundred(
        int? cols,
        int? rows,
        string parameterName)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => Replay.Load(Array.Empty<byte>(), cols, rows));

        Assert.Equal(parameterName, exception.ParamName);
    }

    [Fact]
    public void Load_cast_dimensions_apply_explicit_overrides_independently_of_header_dimensions()
    {
        byte[] cast = Utf8("{\"version\":2,\"width\":20,\"height\":3}\n[0,\"o\",\"x\"]");

        Source explicitColumns = Replay.Load(cast, 500, null);
        Source explicitRows = Replay.Load(cast, null, 1);

        Assert.True(explicitColumns.IsCast);
        Assert.Equal((500, 3), (explicitColumns.Cols, explicitColumns.Rows));
        Assert.Equal((20, 1), (explicitRows.Cols, explicitRows.Rows));
    }

    [Fact]
    public void Load_cast_without_dimensions_uses_eighty_by_twenty_four()
    {
        Source source = Replay.Load(Utf8("{\"version\":2}\n[0,\"o\",\"x\"]"), null, null);

        Assert.True(source.IsCast);
        Assert.Equal((80, 24), (source.Cols, source.Rows));
    }

    [Fact]
    public void Load_cast_keeps_only_output_chunks_in_file_order_and_counts_their_code_points()
    {
        Source source = Replay.Load(Utf8(
            "{\"version\":2,\"width\":20,\"height\":3}\n"
            + "[2,\"o\",\"A\"]\n"
            + "[1,\"i\",\"ignored\"]\n"
            + "[1,\"o\",\"BC\"]\n"
            + "[2,\"o\",\"\"]"), null, null);

        Assert.True(source.IsCast);
        Assert.Equal(3, source.TotalCodePoints);
        Assert.Empty(source.Replaced);
        Assert.Equal(3, source.Chunks.Count);
        Assert.Equal(2_000_000, source.Chunks[0].Micros);
        Assert.Equal(new[] { (int)'A' }, source.Chunks[0].CodePoints);
        Assert.Equal(1_000_000, source.Chunks[1].Micros);
        Assert.Equal(new[] { (int)'B', (int)'C' }, source.Chunks[1].CodePoints);
        Assert.Equal(2_000_000, source.Chunks[2].Micros);
        Assert.Empty(source.Chunks[2].CodePoints);
    }

    [Fact]
    public void Load_propagates_cast_errors_with_the_original_physical_line_number()
    {
        CastException exception = Assert.Throws<CastException>(
            () => Replay.Load(Utf8("{\"version\":2}\n\n[0,\"o\",1]"), null, null));

        Assert.Equal(3, exception.Line);
    }

    [Fact]
    public void Play_selects_unsorted_equal_timestamp_chunks_without_reordering_or_early_stopping()
    {
        Source source = SourceOf(
            Chunk(2_000_000, "A"),
            Chunk(500_000, "B"),
            Chunk(1_000_000, "C"),
            Chunk(1_000_000, "D"));

        Played timed = Replay.Play(source, 1_000_000);
        Played unrestricted = Replay.Play(source, null);

        Assert.Equal(new[] { "BCD", string.Empty, string.Empty }, Rows(timed.Terminal));
        Assert.Equal(new[] { 0, 1, 2 }, timed.Events.Select(ev => ev.Pos));
        Assert.Equal(new[] { "ABCD", string.Empty, string.Empty }, Rows(unrestricted.Terminal));
        Assert.Equal(new[] { 0, 1, 2, 3 }, unrestricted.Events.Select(ev => ev.Pos));
        Assert.Equal(new[] { (int)'A', (int)'B', (int)'C', (int)'D' }, unrestricted.Events.Cast<PrintEvent>().Select(ev => ev.CodePoint).ToArray());
    }

    [Fact]
    public void Play_calls_return_independent_terminals_and_do_not_mutate_the_source()
    {
        CastEvent chunk = Chunk(0, "A");
        CastEvent[] chunks = { chunk };
        Source source = new(true, 3, 1, chunks, Array.Empty<int>(), 1);

        Played first = Replay.Play(source, null);
        first.Terminal.Apply(new PrintEvent(1, 'B'));
        Played second = Replay.Play(source, null);

        Assert.Same(chunks, source.Chunks);
        Assert.Same(chunk.CodePoints, source.Chunks[0].CodePoints);
        Assert.NotSame(first.Terminal, second.Terminal);
        Assert.NotSame(first.Events, second.Events);
        Assert.Equal(new[] { "AB" }, Rows(first.Terminal));
        Assert.Equal(new[] { "A" }, Rows(second.Terminal));
        Assert.Equal(new[] { (int)'A' }, chunk.CodePoints);
    }

    [Fact]
    public void Play_preserves_parser_state_and_positions_across_chunks_split_inside_csi()
    {
        Source source = SourceOf(Chunk(0, "\u001b[3"), Chunk(1, "1mA"));

        Played played = Replay.Play(source, null);

        Assert.Equal(2, played.Events.Count);
        CsiEvent csi = Assert.IsType<CsiEvent>(played.Events[0]);
        Assert.Equal(0, csi.Pos);
        Assert.Equal(char.MinValue, csi.Marker);
        Assert.Equal(new[] { 31 }, csi.Params);
        Assert.Equal(string.Empty, csi.Intermediates);
        Assert.Equal('m', csi.Final);
        Assert.False(csi.Malformed);
        Assert.Equal("<ESC>[31m", csi.Raw);
        Assert.Equal(new PrintEvent(5, 'A'), played.Events[1]);
        Assert.Equal(new Color(ColorKind.Indexed, 1), played.Terminal.CurrentStyle.Fg);
        Assert.Equal(new[] { "A", string.Empty, string.Empty }, Rows(played.Terminal));
    }

    [Fact]
    public void Play_preserves_parser_state_across_chunks_split_inside_an_osc_string()
    {
        Source source = SourceOf(Chunk(0, "\u001b]2;title"), Chunk(1, "\u001b\\X"));

        Played played = Replay.Play(source, null);

        Assert.Equal(new OscEvent(0, "2;title", "<ESC>]2;title"), played.Events[0]);
        Assert.Equal(new PrintEvent(11, 'X'), played.Events[1]);
        Assert.Equal("title", played.Terminal.Snapshot(false).Title);
        Assert.Equal(new[] { "X", string.Empty, string.Empty }, Rows(played.Terminal));
    }

    [Fact]
    public void Play_preserves_parser_state_across_chunks_split_inside_a_control_string()
    {
        Source source = SourceOf(Chunk(0, "\u001bPabc"), Chunk(1, "\u001b\\Z"));

        Played played = Replay.Play(source, null);

        Assert.Equal(new StringEvent(0, "<ESC>P"), played.Events[0]);
        Assert.Equal(new PrintEvent(7, 'Z'), played.Events[1]);
        Assert.Equal(new Finding(0, "VT001", "ignored sequence \"<ESC>P\""), Assert.Single(played.Terminal.Findings));
        Assert.Equal(new[] { "Z", string.Empty, string.Empty }, Rows(played.Terminal));
    }

    [Fact]
    public void Play_unrestricted_calls_finish_and_retains_unterminated_events()
    {
        Source source = SourceOf(Chunk(0, "A\u001b["));

        Played played = Replay.Play(source, null);

        Assert.Equal(new PrintEvent(0, 'A'), played.Events[0]);
        Assert.Equal(new UnterminatedEvent(1, "esc"), played.Events[1]);
        Assert.Equal(2, played.Events.Count);
        Assert.Empty(played.Terminal.Findings);
        Assert.Equal(new[] { "A", string.Empty, string.Empty }, Rows(played.Terminal));
    }

    [Fact]
    public void Play_at_or_after_the_last_chunk_does_not_finish_a_partial_sequence()
    {
        Source source = SourceOf(Chunk(10, "\u001b]0;unfinished"));

        Played atLastTimestamp = Replay.Play(source, 10);
        Played afterLastTimestamp = Replay.Play(source, long.MaxValue);

        Assert.Empty(atLastTimestamp.Events);
        Assert.Empty(afterLastTimestamp.Events);
        Assert.Equal(string.Empty, atLastTimestamp.Terminal.Snapshot(false).Title);
        Assert.Empty(atLastTimestamp.Terminal.Findings);
        Assert.Empty(afterLastTimestamp.Terminal.Findings);
    }

    [Fact]
    public void Play_raw_replacement_code_point_positions_are_counted_after_decoding()
    {
        Source source = Replay.Load(new byte[] { 0x61, 0xFF, 0x62 }, null, null);

        Played played = Replay.Play(source, null);

        Assert.Equal(new[] { new PrintEvent(0, 'a'), new PrintEvent(1, 0xFFFD), new PrintEvent(2, 'b') }, played.Events);
        Assert.Equal(0x61, played.Terminal.Snapshot(false).Lines[0][0].Char);
        Assert.Equal(0xFFFD, played.Terminal.Snapshot(false).Lines[0][1].Char);
        Assert.Equal(0x62, played.Terminal.Snapshot(false).Lines[0][2].Char);
    }

    [Fact]
    public void Frame_times_include_the_unaligned_maximum_exactly_once()
    {
        Source source = SourceOf(Chunk(2_500_000, "A"));

        Assert.Equal(new long[] { 0, 1_000_000, 2_000_000, 2_500_000 }, Replay.FrameTimes(source, 1_000_000));
    }

    [Fact]
    public void Frame_times_do_not_duplicate_an_aligned_maximum()
    {
        Source source = SourceOf(Chunk(2_000_000, "A"));

        Assert.Equal(new long[] { 0, 1_000_000, 2_000_000 }, Replay.FrameTimes(source, 1_000_000));
    }

    [Fact]
    public void Frame_times_include_a_maximum_shorter_than_the_interval()
    {
        Source source = SourceOf(Chunk(500_000, "A"));

        Assert.Equal(new long[] { 0, 500_000 }, Replay.FrameTimes(source, 1_000_000));
    }

    [Fact]
    public void Frame_times_for_empty_output_or_a_zero_timestamp_are_zero_only()
    {
        Source empty = SourceOf();
        Source zero = SourceOf(Chunk(0, "A"));

        Assert.Equal(new long[] { 0 }, Replay.FrameTimes(empty, 1));
        Assert.Equal(new long[] { 0 }, Replay.FrameTimes(zero, 1));
    }

    [Fact]
    public void Frame_times_step_safely_near_long_max_value()
    {
        Source source = SourceOf(Chunk(long.MaxValue, "A"));

        Assert.Equal(new[] { 0, long.MaxValue - 1, long.MaxValue }, Replay.FrameTimes(source, long.MaxValue - 1));
    }

    [Fact]
    public void Frame_times_use_the_largest_timestamp_even_when_chunks_are_unsorted_and_duplicated()
    {
        Source source = SourceOf(
            Chunk(2_000_000, "A"),
            Chunk(1_000_000, "B"),
            Chunk(2_000_000, "C"),
            Chunk(500_000, "D"));

        Assert.Equal(new long[] { 0, 1_000_000, 2_000_000 }, Replay.FrameTimes(source, 1_000_000));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Frame_times_reject_nonpositive_intervals(long interval)
    {
        Source source = SourceOf(Chunk(1, "A"));

        Assert.Throws<ArgumentOutOfRangeException>(() => Replay.FrameTimes(source, interval));
    }

    [Fact]
    public void Replay_of_the_edit_log_example_preserves_the_expected_rows_and_terminal_state()
    {
        const string editLog = "0123456789\r\n"
            + "abcdef\u001b[2;1H\u001b[L\u001b[44m\u001b[K\u001b[0m\u001b[3;4r\u001b[4;1H\n"
            + "X\u001b[?1049hALT\u001b[?1049lY\u001b7\u001b[1;1HZ\u001b8!";

        Played played = Replay.Play(Replay.Load(Utf8(editLog), 10, 5), null);

        Assert.Equal(new[] { "Z123456789", string.Empty, string.Empty, "XY!", string.Empty }, Rows(played.Terminal));
        Assert.False(played.Terminal.AltScreen);
        Assert.False(played.Terminal.RegionIsFull);
    }

    [Fact]
    public void Replay_of_the_demo_cast_example_preserves_unrestricted_and_timed_screens()
    {
        const string demoCast = "{\"version\": 2, \"width\": 20, \"height\": 3}\n"
            + "[0.5, \"o\", \"\\u001b[1mHello\\u001b[0m\"]\n"
            + "[1.25, \"o\", \"\\r\\nwor\"]\n"
            + "[2.5, \"o\", \"ld\\u001b[3;1Hdone\"]";

        Source source = Replay.Load(Utf8(demoCast), null, null);
        Played unrestricted = Replay.Play(source, null);
        Played timed = Replay.Play(source, 1_300_000);

        Assert.Equal(new[] { "Hello", "world", "done" }, Rows(unrestricted.Terminal));
        Assert.Equal(new[] { "Hello", "wor", string.Empty }, Rows(timed.Terminal));
        Assert.Equal(new long[] { 0, 1_000_000, 2_000_000, 2_500_000 }, Replay.FrameTimes(source, 1_000_000));
    }

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

    private static Source SourceOf(params CastEvent[] chunks) =>
        new(true, 20, 3, chunks, Array.Empty<int>(), chunks.Sum(chunk => chunk.CodePoints.Length));

    private static CastEvent Chunk(long micros, string text) =>
        new(micros, Text.CodePoints(text));

    private static string[] Rows(Terminal terminal) =>
        terminal.Snapshot(false).Lines
            .Select(line => string.Concat(line.Select(cell => Text.FromCodePoint(cell.Char))).TrimEnd(' '))
            .ToArray();
}
