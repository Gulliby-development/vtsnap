using System.Diagnostics;
using System.Text;

namespace Vtsnap.Tests;

public sealed class CliTests
{
    private const string Help =
        "usage: vtsnap render [--cols N] [--rows N] [--format text|ansi|json] [--scrollback] [--at SECONDS] FILE\n"
        + "       vtsnap frames --every SECONDS [--cols N] [--rows N] [--format text|ansi] FILE\n"
        + "       vtsnap check [--cols N] [--rows N] FILE\n"
        + "       vtsnap diff [--cols N] [--rows N] [--ignore-style] A B\n";

    private const string BuildLog =
        "\u001b[1;32mcompiling\u001b[0m core\n"
        + "\u001b[2Kdownload 10%\rdownload 60%\rdownload 100%\n"
        + "\u001b[34mdone\u001b[0m\n";

    private const string EditLog =
        "0123456789\r\n"
        + "abcdef\u001b[2;1H\u001b[L\u001b[44m\u001b[K\u001b[0m\u001b[3;4r\u001b[4;1H\n"
        + "X\u001b[?1049hALT\u001b[?1049lY\u001b7\u001b[1;1HZ\u001b8!";

    private const string DemoCast =
        "{\"version\": 2, \"width\": 20, \"height\": 3}\n"
        + "[0.5, \"o\", \"\\u001b[1mHello\\u001b[0m\"]\n"
        + "[1.25, \"o\", \"\\r\\nwor\"]\n"
        + "[2.5, \"o\", \"ld\\u001b[3;1Hdone\"]\n";

    [Fact]
    public void Help_prints_exact_usage_and_does_not_read_input()
    {
        var io = new MemoryIo();

        int exitCode = Cli.Run(new[] { "--help" }, io);

        Assert.Equal(0, exitCode);
        Assert.Equal(Help, io.Stdout);
        Assert.Empty(io.Stderr);
        Assert.Empty(io.ReadFiles);
        Assert.Equal(0, io.StdinReads);
    }

    [Theory]
    [InlineData(new string[] { }, "missing command (render, frames, check or diff)")]
    [InlineData(new[] { "wat" }, "unknown command \"wat\"")]
    [InlineData(new[] { "--help", "x" }, "unknown command \"--help\"")]
    [InlineData(new[] { "render", "--bad", "x" }, "unknown option \"--bad\"")]
    [InlineData(new[] { "render", "--ARG", "x" }, "unknown option \"--ARG\"")]
    [InlineData(new[] { "render", "--cols=VALUE", "x" }, "invalid --cols \"VALUE\"")]
    [InlineData(new[] { "render", "--cols" }, "--cols needs a value")]
    [InlineData(new[] { "render", "--cols=bad", "x" }, "invalid --cols \"bad\"")]
    [InlineData(new[] { "render", "--rows=bad", "x" }, "invalid --rows \"bad\"")]
    [InlineData(new[] { "render", "--format=bad", "x" }, "invalid --format \"bad\"")]
    [InlineData(new[] { "frames", "--format=json", "x" }, "--format json is not available for frames")]
    [InlineData(new[] { "render", "--at=bad", "x" }, "invalid --at \"bad\"")]
    [InlineData(new[] { "frames", "x" }, "missing --every")]
    [InlineData(new[] { "frames", "--every=0", "x" }, "invalid --every \"0\"")]
    [InlineData(new[] { "render", "--scrollback", "--format=json", "x" }, "--scrollback is not available with --format json")]
    [InlineData(new[] { "render" }, "missing FILE")]
    [InlineData(new[] { "diff" }, "missing A")]
    [InlineData(new[] { "diff", "a" }, "missing B")]
    [InlineData(new[] { "render", "a", "b" }, "too many arguments")]
    [InlineData(new[] { "diff", "-", "-" }, "- can be given once")]
    [InlineData(new[] { "render", "--every=1", "x" }, "unknown option \"--every=1\"")]
    public void Validation_errors_have_the_exact_prefix_exit_code_and_no_io(string[] args, string message)
    {
        var io = new MemoryIo();

        int exitCode = Cli.Run(args, io);

        Assert.Equal(2, exitCode);
        Assert.Empty(io.Stdout);
        Assert.Equal($"vtsnap: {message}\n", io.Stderr);
        Assert.Empty(io.ReadFiles);
        Assert.Equal(0, io.StdinReads);
    }

