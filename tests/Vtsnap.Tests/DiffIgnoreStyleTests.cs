using System.Text;

namespace Vtsnap.Tests;

public sealed class DiffIgnoreStyleTests
{
    private const string BeforeLog =
        "\u001b[31merror\u001b[0m: disk full\n"
        + "retry in 5s\n";

    private const string PlainLog =
        "error: disk full\n"
        + "retry in 5s\n";

    private const string OtherLog =
        "error: disk full\n"
        + "retry in 9s\n";

    [Fact]
    public void Diff_reports_style_without_flag_and_suppresses_only_style_when_requested()
    {
        var styleIo = Files(("before.log", BeforeLog), ("plain.log", PlainLog));

        int styleCode = Cli.Run(new[] { "diff", "before.log", "plain.log" }, styleIo);

        Assert.Equal(1, styleCode);
        Assert.Equal("row 1: style differs at columns 1-5\n1 difference\n", styleIo.Stdout);
        Assert.Empty(styleIo.Stderr);

        var ignoredStyleIo = Files(("before.log", BeforeLog), ("plain.log", PlainLog));

        int ignoredStyleCode = Cli.Run(
            new[] { "diff", "--ignore-style", "before.log", "plain.log" },
            ignoredStyleIo);

        Assert.Equal(0, ignoredStyleCode);
        Assert.Equal("same\n", ignoredStyleIo.Stdout);
        Assert.Empty(ignoredStyleIo.Stderr);

        var textIo = Files(("before.log", BeforeLog), ("other.log", OtherLog));

        int textCode = Cli.Run(new[] { "diff", "before.log", "other.log" }, textIo);

        Assert.Equal(1, textCode);
        Assert.Equal(
            "row 1: style differs at columns 1-5\n"
            + "row 2: text differs\n"
            + "- retry in 5s\n"
            + "+ retry in 9s\n"
            + "2 differences\n",
            textIo.Stdout);
        Assert.Empty(textIo.Stderr);

        var ignoredTextIo = Files(("before.log", BeforeLog), ("other.log", OtherLog));

        int ignoredTextCode = Cli.Run(
            new[] { "diff", "--ignore-style", "before.log", "other.log" },
            ignoredTextIo);

        Assert.Equal(1, ignoredTextCode);
        Assert.Equal(
            "row 2: text differs\n"
            + "- retry in 5s\n"
            + "+ retry in 9s\n"
            + "1 difference\n",
            ignoredTextIo.Stdout);
        Assert.Empty(ignoredTextIo.Stderr);
    }

    [Fact]
    public void Ignore_style_is_idempotent_and_can_appear_around_operands_and_size_options()
    {
        string[][] arguments =
        {
            new[] { "diff", "--ignore-style", "before.log", "plain.log" },
            new[] { "diff", "before.log", "--ignore-style", "plain.log" },
            new[] { "diff", "before.log", "plain.log", "--ignore-style" },
            new[]
            {
                "diff",
                "--ignore-style",
                "before.log",
                "--ignore-style",
                "plain.log",
                "--ignore-style"
            },
            new[]
            {
                "diff",
                "--cols",
                "80",
                "--rows",
                "24",
                "--ignore-style",
                "before.log",
                "plain.log"
            }
        };

        foreach (string[] args in arguments)
        {
            var io = Files(("before.log", BeforeLog), ("plain.log", PlainLog));

            int exitCode = Cli.Run(args, io);

            Assert.Equal(0, exitCode);
            Assert.Equal("same\n", io.Stdout);
            Assert.Empty(io.Stderr);
            Assert.Equal(new[] { "before.log", "plain.log" }, io.ReadFiles);
            Assert.Equal(0, io.StdinReads);
        }
    }

    [Fact]
    public void Ignore_style_accepts_stdin_in_either_operand_and_reads_it_once()
    {
        var stdinFirst = Files(new[] { ("plain.log", PlainLog) }, stdin: BeforeLog);

        int firstCode = Cli.Run(
            new[] { "diff", "--ignore-style", "-", "plain.log" },
            stdinFirst);

        Assert.Equal(0, firstCode);
        Assert.Equal("same\n", stdinFirst.Stdout);
        Assert.Empty(stdinFirst.Stderr);
        Assert.Equal(1, stdinFirst.StdinReads);
        Assert.Equal(new[] { "plain.log" }, stdinFirst.ReadFiles);

        var stdinSecond = Files(new[] { ("before.log", BeforeLog) }, stdin: PlainLog);

        int secondCode = Cli.Run(
            new[] { "diff", "before.log", "-", "--ignore-style" },
            stdinSecond);

        Assert.Equal(0, secondCode);
        Assert.Equal("same\n", stdinSecond.Stdout);
        Assert.Empty(stdinSecond.Stderr);
        Assert.Equal(1, stdinSecond.StdinReads);
        Assert.Equal(new[] { "before.log" }, stdinSecond.ReadFiles);
    }

