using System.IO;

namespace Vtsnap.Tests;

public class DocsTests
{
    [Fact]
    public void Readme_has_the_required_sections_and_installation_and_development_commands()
    {
        var readme = ReadReadme();

        Assert.Contains("# vtsnap\n", readme);
        Assert.Contains("## Install\n", readme);
        Assert.Contains("## Usage\n", readme);
        Assert.Contains("## Input\n", readme);
        Assert.Contains("## Screen model\n", readme);
        Assert.Contains("## Commands\n", readme);
        Assert.Contains("## Checks\n", readme);
        Assert.Contains("## Exit codes\n", readme);
        Assert.Contains("## Development\n", readme);

        Assert.Contains("dotnet build -c Release", readme);
        Assert.Contains("src/Vtsnap/bin/Release/net10.0/vtsnap", readme);
        Assert.Contains("with the platform's executable suffix", readme);
        Assert.Contains("dotnet src/Vtsnap/bin/Release/net10.0/vtsnap.dll", readme);
        Assert.Contains("dotnet build\n", readme);
        Assert.Contains("dotnet format --verify-no-changes\n", readme);
        Assert.Contains("dotnet test\n", readme);
    }

    [Fact]
    public void Readme_preserves_the_usage_and_worked_example_blocks_verbatim()
    {
        var readme = ReadReadme();

        AssertCodeBlock(
            readme,
            "text",
            "usage: vtsnap render [--cols N] [--rows N] [--format text|ansi|json] [--scrollback] [--at SECONDS] FILE\n" +
            "       vtsnap frames --every SECONDS [--cols N] [--rows N] [--format text|ansi] FILE\n" +
            "       vtsnap check [--cols N] [--rows N] FILE\n" +
            "       vtsnap diff [--cols N] [--rows N] [--ignore-style] A B\n");

        AssertCodeBlock(
            readme,
            "text",
            "<ESC>[1;32mcompiling<ESC>[0m core\n" +
            "<ESC>[2Kdownload 10%<CR>download 60%<CR>download 100%\n" +
            "<ESC>[34mdone<ESC>[0m\n");

        AssertCodeBlock(
            readme,
            "text",
            "compiling core\n" +
            "download 100%\n" +
            "done\n");

        AssertCodeBlock(
            readme,
            "text",
            "<ESC>[0;1;32mcompiling<ESC>[0m core\n" +
            "download 100%\n" +
            "<ESC>[0;34mdone<ESC>[0m\n");

        AssertCodeBlock(
            readme,
            "text",
            "messy.log:36: VT004 position 40;1 is outside the screen (24 rows, 80 columns)\n" +
            "messy.log:47: VT001 ignored sequence \"<ESC>[5n\"\n" +
            "messy.log:59: VT005 invalid SGR parameters \"38;5;300\"\n" +
            "messy.log:71: VT006 alternate screen is still active at the end\n" +
            "messy.log:71: VT007 cursor is hidden at the end\n" +
            "messy.log:71: VT008 text style is not reset at the end\n" +
            "6 findings\n");
    }

    [Fact]
    public void Readme_explains_input_path_and_cast_rules()
    {
        var readme = ReadReadme();

        Assert.Contains(
            "An input is either raw bytes or an asciinema version 2 cast file. A file is treated as a cast when its first line is a JSON object with numeric `\"version\": 2`; otherwise it is decoded as raw UTF-8 input. Invalid UTF-8 byte sequences are replaced with U+FFFD. Raw input has no size limit.",
            readme);
        Assert.Contains(
            "A cast header may provide integer `width` and `height` values from 1 through 500. Each subsequent nonblank line is an event array containing a nonnegative decimal time in seconds, an event code, and a data string. Times use digits with an optional decimal point and digits, without a sign or exponent; fractional digits beyond six are truncated. Only events with code `\"o\"` are replayed, in file order. A carriage return before a line feed is ignored. Malformed cast events are reported with their line number.",
            readme);
        Assert.Contains(
            "Use `-` as the input name to read standard input; diagnostics display it as `<stdin>`. Other file paths in diagnostics have backslashes displayed as forward slashes. Explicit `--cols` and `--rows` options override cast dimensions. Without an explicit option or a valid cast dimension, the screen defaults to 80 columns by 24 rows.",
            readme);
    }