    [Fact]
    public void Scan_errors_precede_value_errors_and_values_precede_operand_errors()
    {
        var scanIo = new MemoryIo();
        int scanCode = Cli.Run(new[] { "render", "--cols=bad", "--unknown" }, scanIo);
        Assert.Equal(2, scanCode);
        Assert.Empty(scanIo.Stdout);
        Assert.Equal("vtsnap: unknown option \"--unknown\"\n", scanIo.Stderr);
        Assert.Empty(scanIo.ReadFiles);

        var valueIo = new MemoryIo();
        int valueCode = Cli.Run(new[] { "render", "--rows=bad", "--cols=bad" }, valueIo);
        Assert.Equal(2, valueCode);
        Assert.Empty(valueIo.Stdout);
        Assert.Equal("vtsnap: invalid --cols \"bad\"\n", valueIo.Stderr);
        Assert.Empty(valueIo.ReadFiles);

        var operandIo = new MemoryIo();
        int operandCode = Cli.Run(new[] { "render", "--cols=40" }, operandIo);
        Assert.Equal(2, operandCode);
        Assert.Empty(operandIo.Stdout);
        Assert.Equal("vtsnap: missing FILE\n", operandIo.Stderr);
        Assert.Empty(operandIo.ReadFiles);
    }

    [Fact]
    public void Render_build_log_matches_the_text_and_ansi_worked_examples()
    {
        var textIo = FileIo("build.log", BuildLog);

        int textCode = Cli.Run(new[] { "render", "--cols", "24", "--rows", "4", "build.log" }, textIo);

        Assert.Equal(0, textCode);
        Assert.Equal("compiling core\ndownload 100%\ndone\n", textIo.Stdout);
        Assert.Empty(textIo.Stderr);
        Assert.Equal(new[] { "build.log" }, textIo.ReadFiles);

        var ansiIo = FileIo("build.log", BuildLog);
        int ansiCode = Cli.Run(new[] { "render", "--cols=24", "--rows=4", "--format=ansi", "build.log" }, ansiIo);

        Assert.Equal(0, ansiCode);
        Assert.Equal("\u001b[0;1;32mcompiling\u001b[0m core\ndownload 100%\n\u001b[0;34mdone\u001b[0m\n", ansiIo.Stdout);
        Assert.Empty(ansiIo.Stderr);
    }

    [Fact]
    public void Render_and_check_edit_log_match_the_worked_examples()
    {
        var renderIo = FileIo("edit.log", EditLog);

        int renderCode = Cli.Run(new[] { "render", "--cols=10", "--rows=5", "edit.log" }, renderIo);

        Assert.Equal(0, renderCode);
        Assert.Equal("Z123456789\n\n\nXY!\n", renderIo.Stdout);
        Assert.Empty(renderIo.Stderr);

        var checkIo = FileIo("edit.log", EditLog);
        int checkCode = Cli.Run(new[] { "check", "--cols=10", "--rows=5", "edit.log" }, checkIo);

        Assert.Equal(1, checkCode);
        Assert.Equal("edit.log:85: VT009 scroll region is not reset at the end\n1 finding\n", checkIo.Stdout);
        Assert.Empty(checkIo.Stderr);
    }