    [Fact]
    public void Ignore_style_does_not_hide_title_cursor_or_size_differences()
    {
        const string jobLog = "\u001b]0;job\u0007\u001b[32mok\u001b[0m done\n";
        const string buildLog = "\u001b]0;build\u0007ok done\n";
        var titleIo = Files(("job.log", jobLog), ("build.log", buildLog));

        int titleCode = Cli.Run(
            new[] { "diff", "--ignore-style", "job.log", "build.log" },
            titleIo);

        Assert.Equal(1, titleCode);
        Assert.Equal("title: \"job\" vs \"build\"\n1 difference\n", titleIo.Stdout);
        Assert.Empty(titleIo.Stderr);

        var positionIo = Files(("first.log", string.Empty), ("second.log", "\u001b[1;2H"));

        int positionCode = Cli.Run(
            new[] { "diff", "first.log", "second.log", "--ignore-style" },
            positionIo);

        Assert.Equal(1, positionCode);
        Assert.Equal(
            "cursor: row 1, column 1 vs row 1, column 2\n1 difference\n",
            positionIo.Stdout);
        Assert.Empty(positionIo.Stderr);

        var visibilityIo = Files(("visible.log", string.Empty), ("hidden.log", "\u001b[?25l"));

        int visibilityCode = Cli.Run(
            new[] { "diff", "--ignore-style", "visible.log", "hidden.log" },
            visibilityIo);

        Assert.Equal(1, visibilityCode);
        Assert.Equal(
            "cursor: row 1, column 1 vs row 1, column 1\n1 difference\n",
            visibilityIo.Stdout);
        Assert.Empty(visibilityIo.Stderr);

        const string wideCast =
            "{\"version\":2,\"width\":80,\"height\":24}\n"
            + "[0,\"o\",\"A\\u001b]0;first\\u0007\\u001b[2;2H\"]";
        const string otherSizeCast =
            "{\"version\":2,\"width\":81,\"height\":24}\n"
            + "[0,\"o\",\"B\\u001b]0;second\\u0007\\u001b[3;3H\"]";
        var sizeIo = Files(("wide.cast", wideCast), ("other.cast", otherSizeCast));

        int sizeCode = Cli.Run(
            new[] { "diff", "--ignore-style", "wide.cast", "other.cast" },
            sizeIo);

        Assert.Equal(1, sizeCode);
        Assert.Equal("size differs: 80x24 vs 81x24\n1 difference\n", sizeIo.Stdout);
        Assert.Empty(sizeIo.Stderr);
    }

    [Fact]
    public void Ignore_style_keeps_trailing_space_and_combined_difference_behavior()
    {
        var trailingSpacesIo = Files(
            ("spaces.log", "same   \u001b[1;1H"),
            ("plain.log", "same\u001b[1;1H"));

        int trailingSpacesCode = Cli.Run(
            new[] { "diff", "--ignore-style", "spaces.log", "plain.log" },
            trailingSpacesIo);

        Assert.Equal(0, trailingSpacesCode);
        Assert.Equal("same\n", trailingSpacesIo.Stdout);
        Assert.Empty(trailingSpacesIo.Stderr);

        var combinedIo = Files(
            ("before.log", "first\nsecond\n\u001b[1;2H\u001b]0;old\u0007"),
            ("after.log", "FIRST\nsecond!\n\u001b[3;3H\u001b]0;new\u0007"));

        int combinedCode = Cli.Run(
            new[] { "diff", "before.log", "--ignore-style", "after.log" },
            combinedIo);

        Assert.Equal(1, combinedCode);
        Assert.Equal(
            "row 1: text differs\n"
            + "- first\n"
            + "+ FIRST\n"
            + "row 2: text differs\n"
            + "- second\n"
            + "+ second!\n"
            + "cursor: row 1, column 2 vs row 3, column 3\n"
            + "title: \"old\" vs \"new\"\n"
            + "4 differences\n",
            combinedIo.Stdout);
        Assert.Empty(combinedIo.Stderr);
    }

    [Theory]
    [InlineData(new[] { "diff", "--ignore-style=1", "A", "B" }, "unknown option \"--ignore-style=1\"")]
    [InlineData(new[] { "render", "--ignore-style", "x.log" }, "unknown option \"--ignore-style\"")]
    [InlineData(new[] { "check", "--ignore-style", "x.log" }, "unknown option \"--ignore-style\"")]
    [InlineData(new[] { "frames", "--ignore-style", "x.log" }, "unknown option \"--ignore-style\"")]
    public void Ignore_style_invalid_forms_and_other_commands_are_usage_errors(
        string[] args,
        string message)
    {
        var io = new MemoryIo();

        int exitCode = Cli.Run(args, io);

        AssertUsageError(io, exitCode, message);
    }

