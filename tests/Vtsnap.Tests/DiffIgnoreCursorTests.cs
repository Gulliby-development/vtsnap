using System.Text;

namespace Vtsnap.Tests;

public sealed class DiffIgnoreCursorTests
{
    private const string Moved = "\u001b[1;2H";
    private const string Far = "\u001b[3;3H";
    private const string Hidden = "\u001b[?25l";
    private const string Red = "\u001b[31mx\u001b[0m";
    private const string WideCast = "{\"version\":2,\"width\":80,\"height\":24}\n[0,\"o\",\"A\\u001b[2;2H\"]";
    private const string OtherCast = "{\"version\":2,\"width\":81,\"height\":24}\n[0,\"o\",\"A\\u001b[3;3H\"]";

    [Theory]
    [InlineData(new[] { "diff", "a.log", "b.log" }, "", Moved, 1, "cursor: row 1, column 1 vs row 1, column 2\n1 difference\n", "")]
    [InlineData(new[] { "diff", "--ignore-cursor", "a.log", "b.log" }, "", Moved, 0, "same\n", "")]
    [InlineData(new[] { "diff", "--ignore-cursor", "a.log", "b.log" }, "", Hidden, 0, "same\n", "")]
    [InlineData(new[] { "diff", "a.log", "--ignore-cursor", "b.log" }, "", Moved, 0, "same\n", "")]
    [InlineData(new[] { "diff", "a.log", "b.log", "--ignore-cursor" }, "", Moved, 0, "same\n", "")]
    [InlineData(new[] { "diff", "--ignore-cursor", "--ignore-cursor", "a.log", "b.log" }, "", Moved, 0, "same\n", "")]
    [InlineData(new[] { "diff", "--cols", "40", "--ignore-cursor", "a.log", "b.log" }, "ab", "ab" + Moved, 0, "same\n", "")]
    [InlineData(new[] { "diff", "--ignore-cursor", "a.log", "b.log" }, Red, "x", 1, "row 1: style differs at columns 1-1\n1 difference\n", "")]
    [InlineData(new[] { "diff", "--ignore-cursor", "--ignore-style", "a.log", "b.log" }, Red + Far, "x", 0, "same\n", "")]
    [InlineData(new[] { "diff", "--ignore-style", "a.log", "b.log" }, Red + Far, "x", 1, "cursor: row 3, column 3 vs row 1, column 2\n1 difference\n", "")]
    [InlineData(new[] { "diff", "--ignore-cursor", "a.log", "b.log" }, "ab" + Moved, "ac", 1, "row 1: text differs\n- ab\n+ ac\n1 difference\n", "")]
    [InlineData(new[] { "diff", "--ignore-cursor", "a.log", "b.log" }, "\u001b]0;old\u0007" + Moved, "\u001b]0;new\u0007", 1, "title: \"old\" vs \"new\"\n1 difference\n", "")]
    [InlineData(new[] { "diff", "--ignore-cursor", "a.log", "b.log" }, WideCast, OtherCast, 1, "size differs: 80x24 vs 81x24\n1 difference\n", "")]
    [InlineData(new[] { "diff", "--ignore-cursor=1", "a.log", "b.log" }, "", "", 2, "", "vtsnap: unknown option \"--ignore-cursor=1\"\n")]
    [InlineData(new[] { "render", "--ignore-cursor", "a.log" }, "", "", 2, "", "vtsnap: unknown option \"--ignore-cursor\"\n")]
    [InlineData(new[] { "check", "--ignore-cursor", "a.log" }, "", "", 2, "", "vtsnap: unknown option \"--ignore-cursor\"\n")]
    [InlineData(new[] { "frames", "--ignore-cursor", "a.log" }, "", "", 2, "", "vtsnap: unknown option \"--ignore-cursor\"\n")]
    [InlineData(new[] { "diff", "--ignore-cursor", "a.log" }, "", "", 2, "", "vtsnap: missing B\n")]
    public void Diff_ignore_cursor_leaves_out_only_the_cursor(
        string[] args,
        string first,
        string second,
        int exitCode,
        string stdout,
        string stderr)
    {
        var io = new MemoryIo(first, second);

        int code = Cli.Run(args, io);

        Assert.Equal(exitCode, code);
        Assert.Equal(stdout, io.Stdout);
        Assert.Equal(stderr, io.Stderr);
    }

    private sealed class MemoryIo : IIo
    {
        private readonly string first;
        private readonly string second;

        public MemoryIo(string first, string second)
        {
            this.first = first;
            this.second = second;
        }

        public string Stdout { get; private set; } = string.Empty;

        public string Stderr { get; private set; } = string.Empty;

        public byte[] ReadFile(string path)
        {
            return path switch
            {
                "a.log" => Encoding.UTF8.GetBytes(first),
                "b.log" => Encoding.UTF8.GetBytes(second),
                _ => throw new FileNotFoundException()
            };
        }

        public byte[] ReadStdin() => Array.Empty<byte>();

        public void Out(string text) => Stdout += text;

        public void Err(string text) => Stderr += text;
    }
}