    [Fact]
    public void Readme_explains_screen_rendering_and_command_options()
    {
        var readme = ReadReadme();

        Assert.Contains(
            "vtsnap applies input to a virtual terminal that starts with a blank screen, the cursor at the top left, the default text style, and a visible cursor. Every Unicode code point occupies one cell; there are no character-width tables, and C1 control code points are treated as ordinary characters. Basic controls move the cursor and line-feed or carriage-return as expected. The supported escape handling includes cursor movement and addressing, screen and line erasure, row insertion/deletion and scrolling, scroll regions, saved cursor state, text attributes and colors, and alternate-screen and cursor-visibility modes. OSC 0 and OSC 2 set the screen title. Unsupported or malformed sequences may produce findings rather than changing the screen.",
            readme);
        Assert.Contains(
            "The default `render` format is `text`. Text output removes trailing spaces from each line and drops trailing empty lines. ANSI output preserves styled blank cells, trims trailing default blank cells, and resets the style at the end of each line. JSON output contains the title, cursor state, and all screen rows, including empty rows; each row contains trimmed text and spans for its non-default styles. Cursor positions in JSON are 1-based. The `--scrollback` option prepends main-screen lines that scrolled off the top; it is not available with JSON.",
            readme);
        Assert.Contains(
            "`--at SECONDS` renders a cast after every output event whose time is at or before the requested time. It is available only for casts. Frame rendering likewise uses cast event times and renders the screen at each requested frame time.",
            readme);

        Assert.Contains(
            "- `render FILE` replays an input and prints the screen. Use `--format text|ansi|json` to select the format, `--at SECONDS` for an inclusive cast cutoff, or `--scrollback` to include main-screen history (not with JSON).",
            readme);
        Assert.Contains(
            "- `frames --every SECONDS FILE` renders cast frames in text or ANSI. The interval must be greater than zero. Frames start at time zero, continue at each interval through the final output-event time, and include that final time when it is not an interval boundary.",
            readme);
        Assert.Contains("- `check FILE` reports terminal-cleanliness findings or prints `ok` when there are none.", readme);
        Assert.Contains(
            "- `diff A B` compares the screens from two inputs without scrollback. It reports a size mismatch, rows with text differences (ignoring trailing spaces) or style differences, and differences in cursor position or title.",
            readme);
        Assert.Contains(
            "The usage block lists the options available to each command. `--cols N` and `--rows N` accept decimal values from 1 through 500 with no leading zero. Value options accept the value as the next argument or after `=`; the last occurrence wins. The value is consumed as-is even if it begins with a hyphen. `--format` is available to `render` and `frames` (JSON only for `render`); `--at` is for `render`; `--every` is for `frames`; and `--scrollback` is a flag for `render`. Seconds values are nonnegative decimal digits with an optional fractional part; fractions are truncated after six digits, and signs, exponents, malformed values, or values outside the supported microsecond range are rejected. `--every` must be greater than zero.",
            readme);
    }

    [Fact]
    public void Readme_lists_all_findings_sorting_rules_and_exit_codes()
    {
        var readme = ReadReadme();

        Assert.Contains(
            "`check` reports findings in input order by position, then code, then order of occurrence. Positions are zero-based code-point indexes. End-state findings use the total number of input code points. Each finding is shown as `FILE:POS: CODE MESSAGE`, followed by a finding count.",
            readme);

        Assert.Contains("| VT001 | An escape sequence was ignored. |", readme);
        Assert.Contains("| VT002 | An invalid UTF-8 byte sequence was replaced. |", readme);
        Assert.Contains("| VT003 | An escape sequence, OSC string, or control string was unterminated. |", readme);
        Assert.Contains("| VT004 | A requested cursor position was outside the screen. |", readme);
        Assert.Contains("| VT005 | SGR parameters were invalid. |", readme);
        Assert.Contains("| VT006 | The alternate screen was still active at the end. |", readme);
        Assert.Contains("| VT007 | The cursor was hidden at the end. |", readme);
        Assert.Contains("| VT008 | The text style was not reset at the end. |", readme);
        Assert.Contains("| VT009 | The scroll region was not reset at the end. |", readme);

        Assert.Contains("| 0 | The command succeeded; `check` found no findings or `diff` found no differences. |", readme);
        Assert.Contains("| 1 | `check` found one or more findings, or `diff` found differences. |", readme);
        Assert.Contains("| 2 | A command-line, file-reading, or cast error occurred. The diagnostic is written to standard error and standard output is empty. |", readme);
    }

    private static void AssertCodeBlock(string readme, string language, string contents)
    {
        Assert.Contains($"```{language}\n{contents}```", readme);
    }

    private static string ReadReadme()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var readmePath = Path.Combine(directory.FullName, "README.md");
            if (File.Exists(readmePath))
            {
                return File.ReadAllText(readmePath).Replace("\r\n", "\n", StringComparison.Ordinal);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate README.md above the test output directory.");
    }
}
