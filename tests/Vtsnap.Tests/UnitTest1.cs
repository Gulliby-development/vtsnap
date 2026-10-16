namespace Vtsnap.Tests;

using System.Globalization;

public class UnitTest1
{
    [Theory]
    [InlineData(1, "finding", "findings", "1 finding")]
    [InlineData(0, "finding", "findings", "0 findings")]
    [InlineData(-1, "finding", "findings", "-1 findings")]
    [InlineData(int.MinValue, "item", "items", "-2147483648 items")]
    [InlineData(int.MaxValue, "item", "items", "2147483647 items")]
    public void Plural_uses_invariant_count_and_singular_only_for_one(
        int count,
        string one,
        string many,
        string expected)
    {
        Assert.Equal(expected, Text.Plural(count, one, many));
    }

    [Theory]
    [InlineData(0L, "0.000")]
    [InlineData(1L, "0.000")]
    [InlineData(999L, "0.000")]
    [InlineData(1_000L, "0.001")]
    [InlineData(1_234_999L, "1.234")]
    [InlineData(-1L, "-0.000")]
    [InlineData(-1_999L, "-0.001")]
    [InlineData(long.MaxValue, "9223372036854.775")]
    [InlineData(long.MinValue, "-9223372036854.775")]
    public void Seconds_formats_truncated_milliseconds_for_the_int64_domain(long micros, string expected)
    {
        Assert.Equal(expected, Text.Seconds(micros));
    }