    [Fact]
    public void Render_scrollback_includes_lines_that_scrolled_off_the_top()
    {
        const string scrollLog = "1\n2\n3\n4\n5\n";
        var screenIo = FileIo("scroll.log", scrollLog);

        int screenCode = Cli.Run(new[] { "render", "--cols=6", "--rows=3", "scroll.log" }, screenIo);

        Assert.Equal(0, screenCode);
        Assert.Equal("4\n5\n", screenIo.Stdout);

        var historyIo = FileIo("scroll.log", scrollLog);
        int historyCode = Cli.Run(new[] { "render", "--cols=6", "--rows=3", "--scrollback", "scroll.log" }, historyIo);

        Assert.Equal(0, historyCode);
        Assert.Equal("1\n2\n3\n4\n5\n", historyIo.Stdout);
        Assert.Empty(historyIo.Stderr);
    }

    [Fact]
    public void Render_json_matches_the_exact_json_worked_example()
    {
        const string jsonLog = "\u001b]0;a\"b\u0007ab\u001b[1;31mc\u001b[0m\n\u001b[44mx";
        const string expected =
            "{\"cols\":6,\"rows\":2,\"title\":\"a\\\"b\",\"cursor\":{\"row\":2,\"col\":2,\"visible\":true},\"lines\":[\n"
            + "{\"text\":\"abc\",\"spans\":[{\"col\":3,\"len\":1,\"fg\":1,\"bg\":null,\"attrs\":[\"bold\"]}]},\n"
            + "{\"text\":\"x\",\"spans\":[{\"col\":1,\"len\":1,\"fg\":null,\"bg\":4,\"attrs\":[]}]}\n"
            + "]}\n";
        var io = FileIo("json.log", jsonLog);

        int exitCode = Cli.Run(new[] { "render", "--cols=6", "--rows=2", "--format=json", "json.log" }, io);

        Assert.Equal(0, exitCode);
        Assert.Equal(expected, io.Stdout);
        Assert.Empty(io.Stderr);
    }

    [Fact]
    public void Render_demo_cast_and_inclusive_at_cutoff_match_the_worked_examples()
    {
        var fullIo = FileIo("demo.cast", DemoCast);

        int fullCode = Cli.Run(new[] { "render", "demo.cast" }, fullIo);

        Assert.Equal(0, fullCode);
        Assert.Equal("Hello\nworld\ndone\n", fullIo.Stdout);
        Assert.Empty(fullIo.Stderr);

        var cutoffIo = FileIo("demo.cast", DemoCast);
        int cutoffCode = Cli.Run(new[] { "render", "--at=1.3", "demo.cast" }, cutoffIo);

        Assert.Equal(0, cutoffCode);
        Assert.Equal("Hello\nwor\n", cutoffIo.Stdout);
        Assert.Empty(cutoffIo.Stderr);
    }

    [Fact]
    public void Frames_demo_cast_match_the_exact_off_interval_maximum_example()
    {
        var io = FileIo("demo.cast", DemoCast);

        int exitCode = Cli.Run(new[] { "frames", "--every=1", "demo.cast" }, io);

        Assert.Equal(0, exitCode);
        Assert.Equal(
            "=== 0.000 s ===\n"
            + "=== 1.000 s ===\nHello\n"
            + "=== 2.000 s ===\nHello\nwor\n"
            + "=== 2.500 s ===\nHello\nworld\ndone\n",
            io.Stdout);
        Assert.Empty(io.Stderr);
    }

