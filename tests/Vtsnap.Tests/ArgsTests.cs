namespace Vtsnap.Tests;

public class ArgsTests
{
    [Fact]
    public void Help_is_recognized_only_as_the_sole_argument()
    {
        Parsed parsed = Args.Parse(new[] { "--help" });

        Assert.Equal("--help", parsed.Command);
        Assert.Null(parsed.Cols);
        Assert.Null(parsed.Rows);
        Assert.Equal("text", parsed.Format);
        Assert.False(parsed.Scrollback);
        Assert.Null(parsed.AtMicros);
        Assert.Null(parsed.EveryMicros);
        Assert.Empty(parsed.Files);
    }

    [Fact]
    public void Render_defaults_are_applied()
    {
        Parsed parsed = Args.Parse(new[] { "render", "screen.log" });

        Assert.Equal("render", parsed.Command);
        Assert.Null(parsed.Cols);
        Assert.Null(parsed.Rows);
        Assert.Equal("text", parsed.Format);
        Assert.False(parsed.Scrollback);
        Assert.Null(parsed.AtMicros);
        Assert.Null(parsed.EveryMicros);
        Assert.Equal(new[] { "screen.log" }, parsed.Files);
    }

    [Fact]
    public void Options_may_appear_before_and_after_operands_and_use_both_value_forms()
    {
        Parsed parsed = Args.Parse(new[]
        {
            "render", "--cols", "40", "screen.log", "--rows=12", "--format", "ansi", "--at=1.25", "--scrollback"
        });

        Assert.Equal(40, parsed.Cols);
        Assert.Equal(12, parsed.Rows);
        Assert.Equal("ansi", parsed.Format);
        Assert.Equal(1_250_000L, parsed.AtMicros);
        Assert.True(parsed.Scrollback);
        Assert.Equal(new[] { "screen.log" }, parsed.Files);
    }

    [Fact]
    public void Repeated_options_use_the_last_value_before_validation()
    {
        Parsed parsed = Args.Parse(new[]
        {
            "render", "--cols=0", "--rows=1", "--format=bad", "--at=not-seconds",
            "--cols=500", "--rows=500", "--format=json", "--at=0.0000019", "screen.log"
        });

        Assert.Equal(500, parsed.Cols);
        Assert.Equal(500, parsed.Rows);
        Assert.Equal("json", parsed.Format);
        Assert.Equal(1L, parsed.AtMicros);
    }

    [Fact]
    public void A_bad_last_repeated_value_is_not_hidden_by_an_earlier_valid_value()
    {
        AssertError("invalid --cols \"bad\"", "render", "--cols=40", "--cols=bad", "screen.log");
    }

    [Fact]
    public void Frames_parses_every_and_dimension_options()
    {
        Parsed parsed = Args.Parse(new[] { "frames", "--every=0.000001", "--cols", "1", "--rows=500", "demo.cast" });

        Assert.Equal("frames", parsed.Command);
        Assert.Equal(1L, parsed.EveryMicros);
        Assert.Equal(1, parsed.Cols);
        Assert.Equal(500, parsed.Rows);
        Assert.Equal("text", parsed.Format);
        Assert.Equal(new[] { "demo.cast" }, parsed.Files);
    }

    [Fact]
    public void Check_and_diff_accept_only_their_common_dimension_options()
    {
        Parsed check = Args.Parse(new[] { "check", "--cols=1", "--rows", "500", "screen.log" });
        Parsed diff = Args.Parse(new[] { "diff", "left.log", "--rows=1", "--cols", "500", "right.log" });

        Assert.Equal(1, check.Cols);
        Assert.Equal(500, check.Rows);
        Assert.Equal(new[] { "screen.log" }, check.Files);
        Assert.Equal(500, diff.Cols);
        Assert.Equal(1, diff.Rows);
        Assert.Equal(new[] { "left.log", "right.log" }, diff.Files);
    }

    [Theory]
    [InlineData("render")]
    [InlineData("frames")]
    public void Render_and_frames_accept_each_supported_format(string command)
    {
        string[] args = command == "frames"
            ? new[] { "frames", "--every", "1", "--format", "ansi", "demo.cast" }
            : new[] { "render", "--format", "ansi", "screen.log" };

        Assert.Equal("ansi", Args.Parse(args).Format);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("ansi")]
    [InlineData("json")]
    public void Render_accepts_each_exact_format_name(string format)
    {
        Assert.Equal(format, Args.Parse(new[] { "render", "--format", format, "screen.log" }).Format);
    }

