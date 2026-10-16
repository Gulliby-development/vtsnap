using System.Globalization;
using System.Text;

namespace Vtsnap.Tests;

public sealed class CastTests
{
    [Theory]
    [InlineData("{\"version\":2}")]
    [InlineData("{\"version\":2.0}")]
    [InlineData("{\"version\":2e0}")]
    public void Looks_like_cast_accepts_numeric_versions_equal_to_two(string header)
    {
        Assert.True(Cast.LooksLikeCast(Utf8(header)));
    }

    [Fact]
    public void Parse_accepts_header_with_sixty_five_nested_optional_metadata_arrays()
    {
        // Thirteen hand-written groups of five arrays make the 65-level regression case explicit.
        const string fiveOpenArrays = "[[[[[";
        const string fiveCloseArrays = "]]]]]";
        const string header = "{\"version\":2,\"metadata\":"
            + fiveOpenArrays + fiveOpenArrays + fiveOpenArrays
            + fiveOpenArrays + fiveOpenArrays + fiveOpenArrays
            + fiveOpenArrays + fiveOpenArrays + fiveOpenArrays
            + fiveOpenArrays + fiveOpenArrays + fiveOpenArrays + fiveOpenArrays
            + "0"
            + fiveCloseArrays + fiveCloseArrays + fiveCloseArrays
            + fiveCloseArrays + fiveCloseArrays + fiveCloseArrays
            + fiveCloseArrays + fiveCloseArrays + fiveCloseArrays
            + fiveCloseArrays + fiveCloseArrays + fiveCloseArrays + fiveCloseArrays
            + "}";
        byte[] bytes = Utf8(header);

        Assert.True(Cast.LooksLikeCast(bytes));
        CastFile cast = Cast.Parse(bytes);
        Assert.Empty(cast.Output);
        Assert.Equal(0, cast.EventCount);
        Assert.Equal(0, cast.Duration);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"version\":\"2\"}")]
    [InlineData("{\"version\":1}")]
    [InlineData("{\"version\":3}")]
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData("{\"version\":2")]
    [InlineData("")]
    [InlineData("   ")]
    public void Looks_like_cast_rejects_missing_wrong_or_invalid_first_line(string header)
    {
        Assert.False(Cast.LooksLikeCast(Utf8(header)));
    }

    [Fact]
    public void Looks_like_cast_checks_only_the_first_physical_line()
    {
        byte[] bytes = Utf8("{\"version\":2}\nnot a cast event");

        Assert.True(Cast.LooksLikeCast(bytes));
        AssertLine(2, () => Cast.Parse(bytes));
    }

    [Fact]
    public void Parse_accepts_a_header_with_invalid_later_lines_as_a_header_then_reports_the_event_line()
    {
        byte[] bytes = Utf8("{\"version\":2}\n[0,\"o\"]");

        Assert.True(Cast.LooksLikeCast(bytes));
        AssertLine(2, () => Cast.Parse(bytes));
    }

    [Theory]
    [InlineData("{\"version\":2,\"width\":1,\"height\":500}", 1, 500)]
    [InlineData("{\"version\":2,\"width\":500,\"height\":1}", 500, 1)]
    [InlineData("{\"version\":2,\"width\":20.0,\"height\":3}", 20, 3)]
    [InlineData("{\"version\":2,\"width\":2e1,\"height\":2e0}", 20, 2)]
    public void Parse_retains_each_integral_dimension_from_one_through_five_hundred(
        string header,
        int width,
        int height)
    {
        CastFile cast = Cast.Parse(Utf8(header));

        Assert.Equal(width, cast.Width);
        Assert.Equal(height, cast.Height);
    }

    [Theory]
    [InlineData("{\"version\":2,\"width\":0,\"height\":20}", null, 20)]
    [InlineData("{\"version\":2,\"width\":501,\"height\":20}", null, 20)]
    [InlineData("{\"version\":2,\"width\":1.5,\"height\":20}", null, 20)]
    [InlineData("{\"version\":2,\"width\":\"20\",\"height\":20}", null, 20)]
    [InlineData("{\"version\":2,\"width\":null,\"height\":20}", null, 20)]
    [InlineData("{\"version\":2,\"width\":20}", 20, null)]
    [InlineData("{\"version\":2,\"width\":20,\"height\":501}", 20, null)]
    public void Parse_discards_invalid_dimensions_independently(
        string header,
        int? width,
        int? height)
    {
        CastFile cast = Cast.Parse(Utf8(header));

        Assert.Equal(width, cast.Width);
        Assert.Equal(height, cast.Height);
    }

    [Theory]
    [InlineData("{\"version\":2}")]
    [InlineData("{\"version\":2}\n")]
    [InlineData("{\"version\":2}\r\n")]
    public void Header_only_casts_have_no_events_or_duration(string input)
    {
        CastFile cast = Cast.Parse(Utf8(input));

        Assert.Empty(cast.Output);
        Assert.Equal(0, cast.EventCount);
        Assert.Equal(0, cast.Duration);
    }

    [Fact]
    public void Parse_preserves_output_order_duplicates_and_counts_non_output_events()
    {
        CastFile cast = Cast.Parse(Utf8(
            "{\"version\":2}\n"
            + "[2,\"o\",\"A\"]\n"
            + "[1,\"i\",\"ignored\"]\n"
            + "[1,\"o\",\"B\"]\n"
            + "[2,\"o\",\"C\"]\n"
            + "[9,\"i\",\"last\"]"));

        Assert.Equal(5, cast.EventCount);
        Assert.Equal(2_000_000, cast.Duration);
        Assert.Equal(3, cast.Output.Count);
        Assert.Equal(2_000_000, cast.Output[0].Micros);
        Assert.Equal(new[] { 0x41 }, cast.Output[0].CodePoints);
        Assert.Equal(1_000_000, cast.Output[1].Micros);
        Assert.Equal(new[] { 0x42 }, cast.Output[1].CodePoints);
        Assert.Equal(2_000_000, cast.Output[2].Micros);
        Assert.Equal(new[] { 0x43 }, cast.Output[2].CodePoints);
    }

    [Fact]
    public void Parse_counts_non_output_events_but_uses_zero_duration_when_there_are_no_outputs()
    {
        CastFile cast = Cast.Parse(Utf8(
            "{\"version\":2}\n"
            + "[9,\"i\",\"ignored\"]\n"
            + "[4,\"h\",\"also ignored\"]"));

        Assert.Empty(cast.Output);
        Assert.Equal(2, cast.EventCount);
        Assert.Equal(0, cast.Duration);
    }

    [Fact]
    public void Parse_retains_an_empty_output_event_at_time_zero()
    {
        CastFile cast = Cast.Parse(Utf8("{\"version\":2}\n[0,\"o\",\"\"]"));

        CastEvent output = Assert.Single(cast.Output);
        Assert.Equal(0, output.Micros);
        Assert.Empty(output.CodePoints);
        Assert.Equal(1, cast.EventCount);
        Assert.Equal(0, cast.Duration);
    }

    [Fact]
    public void Parse_decodes_json_escapes_and_supplementary_code_points()
    {
        CastFile cast = Cast.Parse(Utf8("{\"version\":2}\n[0,\"o\",\"a\\n\\u001b\\ud83d\\ude00\"]"));

        Assert.Equal(new[] { 'a', '\n', 0x1B, 0x1F600 }, Assert.Single(cast.Output).CodePoints);
    }

    [Fact]
    public void Parse_decodes_unescaped_supplementary_characters_to_one_code_point()
    {
        CastFile cast = Cast.Parse(Utf8("{\"version\":2}\n[0,\"o\",\"A\U0001F600B\"]"));

        Assert.Equal(new[] { 'A', 0x1F600, 'B' }, Assert.Single(cast.Output).CodePoints);
    }

    [Theory]
    [InlineData("{\"version\":2}\n[0,\"o\",\"\\uD800\"]")]
    [InlineData("{\"version\":2}\n[0,\"o\",\"\\uDC00\"]")]
    public void Parse_replaces_each_unpaired_json_surrogate_with_the_replacement_code_point(string input)
    {
        CastFile cast = Cast.Parse(Utf8(input));

        Assert.Equal(new[] { 0xFFFD }, Assert.Single(cast.Output).CodePoints);
    }

    [Theory]
    [InlineData("{\"version\":2}\n[0,\"o\",\"A\"]")]
    [InlineData("{\"version\":2}\r\n\r\n[0,\"o\",\"A\"]\r\n")]
    public void Parse_accepts_lf_crlf_blank_lines_and_final_lines_with_or_without_lf(string input)
    {
        CastFile cast = Cast.Parse(Utf8(input));

        Assert.Equal(1, cast.EventCount);
        Assert.Equal(new[] { 0x41 }, Assert.Single(cast.Output).CodePoints);
    }

    [Fact]
    public void Parse_counts_blank_physical_lines_when_reporting_a_bad_event()
    {
        byte[] bytes = Utf8("{\"version\":2}\n\n  \r\nnot json");

        AssertLine(4, () => Cast.Parse(bytes));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("[0,\"o\"]")]
    [InlineData("[0,\"o\",\"x\",1]")]
    [InlineData("[true,\"o\",\"x\"]")]
    [InlineData("[0,1,\"x\"]")]
    [InlineData("[0,\"o\",1]")]
    [InlineData("[0,\"o\",\"x\"")]
    public void Parse_rejects_invalid_event_shapes_types_and_json(string eventLine)
    {
        AssertLine(2, () => Cast.Parse(Utf8("{\"version\":2}\n" + eventLine)));
    }

    [Fact]
    public void Parse_validates_malformed_event_lines_even_when_the_code_is_not_output()
    {
        AssertLine(2, () => Cast.Parse(Utf8("{\"version\":2}\n[0,\"i\",1]")));
    }

    [Theory]
    [InlineData("[0,\"o\",\"x\"]", 0L)]
    [InlineData("[12,\"o\",\"x\"]", 12_000_000L)]
    [InlineData("[1.5,\"o\",\"x\"]", 1_500_000L)]
    [InlineData("[0.0000019,\"o\",\"x\"]", 1L)]
    [InlineData("[1.123456789,\"o\",\"x\"]", 1_123_456L)]
    [InlineData("[9223372036854.775807,\"o\",\"x\"]", long.MaxValue)]
    [InlineData("[9223372036854.7758079,\"o\",\"x\"]", long.MaxValue)]
    public void Parse_converts_exact_nonnegative_decimal_timestamps_to_microseconds(
        string eventLine,
        long micros)
    {
        CastFile cast = Cast.Parse(Utf8("{\"version\":2}\n" + eventLine));

        Assert.Equal(micros, Assert.Single(cast.Output).Micros);
        Assert.Equal(micros, cast.Duration);
    }

    [Theory]
    [InlineData("[-0,\"o\",\"x\"]")]
    [InlineData("[-1,\"o\",\"x\"]")]
    [InlineData("[+1,\"o\",\"x\"]")]
    [InlineData("[1e0,\"o\",\"x\"]")]
    [InlineData("[1E+0,\"o\",\"x\"]")]
    [InlineData("[\"1\",\"o\",\"x\"]")]
    [InlineData("[.5,\"o\",\"x\"]")]
    [InlineData("[1.,\"o\",\"x\"]")]
    [InlineData("[01,\"o\",\"x\"]")]
    [InlineData("[9223372036854.775808,\"o\",\"x\"]")]
    [InlineData("[9223372036855,\"o\",\"x\"]")]
    public void Parse_rejects_signed_exponent_malformed_and_overflowing_timestamps(string eventLine)
    {
        AssertLine(2, () => Cast.Parse(Utf8("{\"version\":2}\n" + eventLine)));
    }

    [Fact]
    public void Parse_timestamp_conversion_is_independent_of_current_culture()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            CastFile cast = Cast.Parse(Utf8("{\"version\":2}\n[1.5,\"o\",\"x\"]"));

            Assert.Equal(1_500_000, Assert.Single(cast.Output).Micros);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void Looks_like_cast_and_parse_reject_malformed_utf8_in_the_header()
    {
        byte[] malformedHeader = { 0xFF };

        Assert.False(Cast.LooksLikeCast(malformedHeader));
        AssertLine(1, () => Cast.Parse(malformedHeader));
    }

    [Fact]
    public void Looks_like_cast_and_parse_do_not_strip_a_utf8_bom_from_the_header()
    {
        byte[] bomHeader = { 0xEF, 0xBB, 0xBF, 0x7B, 0x22, 0x76, 0x65, 0x72, 0x73, 0x69, 0x6F, 0x6E, 0x22, 0x3A, 0x32, 0x7D };

        Assert.False(Cast.LooksLikeCast(bomHeader));
        AssertLine(1, () => Cast.Parse(bomHeader));
    }

    [Fact]
    public void Parse_rejects_malformed_utf8_in_an_output_event_line()
    {
        byte[] prefix = Utf8("{\"version\":2}\n[0,\"o\",\"");
        byte[] suffix = Utf8("\"]");
        byte[] malformedOutputLine = prefix.Concat(new byte[] { 0xE2, 0x82 }).Concat(suffix).ToArray();

        AssertLine(2, () => Cast.Parse(malformedOutputLine));
    }

    [Fact]
    public void Parse_rejects_malformed_utf8_in_a_non_output_event_line()
    {
        byte[] prefix = Utf8("{\"version\":2}\n[0,\"i\",\"");
        byte[] suffix = Utf8("\"]");
        byte[] malformedNonOutputLine = prefix.Concat(new byte[] { 0xFF }).Concat(suffix).ToArray();

        AssertLine(2, () => Cast.Parse(malformedNonOutputLine));
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    private static void AssertLine(int expectedLine, Action action)
    {
        CastException exception = Assert.Throws<CastException>(action);
        Assert.Equal(expectedLine, exception.Line);
    }
}
