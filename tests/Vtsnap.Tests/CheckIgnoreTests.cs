using System.Text;

namespace Vtsnap.Tests;

public sealed class CheckIgnoreTests
{
    private const string HiddenAndStyled = "\x1b[?25l\x1b[31mx";

    [Fact]
    public void Check_without_the_option_reports_every_finding()
    {
        var io = Run("check", "t.log");

        Assert.Equal(1, io.ExitCode);
        Assert.Equal(
            "t.log:12: VT007 cursor is hidden at the end\n"
            + "t.log:12: VT008 text style is not reset at the end\n"
            + "2 findings\n",
            io.Stdout);
        Assert.Empty(io.Stderr);
    }

    [Fact]
    public void Ignoring_one_code_leaves_the_others_and_their_count()
    {
        var io = Run("check", "--ignore", "VT007", "t.log");

        Assert.Equal(1, io.ExitCode);
        Assert.Equal("t.log:12: VT008 text style is not reset at the end\n1 finding\n", io.Stdout);
        Assert.Empty(io.Stderr);
    }

    [Fact]
    public void Ignoring_every_finding_prints_ok_and_exits_zero()
    {
        var io = Run("check", "t.log", "--ignore=VT008,VT007,VT008");

        Assert.Equal(0, io.ExitCode);
        Assert.Equal("ok\n", io.Stdout);
        Assert.Empty(io.Stderr);
    }

    [Fact]
    public void A_code_that_does_not_occur_changes_nothing_and_the_last_option_wins()
    {
        var absent = Run("check", "--ignore", "VT001,VT009", "t.log");

        Assert.Equal(1, absent.ExitCode);
        Assert.EndsWith("2 findings\n", absent.Stdout);

        var last = Run("check", "--ignore", "VT007", "--ignore", "VT008", "t.log");

        Assert.Equal(1, last.ExitCode);
        Assert.Equal("t.log:12: VT007 cursor is hidden at the end\n1 finding\n", last.Stdout);
    }

    [Theory]
    [InlineData("VT010")]
    [InlineData("VT000")]
    [InlineData("vt007")]
    [InlineData("VT007,")]
    [InlineData(",VT007")]
    [InlineData("VT007, VT008")]
    [InlineData("")]
    public void A_value_that_is_not_a_list_of_codes_is_a_usage_error(string value)
    {
        var io = Run("check", "--ignore", value, "t.log");

        Assert.Equal(2, io.ExitCode);
        Assert.Empty(io.Stdout);
        Assert.Equal($"vtsnap: invalid --ignore \"{value}\"\n", io.Stderr);
    }

    [Fact]
    public void The_option_belongs_to_check_only_and_needs_a_value()
    {
        var render = Run("render", "--ignore", "VT007", "t.log");

        Assert.Equal(2, render.ExitCode);
        Assert.Equal("vtsnap: unknown option \"--ignore\"\n", render.Stderr);

        var missing = Run("check", "t.log", "--ignore");

        Assert.Equal(2, missing.ExitCode);
        Assert.Equal("vtsnap: --ignore needs a value\n", missing.Stderr);
    }

    private static MemoryIo Run(params string[] args)
    {
        var io = new MemoryIo(Encoding.UTF8.GetBytes(HiddenAndStyled));
        io.ExitCode = Cli.Run(args, io);
        return io;
    }

    private sealed class MemoryIo : IIo
    {
        private readonly byte[] contents;

        public MemoryIo(byte[] contents)
        {
            this.contents = contents;
        }

        public int ExitCode { get; set; }

        public string Stdout { get; private set; } = string.Empty;

        public string Stderr { get; private set; } = string.Empty;

        public byte[] ReadFile(string path) => contents;

        public byte[] ReadStdin() => Array.Empty<byte>();

        public void Out(string text) => Stdout += text;

        public void Err(string text) => Stderr += text;
    }
}