    [Theory]
    [InlineData("render", "--every", "screen.log", "unknown option \"--every\"")]
    [InlineData("render", "--every=1", "screen.log", "unknown option \"--every=1\"")]
    [InlineData("check", "--format", "text", "unknown option \"--format\"")]
    [InlineData("check", "--at=2", "screen.log", "unknown option \"--at=2\"")]
    [InlineData("check", "--scrollback", "screen.log", "unknown option \"--scrollback\"")]
    [InlineData("frames", "--scrollback", "demo.cast", "unknown option \"--scrollback\"")]
    [InlineData("diff", "--format=json", "a.log", "unknown option \"--format=json\"")]
    [InlineData("diff", "--at=1", "a.log", "unknown option \"--at=1\"")]
    [InlineData("render", "--scrollback=true", "screen.log", "unknown option \"--scrollback=true\"")]
    [InlineData("render", "--help", "screen.log", "unknown option \"--help\"")]
    [InlineData("render", "--", "screen.log", "unknown option \"--\"")]
    [InlineData("render", "--unsupported=ARG", "screen.log", "unknown option \"--unsupported=ARG\"")]
    public void Unsupported_options_are_reported_with_the_original_spelling(
        string command,
        string first,
        string second,
        string expected)
    {
        AssertError(expected, command, first, second);
    }

    [Theory]
    [InlineData("wat", null, "unknown command \"wat\"")]
    [InlineData("--help", "x", "unknown command \"--help\"")]
    [InlineData("-", null, "unknown command \"-\"")]
    public void Unknown_commands_precede_option_and_operand_validation(string command, string? operand, string expected)
    {
        if (operand is null)
        {
            AssertError(expected, command);
        }
        else
        {
            AssertError(expected, command, operand);
        }
    }

    [Fact]
    public void Missing_command_has_the_exact_message()
    {
        AssertError("missing command (render, frames, check or diff)");
    }

    [Theory]
    [InlineData("--cols")]
    [InlineData("--rows")]
    [InlineData("--format")]
    [InlineData("--at")]
    public void Value_options_at_the_end_of_render_report_missing_values(string option)
    {
        AssertError($"{option} needs a value", "render", option);
    }

    [Fact]
    public void Frames_every_at_the_end_is_a_scan_error()
    {
        AssertError("--every needs a value", "frames", "--every");
    }

    [Fact]
    public void Scan_errors_are_reported_left_to_right_before_value_validation()
    {
        AssertError("unknown option \"--first\"", "render", "--first", "--cols");
        AssertError("unknown option \"--second\"", "render", "--cols=1", "--second", "--rows");
        AssertError("--cols needs a value", "render", "--cols");
    }

    [Fact]
    public void A_following_dash_argument_is_consumed_as_an_option_value()
    {
        AssertError("invalid --cols \"-1\"", "render", "--cols", "-1", "screen.log");
        AssertError("invalid --format \"--cols\"", "render", "--format", "--cols", "screen.log");
    }

    [Fact]
    public void An_option_like_value_is_not_reinterpreted_as_a_later_option()
    {
        AssertError("invalid --cols \"--rows\"", "render", "--cols", "--rows", "4", "screen.log");
    }