    [Fact]
    public void Text_formatting_and_parsing_are_independent_of_current_culture()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal("1234 items", Text.Plural(1234, "item", "items"));
            Assert.Equal("-1.234", Text.Seconds(-1_234_999));
            Assert.Equal(1_500_000L, Text.ParseSeconds("1.5"));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Theory]
    [InlineData("0", 0L)]
    [InlineData("12", 12_000_000L)]
    [InlineData("01", 1_000_000L)]
    [InlineData("1.5", 1_500_000L)]
    [InlineData("1.1234567", 1_123_456L)]
    [InlineData("0.0000009", 0L)]
    [InlineData("9223372036854.7758079", long.MaxValue)]
    [InlineData("000000000000000000000000001.000001", 1_000_001L)]
    [InlineData("0009223372036854.775807", long.MaxValue)]
    public void ParseSeconds_accepts_ascii_decimal_seconds_and_truncates_fraction(
        string input,
        long? expected)
    {
        Assert.Equal(expected, Text.ParseSeconds(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("+1")]
    [InlineData("-1")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("1e2")]
    [InlineData("١")]
    [InlineData(".5")]
    [InlineData("1.")]
    [InlineData("1.2.3")]
    [InlineData("9223372036855")]
    [InlineData("9223372036854.775808")]
    [InlineData("0009223372036854.775808")]
    [InlineData("1.000000x")]
    [InlineData("1.000000١")]
    [InlineData("1.000000e2")]
    public void ParseSeconds_rejects_invalid_syntax_and_overflow(string input)
    {
        Assert.Null(Text.ParseSeconds(input));
    }

    [Theory]
    [InlineData("", "\"\"")]
    [InlineData("\"", "\"\\\"\"")]
    [InlineData("\\", "\"\\\\\"")]
    [InlineData("\n\r\t", "\"\\n\\r\\t\"")]
    [InlineData("\0\b\u001b\u001f\u007f", "\"\\u0000\\u0008\\u001b\\u001f\\u007f\"")]
    [InlineData("/ café\u0080\U0001F600", "\"/ café\u0080\U0001F600\"")]
    public void JsonString_escapes_only_the_specified_characters(string input, string expected)
    {
        Assert.Equal(expected, Text.JsonString(input));
    }

    [Fact]
    public void CodePoints_returns_empty_for_empty_text()
    {
        Assert.Empty(Text.CodePoints(""));
    }

    [Fact]
    public void CodePoints_preserves_ascii()
    {
        Assert.Equal(new[] { 0x41 }, Text.CodePoints("A"));
    }

    [Fact]
    public void CodePoints_preserves_nul_and_bom_as_scalars()
    {
        Assert.Equal(new[] { 0, 0xFEFF }, Text.CodePoints("\0\uFEFF"));
    }

    [Fact]
    public void CodePoints_combines_a_surrogate_pair_into_one_supplementary_scalar()
    {
        Assert.Equal(new[] { 0x1F600 }, Text.CodePoints("\uD83D\uDE00"));
    }

    [Fact]
    public void CodePoints_replaces_an_unpaired_high_surrogate()
    {
        Assert.Equal(new[] { 0xFFFD }, Text.CodePoints("\uD800"));
    }

    [Fact]
    public void CodePoints_replaces_an_unpaired_low_surrogate()
    {
        Assert.Equal(new[] { 0xFFFD }, Text.CodePoints("\uDFFF"));
    }

    [Fact]
    public void CodePoints_replaces_high_surrogate_before_non_low_surrogate()
    {
        Assert.Equal(new[] { 0xFFFD, 0x41 }, Text.CodePoints("\uD800A"));
    }

    [Fact]
    public void CodePoints_replaces_each_lone_surrogate_around_a_valid_pair()
    {
        Assert.Equal(
            new[] { 0xFFFD, 0x1F600, 0xFFFD },
            Text.CodePoints("\uDC00\uD83D\uDE00\uD800"));
    }

    [Theory]
    [InlineData(0, "\0")]
    [InlineData(0x41, "A")]
    [InlineData(0xFEFF, "\uFEFF")]
    [InlineData(0x1F600, "\U0001F600")]
    [InlineData(0x10FFFF, "\U0010FFFF")]
    public void FromCodePoint_converts_valid_unicode_scalars(int codePoint, string expected)
    {
        Assert.Equal(expected, Text.FromCodePoint(codePoint));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0xD800)]
    [InlineData(0xDFFF)]
    [InlineData(0x110000)]
    public void FromCodePoint_rejects_values_outside_the_unicode_scalar_range(int codePoint)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Text.FromCodePoint(codePoint));
    }

    [Theory]
    [InlineData(new byte[] { }, new int[] { }, new int[] { })]
    [InlineData(new byte[] { 0x00 }, new int[] { 0 }, new int[] { })]
    [InlineData(new byte[] { 0x61, 0xFF, 0x62 }, new int[] { 0x61, 0xFFFD, 0x62 }, new int[] { 1 })]
    [InlineData(new byte[] { 0xE2, 0x82 }, new int[] { 0xFFFD }, new int[] { 0 })]
    [InlineData(new byte[] { 0xC0, 0x80 }, new int[] { 0xFFFD, 0xFFFD }, new int[] { 0, 1 })]
    [InlineData(new byte[] { 0xED, 0xA0, 0x80 }, new int[] { 0xFFFD, 0xFFFD, 0xFFFD }, new int[] { 0, 1, 2 })]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF }, new int[] { 0xFEFF }, new int[] { })]
    [InlineData(new byte[] { 0xEF, 0xBF, 0xBD, 0xFF }, new int[] { 0xFFFD, 0xFFFD }, new int[] { 1 })]
    [InlineData(new byte[] { 0xF0, 0x9F, 0x98, 0x80, 0xFF }, new int[] { 0x1F600, 0xFFFD }, new int[] { 1 })]
    [InlineData(new byte[] { 0xC2, 0xA2 }, new int[] { 0xA2 }, new int[] { })]
    [InlineData(new byte[] { 0xE2, 0x82, 0xAC }, new int[] { 0x20AC }, new int[] { })]
    [InlineData(new byte[] { 0xF0, 0x9F, 0x98, 0x80 }, new int[] { 0x1F600 }, new int[] { })]
    [InlineData(new byte[] { 0x80, 0xBF }, new int[] { 0xFFFD, 0xFFFD }, new int[] { 0, 1 })]
    [InlineData(new byte[] { 0xE2, 0x28, 0xA1 }, new int[] { 0xFFFD, 0x28, 0xFFFD }, new int[] { 0, 2 })]
    [InlineData(new byte[] { 0xE2, 0x82, 0x41 }, new int[] { 0xFFFD, 0x41 }, new int[] { 0 })]
    [InlineData(new byte[] { 0xF0, 0x90, 0x80 }, new int[] { 0xFFFD }, new int[] { 0 })]
    [InlineData(new byte[] { 0xF4, 0x90, 0x80, 0x80 }, new int[] { 0xFFFD, 0xFFFD, 0xFFFD, 0xFFFD }, new int[] { 0, 1, 2, 3 })]
    [InlineData(new byte[] { 0xF5, 0x80, 0x80, 0x80 }, new int[] { 0xFFFD, 0xFFFD, 0xFFFD, 0xFFFD }, new int[] { 0, 1, 2, 3 })]
    public void Decode_returns_code_points_and_replacement_indices_for_explicit_byte_sequences(
        byte[] bytes,
        int[] expectedCodePoints,
        int[] expectedReplaced)
    {
        DecodeResult result = Utf8Decoder.Decode(bytes);
        Assert.Equal(expectedCodePoints, result.CodePoints);
        Assert.Equal(expectedReplaced, result.Replaced);
    }
}