    [Fact]
    public void Ignore_style_preserves_scan_value_and_operand_error_precedence()
    {
        AssertUsageFailure(
            new[] { "diff", "--cols", "bad", "--bad", "--ignore-style=1", "A", "B" },
            "unknown option \"--bad\"");
        AssertUsageFailure(
            new[] { "diff", "--cols", "bad", "--ignore-style=1", "--bad", "A", "B" },
            "unknown option \"--ignore-style=1\"");
        AssertUsageFailure(
            new[] { "diff", "--ignore-style", "--cols", "bad", "A" },
            "invalid --cols \"bad\"");
        AssertUsageFailure(
            new[] { "diff", "--ignore-style", "A" },
            "missing B");
        AssertUsageFailure(
            new[] { "diff", "--ignore-style", "missing-a" },
            "missing B");
    }

    [Fact]
    public void Ignore_style_keeps_duplicate_stdin_and_unreadable_file_errors_in_order()
    {
        AssertUsageFailure(
            new[] { "diff", "--ignore-style", "-", "-" },
            "- can be given once");

        var unreadableFirst = new MemoryIo();

        int firstCode = Cli.Run(
            new[] { "diff", "--ignore-style", "missing-a", "unused-b" },
            unreadableFirst);

        AssertFileReadError(unreadableFirst, firstCode, "missing-a: cannot read the file");
        Assert.Equal(new[] { "missing-a" }, unreadableFirst.ReadFiles);

        var unreadableSecond = Files(("good-a", "readable"));

        int secondCode = Cli.Run(
            new[] { "diff", "good-a", "missing-b", "--ignore-style" },
            unreadableSecond);

        AssertFileReadError(unreadableSecond, secondCode, "missing-b: cannot read the file");
        Assert.Equal(new[] { "good-a", "missing-b" }, unreadableSecond.ReadFiles);
    }

    [Fact]
    public void Help_prints_the_exact_updated_usage_without_reading_input()
    {
        var io = new MemoryIo();

        int exitCode = Cli.Run(new[] { "--help" }, io);

        Assert.Equal(0, exitCode);
        Assert.Equal(
            "usage: vtsnap render [--cols N] [--rows N] [--format text|ansi|json] [--scrollback] [--at SECONDS] FILE\n"
            + "       vtsnap frames --every SECONDS [--cols N] [--rows N] [--format text|ansi] FILE\n"
            + "       vtsnap check [--cols N] [--rows N] FILE\n"
            + "       vtsnap diff [--cols N] [--rows N] [--ignore-style] A B\n",
            io.Stdout);
        Assert.Empty(io.Stderr);
        Assert.Empty(io.ReadFiles);
        Assert.Equal(0, io.StdinReads);
    }

    private static void AssertUsageFailure(string[] args, string message)
    {
        var io = new MemoryIo();

        int exitCode = Cli.Run(args, io);

        AssertUsageError(io, exitCode, message);
    }

    private static void AssertUsageError(MemoryIo io, int exitCode, string message)
    {
        Assert.Equal(2, exitCode);
        Assert.Empty(io.Stdout);
        Assert.Equal($"vtsnap: {message}{(char)10}", io.Stderr);
        Assert.Empty(io.ReadFiles);
        Assert.Equal(0, io.StdinReads);
    }

    private static void AssertFileReadError(MemoryIo io, int exitCode, string message)
    {
        Assert.Equal(2, exitCode);
        Assert.Empty(io.Stdout);
        Assert.Equal($"vtsnap: {message}{(char)10}", io.Stderr);
        Assert.Equal(0, io.StdinReads);
    }

    private static MemoryIo Files(
        params (string Path, string Contents)[] files)
    {
        return Files(files, stdin: string.Empty);
    }

    private static MemoryIo Files(
        (string Path, string Contents)[] files,
        string stdin)
    {
        var bytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach ((string path, string contents) in files)
        {
            bytes.Add(path, Encoding.UTF8.GetBytes(contents));
        }

        return new MemoryIo(bytes, Encoding.UTF8.GetBytes(stdin));
    }

    private sealed class MemoryIo : IIo
    {
        private readonly IReadOnlyDictionary<string, byte[]> files;
        private readonly byte[] stdin;

        public MemoryIo()
            : this(new Dictionary<string, byte[]>(StringComparer.Ordinal), Array.Empty<byte>())
        {
        }

        public MemoryIo(IReadOnlyDictionary<string, byte[]> files, byte[] stdin)
        {
            this.files = files;
            this.stdin = stdin;
        }

        public string Stdout { get; private set; } = string.Empty;

        public string Stderr { get; private set; } = string.Empty;

        public List<string> ReadFiles { get; } = new();

        public int StdinReads { get; private set; }

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
            return stdin;
        }

        public void Out(string text) => Stdout += text;

        public void Err(string text) => Stderr += text;
    }
}