    [Theory]
    [InlineData("--cols", "1")]
    [InlineData("--cols", "500")]
    [InlineData("--rows", "1")]
    [InlineData("--rows", "500")]
    public void Dimensions_accept_both_inclusive_boundaries(string option, string value)
    {
        Parsed parsed = Args.Parse(new[] { "render", option, value, "screen.log" });

        int expected = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expected, option == "--cols" ? parsed.Cols : parsed.Rows);
    }

    [Theory]
    [InlineData("--cols", "", "invalid --cols \"\"")]
    [InlineData("--cols", "0", "invalid --cols \"0\"")]
    [InlineData("--cols", "501", "invalid --cols \"501\"")]
    [InlineData("--cols", "01", "invalid --cols \"01\"")]
    [InlineData("--cols", "+1", "invalid --cols \"+1\"")]
    [InlineData("--cols", "-1", "invalid --cols \"-1\"")]
    [InlineData("--cols", "١", "invalid --cols \"١\"")]
    [InlineData("--cols", "2147483648", "invalid --cols \"2147483648\"")]
    [InlineData("--cols", "VALUE", "invalid --cols \"VALUE\"")]
    [InlineData("--rows", "", "invalid --rows \"\"")]
    [InlineData("--rows", "0", "invalid --rows \"0\"")]
    [InlineData("--rows", "501", "invalid --rows \"501\"")]
    [InlineData("--rows", "01", "invalid --rows \"01\"")]
    [InlineData("--rows", "+1", "invalid --rows \"+1\"")]
    [InlineData("--rows", "-1", "invalid --rows \"-1\"")]
    public void Dimensions_reject_out_of_range_and_noncanonical_spellings(
        string option,
        string value,
        string expected)
    {
        AssertError(expected, "render", $"{option}={value}", "screen.log");
    }

    [Theory]
    [InlineData("0", 0L)]
    [InlineData("12", 12_000_000L)]
    [InlineData("1.5", 1_500_000L)]
    [InlineData("0.0000019", 1L)]
    [InlineData("1.1234567", 1_123_456L)]
    [InlineData("9223372036854.775807", long.MaxValue)]
    public void At_accepts_ascii_decimal_seconds_and_truncates_after_microseconds(string value, long expected)
    {
        Assert.Equal(expected, Args.Parse(new[] { "render", "--at", value, "demo.cast" }).AtMicros);
    }

    [Theory]
    [InlineData("", "invalid --at \"\"")]
    [InlineData("+1", "invalid --at \"+1\"")]
    [InlineData("-1", "invalid --at \"-1\"")]
    [InlineData(" 1", "invalid --at \" 1\"")]
    [InlineData("1 ", "invalid --at \"1 \"")]
    [InlineData("1e2", "invalid --at \"1e2\"")]
    [InlineData(".5", "invalid --at \".5\"")]
    [InlineData("1.", "invalid --at \"1.\"")]
    [InlineData("1.2.3", "invalid --at \"1.2.3\"")]
    [InlineData("١", "invalid --at \"١\"")]
    [InlineData("9223372036855", "invalid --at \"9223372036855\"")]
    [InlineData("9223372036854.775808", "invalid --at \"9223372036854.775808\"")]
    public void At_rejects_invalid_decimal_syntax_and_int64_overflow(string value, string expected)
    {
        AssertError(expected, "render", "--at", value, "demo.cast");
    }

    [Theory]
    [InlineData("1", 1_000_000L)]
    [InlineData("1.0000019", 1_000_001L)]
    [InlineData("9223372036854.775807", long.MaxValue)]
    public void Every_accepts_positive_representable_seconds(string value, long expected)
    {
        Assert.Equal(expected, Args.Parse(new[] { "frames", "--every", value, "demo.cast" }).EveryMicros);
    }

    [Theory]
    [InlineData("0", "invalid --every \"0\"")]
    [InlineData("0.0000009", "invalid --every \"0.0000009\"")]
    [InlineData("-1", "invalid --every \"-1\"")]
    [InlineData("1e2", "invalid --every \"1e2\"")]
    [InlineData("9223372036855", "invalid --every \"9223372036855\"")]
    public void Every_rejects_zero_submicrosecond_and_invalid_seconds(string value, string expected)
    {
        AssertError(expected, "frames", "--every", value, "demo.cast");
    }

    [Fact]
    public void Frames_requires_every()
    {
        AssertError("missing --every", "frames", "demo.cast");
    }

    [Fact]
    public void Frames_requires_every_before_checking_missing_operands()
    {
        AssertError("missing --every", "frames");
    }

    [Theory]
    [InlineData("JSON")]
    [InlineData("Text")]
    [InlineData("plain")]
    public void Render_format_names_are_case_sensitive_and_exact(string value)
    {
        AssertError($"invalid --format \"{value}\"", "render", "--format", value, "screen.log");
    }

    [Fact]
    public void Frames_json_is_rejected_after_format_parsing()
    {
        AssertError("--format json is not available for frames", "frames", "--format=json", "--every=1", "demo.cast");
    }

    [Fact]
    public void Frames_json_precedes_a_missing_every_value()
    {
        AssertError("--format json is not available for frames", "frames", "--format=json", "demo.cast");
    }

    [Fact]
    public void An_invalid_frames_format_precedes_the_json_availability_error()
    {
        AssertError("invalid --format \"yaml\"", "frames", "--format=yaml", "--every=1", "demo.cast");
    }

    [Fact]
    public void A_scan_error_precedes_an_earlier_invalid_format()
    {
        AssertError("--every needs a value", "frames", "--format=invalid", "--every");
    }

    [Fact]
    public void Scrollback_and_json_conflict_precedes_a_missing_file()
    {
        AssertError("--scrollback is not available with --format json", "render", "--scrollback", "--format=json");
    }

    [Fact]
    public void Invalid_at_precedes_the_scrollback_json_conflict_in_parser_and_cli()
    {
        string[] args = { "render", "--at=bad", "--scrollback", "--format=json", "screen.log" };
        AssertError("invalid --at \"bad\"", args);

        var io = new CapturingIo();
        int exitCode = Cli.Run(args, io);

        Assert.Equal(2, exitCode);
        Assert.Empty(io.Stdout);
        Assert.Equal("vtsnap: invalid --at \"bad\"\n", io.Stderr);
        Assert.Equal(0, io.FileReads);
        Assert.Equal(0, io.StdinReads);
    }

    [Fact]
    public void Invalid_at_precedes_a_missing_file()
    {
        AssertError("invalid --at \"bad\"", "render", "--at=bad");
    }

    [Fact]
    public void Dimensions_are_validated_before_rows_and_format_then_operands()
    {
        AssertError("invalid --cols \"bad\"", "render", "--cols=bad", "--rows=bad", "--format=bad");
        AssertError("invalid --rows \"bad\"", "render", "--rows=bad", "--format=bad");
        AssertError("invalid --format \"bad\"", "render", "--format=bad", "--at=bad");
    }

    [Fact]
    public void Columns_are_validated_before_rows()
    {
        AssertError("invalid --cols \"bad-cols\"", "render", "--rows=bad-rows", "--cols=bad-cols", "screen.log");
    }

    [Fact]
    public void Invalid_every_precedes_a_missing_file()
    {
        AssertError("invalid --every \"0\"", "frames", "--every=0");
    }

    [Fact]
    public void Empty_scrollback_flag_is_not_an_option_value()
    {
        AssertError("unknown option \"--scrollback=\"", "render", "--scrollback=", "screen.log");
    }

    [Theory]
    [InlineData(new string[] { "render" }, "missing FILE")]
    [InlineData(new string[] { "check" }, "missing FILE")]
    [InlineData(new string[] { "diff" }, "missing A")]
    [InlineData(new string[] { "diff", "left.log" }, "missing B")]
    [InlineData(new string[] { "render", "one.log", "two.log" }, "too many arguments")]
    [InlineData(new string[] { "check", "one.log", "two.log" }, "too many arguments")]
    [InlineData(new string[] { "diff", "a.log", "b.log", "c.log" }, "too many arguments")]
    [InlineData(new string[] { "diff", "-", "-", "extra.log" }, "too many arguments")]
    [InlineData(new string[] { "diff", "-", "-" }, "- can be given once")]
    public void Operand_count_errors_are_exact(string[] args, string expected)
    {
        AssertError(expected, args);
    }

    [Fact]
    public void Validation_errors_precede_missing_operands()
    {
        AssertError("invalid --cols \"bad\"", "render", "--cols=bad");
        AssertError("invalid --rows \"bad\"", "render", "--rows=bad");
    }

    [Fact]
    public void A_single_dash_is_an_operand_and_diff_may_read_it_once()
    {
        Assert.Equal(new[] { "-" }, Args.Parse(new[] { "render", "-" }).Files);
        Assert.Equal(new[] { "-", "right.log" }, Args.Parse(new[] { "diff", "-", "right.log" }).Files);
        Assert.Equal(new[] { "left.log", "-" }, Args.Parse(new[] { "diff", "left.log", "-" }).Files);
    }

    private sealed class CapturingIo : IIo
    {
        public string Stdout { get; private set; } = string.Empty;

        public string Stderr { get; private set; } = string.Empty;

        public int FileReads { get; private set; }

        public int StdinReads { get; private set; }

        public byte[] ReadFile(string path)
        {
            FileReads++;
            return Array.Empty<byte>();
        }

        public byte[] ReadStdin()
        {
            StdinReads++;
            return Array.Empty<byte>();
        }

        public void Out(string text) => Stdout += text;

        public void Err(string text) => Stderr += text;
    }

    private static void AssertError(string expected, params string[] args)
    {
        CliError error = Assert.Throws<CliError>(() => Args.Parse(args));
        Assert.Equal(expected, error.Message);
    }
}