    [Fact]
    public void Check_messy_log_and_diff_a_b_match_the_worked_examples()
    {
        const string messyLog =
            "\u001b[?1049h\u001b[2J\u001b[3;5HMenu\u001b[?25l\u001b[31mred"
            + "\u001b[40;1Hlate\u001b[5n\u001b[?2004h\u001b[38;5;300m!";
        var checkIo = FileIo("messy.log", messyLog);

        int checkCode = Cli.Run(new[] { "check", "messy.log" }, checkIo);

        Assert.Equal(1, checkCode);
        Assert.Equal(
            "messy.log:36: VT004 position 40;1 is outside the screen (24 rows, 80 columns)\n"
            + "messy.log:47: VT001 ignored sequence \"<ESC>[5n\"\n"
            + "messy.log:59: VT005 invalid SGR parameters \"38;5;300\"\n"
            + "messy.log:71: VT006 alternate screen is still active at the end\n"
            + "messy.log:71: VT007 cursor is hidden at the end\n"
            + "messy.log:71: VT008 text style is not reset at the end\n"
            + "6 findings\n",
            checkIo.Stdout);
        Assert.Empty(checkIo.Stderr);

        const string aLog = "one\ntwo \u001b[31mred\u001b[0m\nthree\n";
        const string bLog = "one\ntwo \u001b[32mred\u001b[0m\nthrEE\n";
        var diffIo = new MemoryIo(new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["a.log"] = Utf8(aLog),
            ["b.log"] = Utf8(bLog)
        });

        int diffCode = Cli.Run(new[] { "diff", "--cols=10", "--rows=4", "a.log", "b.log" }, diffIo);

        Assert.Equal(1, diffCode);
        Assert.Equal(
            "row 2: style differs at columns 5-7\n"
            + "row 3: text differs\n"
            + "- three\n"
            + "+ thrEE\n"
            + "2 differences\n",
            diffIo.Stdout);
        Assert.Empty(diffIo.Stderr);
        Assert.Equal(new[] { "a.log", "b.log" }, diffIo.ReadFiles);
    }

    [Fact]
    public void Clean_check_same_diff_and_size_difference_have_exact_status_and_output()
    {
        var cleanIo = FileIo("clean.log", "clean\n");

        int cleanCode = Cli.Run(new[] { "check", "clean.log" }, cleanIo);

        Assert.Equal(0, cleanCode);
        Assert.Equal("ok\n", cleanIo.Stdout);
        Assert.Empty(cleanIo.Stderr);

        var sameIo = new MemoryIo(new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["a.log"] = Utf8("same\n"),
            ["b.log"] = Utf8("same\n")
        });
        int sameCode = Cli.Run(new[] { "diff", "a.log", "b.log" }, sameIo);
        Assert.Equal(0, sameCode);
        Assert.Equal("same\n", sameIo.Stdout);

        var sizeIo = new MemoryIo(new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["a.log"] = Utf8("{\"version\":2,\"width\":80,\"height\":24}\n[0,\"o\",\"x\"]"),
            ["b.log"] = Utf8("{\"version\":2,\"width\":20,\"height\":3}\n[0,\"o\",\"x\"]")
        });
        int sizeCode = Cli.Run(new[] { "diff", "a.log", "b.log" }, sizeIo);
        Assert.Equal(1, sizeCode);
        Assert.Equal("size differs: 80x24 vs 20x3\n1 difference\n", sizeIo.Stdout);
    }

    [Fact]
    public void Check_normalizes_backslashes_for_diagnostics_and_preserves_original_read_path()
    {
        const string path = "logs\\dirty.log";
        var io = FileIo(path, "\u001b[5n");

        int exitCode = Cli.Run(new[] { "check", path }, io);

        Assert.Equal(1, exitCode);
        Assert.Equal("logs/dirty.log:0: VT001 ignored sequence \"<ESC>[5n\"\n1 finding\n", io.Stdout);
        Assert.Empty(io.Stderr);
        Assert.Equal(new[] { path }, io.ReadFiles);
    }

    [Fact]
    public void File_read_failures_use_normalized_paths_and_validation_never_reads_files()
    {
        var io = new MemoryIo();

        int exitCode = Cli.Run(new[] { "render", "logs\\missing.log" }, io);

        Assert.Equal(2, exitCode);
        Assert.Empty(io.Stdout);
        Assert.Equal("vtsnap: logs/missing.log: cannot read the file\n", io.Stderr);
        Assert.Equal(new[] { "logs\\missing.log" }, io.ReadFiles);
    }

    [Theory]
    [InlineData("{\"version\":2}\n[0,\"o\",1]", 2)]
    [InlineData("{\"version\":2}\n[0,\"o\",\"ok\"]\n\n[1,\"o\",3]", 4)]
    public void Malformed_cast_events_keep_their_physical_line_number(string cast, int line)
    {
        const string path = "dir\\broken.cast";
        var io = FileIo(path, cast);

        int exitCode = Cli.Run(new[] { "render", path }, io);

        Assert.Equal(2, exitCode);
        Assert.Empty(io.Stdout);
        Assert.Equal($"vtsnap: dir/broken.cast:{line}: bad event\n", io.Stderr);
        Assert.Equal(new[] { path }, io.ReadFiles);
    }

    [Fact]
    public void Stdin_is_read_as_bytes_and_displayed_as_stdin_in_findings()
    {
        var io = new MemoryIo { StdinBytes = new byte[] { 0x61, 0xFF, 0x62 } };

        int exitCode = Cli.Run(new[] { "check", "-" }, io);

        Assert.Equal(1, exitCode);
        Assert.Equal("<stdin>:1: VT002 invalid UTF-8 byte sequence\n1 finding\n", io.Stdout);
        Assert.Empty(io.Stderr);
        Assert.Equal(1, io.StdinReads);
        Assert.Empty(io.ReadFiles);
    }

    [Fact]
    public void Cast_required_errors_follow_loading_and_malformed_cast_errors()
    {
        var rawAtIo = FileIo("raw.log", "x");
        int rawAtCode = Cli.Run(new[] { "render", "--at=1", "raw.log" }, rawAtIo);
        Assert.Equal(2, rawAtCode);
        Assert.Empty(rawAtIo.Stdout);
        Assert.Equal("vtsnap: --at needs a cast file\n", rawAtIo.Stderr);
        Assert.Equal(new[] { "raw.log" }, rawAtIo.ReadFiles);

        var rawFramesIo = FileIo("raw.log", "x");
        int rawFramesCode = Cli.Run(new[] { "frames", "--every=1", "raw.log" }, rawFramesIo);
        Assert.Equal(2, rawFramesCode);
        Assert.Empty(rawFramesIo.Stdout);
        Assert.Equal("vtsnap: frames needs a cast file\n", rawFramesIo.Stderr);

        var malformedIo = FileIo("bad.cast", "{\"version\":2}\n[0,\"o\",1]");
        int malformedCode = Cli.Run(new[] { "render", "--at=1", "bad.cast" }, malformedIo);
        Assert.Equal(2, malformedCode);
        Assert.Empty(malformedIo.Stdout);
        Assert.Equal("vtsnap: bad.cast:2: bad event\n", malformedIo.Stderr);
    }

    [Fact]
    public void Diff_reads_a_then_b_and_reports_no_partial_stdout_when_a_or_b_fails()
    {
        var missingA = new MemoryIo();
        int aCode = Cli.Run(new[] { "diff", "missing-a", "unused-b" }, missingA);
        Assert.Equal(2, aCode);
        Assert.Empty(missingA.Stdout);
        Assert.Equal("vtsnap: missing-a: cannot read the file\n", missingA.Stderr);
        Assert.Equal(new[] { "missing-a" }, missingA.ReadFiles);

        var missingB = new MemoryIo(new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["good-a"] = Utf8("A")
        });
        int bCode = Cli.Run(new[] { "diff", "good-a", "missing-b" }, missingB);
        Assert.Equal(2, bCode);
        Assert.Empty(missingB.Stdout);
        Assert.Equal("vtsnap: missing-b: cannot read the file\n", missingB.Stderr);
        Assert.Equal(new[] { "good-a", "missing-b" }, missingB.ReadFiles);
    }

    [Fact]
    public void Cast_header_dimensions_and_explicit_overrides_control_screen_wrapping()
    {
        const string cast = "{\"version\":2,\"width\":3,\"height\":2}\n[0,\"o\",\"abcd\"]\n";
        var headerIo = FileIo("size.cast", cast);

        int headerCode = Cli.Run(new[] { "render", "size.cast" }, headerIo);

        Assert.Equal(0, headerCode);
        Assert.Equal("abc\nd\n", headerIo.Stdout);

        var overrideIo = FileIo("size.cast", cast);
        int overrideCode = Cli.Run(new[] { "render", "size.cast", "--cols=4", "--rows=1" }, overrideIo);
        Assert.Equal(0, overrideCode);
        Assert.Equal("abcd\n", overrideIo.Stdout);
    }

    [Fact]
    public void Raw_input_uses_default_eighty_column_screen_width()
    {
        const string eighty =
            "0123456789012345678901234567890123456789"
            + "0123456789012345678901234567890123456789";
        var io = FileIo("wide.log", eighty + "X");

        int exitCode = Cli.Run(new[] { "render", "wide.log" }, io);

        Assert.Equal(0, exitCode);
        Assert.Equal(eighty + "\nX\n", io.Stdout);
    }

    [Fact]
    public void At_cutoff_is_inclusive_and_does_not_apply_later_chunks()
    {
        const string cast =
            "{\"version\":2}\n"
            + "[0,\"o\",\"A\"]\n"
            + "[1,\"o\",\"B\"]\n"
            + "[2,\"o\",\"C\"]\n";
        var exactIo = FileIo("times.cast", cast);

        int exactCode = Cli.Run(new[] { "render", "--at=1", "times.cast" }, exactIo);

        Assert.Equal(0, exactCode);
        Assert.Equal("AB\n", exactIo.Stdout);

        var betweenIo = FileIo("times.cast", cast);
        int betweenCode = Cli.Run(new[] { "render", "--at=0.5", "times.cast" }, betweenIo);
        Assert.Equal(0, betweenCode);
        Assert.Equal("A\n", betweenIo.Stdout);
    }

    [Fact]
    public void Cast_sequences_can_span_chunks_and_unrestricted_check_finishes_open_sequences()
    {
        const string splitCast =
            "{\"version\":2}\n"
            + "[0,\"o\",\"\\u001b\"]\n"
            + "[1,\"o\",\"[31mR\"]\n";
        var beforeIo = FileIo("split.cast", splitCast);

        int beforeCode = Cli.Run(new[] { "render", "--at=0.5", "split.cast" }, beforeIo);

        Assert.Equal(0, beforeCode);
        Assert.Empty(beforeIo.Stdout);

        var afterIo = FileIo("split.cast", splitCast);
        int afterCode = Cli.Run(new[] { "render", "--at=1", "split.cast" }, afterIo);
        Assert.Equal(0, afterCode);
        Assert.Equal("R\n", afterIo.Stdout);

        var incompleteIo = FileIo("open.cast", "{\"version\":2}\n[0,\"o\",\"\\u001b\"]");
        int checkCode = Cli.Run(new[] { "check", "open.cast" }, incompleteIo);
        Assert.Equal(1, checkCode);
        Assert.Equal("open.cast:0: VT003 unterminated escape sequence\n1 finding\n", incompleteIo.Stdout);
    }

    [Fact]
    public void Frames_add_maximum_only_when_off_interval_and_keep_zero_for_empty_output()
    {
        const string onIntervalCast = "{\"version\":2}\n[1,\"o\",\"A\"]\n";
        var onIntervalIo = FileIo("on.cast", onIntervalCast);

        int onIntervalCode = Cli.Run(new[] { "frames", "--every=1", "on.cast" }, onIntervalIo);

        Assert.Equal(0, onIntervalCode);
        Assert.Equal("=== 0.000 s ===\n=== 1.000 s ===\nA\n", onIntervalIo.Stdout);

        var emptyIo = FileIo("empty.cast", "{\"version\":2}\n[4,\"i\",\"ignored\"]\n");
        int emptyCode = Cli.Run(new[] { "frames", "--every=2", "empty.cast" }, emptyIo);
        Assert.Equal(0, emptyCode);
        Assert.Equal("=== 0.000 s ===\n", emptyIo.Stdout);
    }

    [Fact]
    public void Frames_ansi_uses_the_ansi_renderer_without_newline_translation()
    {
        const string cast = "{\"version\":2}\n[0.5,\"o\",\"\\u001b[31mx\\u001b[0m\"]\n";
        var io = FileIo("ansi.cast", cast);

        int exitCode = Cli.Run(new[] { "frames", "--every=1", "--format=ansi", "ansi.cast" }, io);

        Assert.Equal(0, exitCode);
        Assert.Equal("=== 0.000 s ===\n=== 0.500 s ===\n\u001b[0;31mx\u001b[0m\n", io.Stdout);
        Assert.Empty(io.Stderr);
    }

    [Fact]
    public void Check_reports_invalid_utf8_and_unterminated_sequences_together()
    {
        var io = new MemoryIo(new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["dirty.log"] = new byte[] { 0x41, 0xFF, 0x1B }
        });

        int exitCode = Cli.Run(new[] { "check", "dirty.log" }, io);

        Assert.Equal(1, exitCode);
        Assert.Equal(
            "dirty.log:1: VT002 invalid UTF-8 byte sequence\n"
            + "dirty.log:2: VT003 unterminated escape sequence\n"
            + "2 findings\n",
            io.Stdout);
        Assert.Empty(io.Stderr);
    }

    [Fact]
    public async Task Process_adapter_preserves_malformed_stdin_bytes_and_emits_replacement_utf8()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Path.GetFullPath(AppContext.BaseDirectory),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("vtsnap.dll");
        startInfo.ArgumentList.Add("render");
        startInfo.ArgumentList.Add("-");

        using Process process = Process.Start(startInfo)!;
        Task<byte[]> stdoutTask = ReadBytesAsync(process.StandardOutput.BaseStream);
        Task<byte[]> stderrTask = ReadBytesAsync(process.StandardError.BaseStream);

        process.StandardInput.BaseStream.Write(new byte[] { 0x61, 0xFF, 0x62 });
        process.StandardInput.BaseStream.Flush();
        process.StandardInput.Close();

        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw;
        }

        Assert.Equal(0, process.ExitCode);
        Assert.Equal(new byte[] { 0x61, 0xEF, 0xBF, 0xBD, 0x62, 0x0A }, await stdoutTask);
        Assert.Empty(await stderrTask);
    }

    private static async Task<byte[]> ReadBytesAsync(Stream stream)
    {
        using var result = new MemoryStream();
        await stream.CopyToAsync(result);
        return result.ToArray();
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    private static MemoryIo FileIo(string path, string contents) =>
        new(new Dictionary<string, byte[]>(StringComparer.Ordinal) { [path] = Utf8(contents) });

    private sealed class MemoryIo : IIo
    {
        private readonly IReadOnlyDictionary<string, byte[]> files;

        public MemoryIo() : this(new Dictionary<string, byte[]>(StringComparer.Ordinal))
        {
        }

        public MemoryIo(IReadOnlyDictionary<string, byte[]> files)
        {
            this.files = files;
        }

        public string Stdout { get; private set; } = string.Empty;

        public string Stderr { get; private set; } = string.Empty;

        public List<string> ReadFiles { get; } = new();

        public int StdinReads { get; private set; }

        public byte[] StdinBytes { get; init; } = Array.Empty<byte>();

        public byte[] ReadFile(string path)
        {
            ReadFiles.Add(path);
            if (!files.TryGetValue(path, out byte[]? bytes))
            {
                throw new FileNotFoundException();
            }

            return bytes;
        }

        public byte[] ReadStdin()
        {
            StdinReads++;
            return StdinBytes;
        }

        public void Out(string text) => Stdout += text;

        public void Err(string text) => Stderr += text;
    }
}
