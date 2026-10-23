<p align="center"><img src="docs/logo.svg" width="96" alt="" /></p>

# vtsnap

vtsnap is an offline command-line tool that replays terminal output through a virtual terminal and shows the screen it produced. It is for people inspecting CI logs or captured program output, users working with asciinema recordings, and authors comparing screens in snapshot tests. It can render text, ANSI or JSON, check terminal state, and compare two screens.

[![CI](https://github.com/Gulliby-development/vtsnap/actions/workflows/ci.yml/badge.svg)](https://github.com/Gulliby-development/vtsnap/actions/workflows/ci.yml) [![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

## Install

Build the Release configuration with .NET 10:

```sh
dotnet build -c Release
```

The executable is `src/Vtsnap/bin/Release/net10.0/vtsnap`, with the platform's executable suffix (such as `.exe` on Windows). You can also run the assembly with:

```sh
dotnet src/Vtsnap/bin/Release/net10.0/vtsnap.dll
```

## Usage

With `--help` as the only argument, vtsnap prints this usage and exits successfully:

```text
usage: vtsnap render [--cols N] [--rows N] [--format text|ansi|json] [--scrollback] [--at SECONDS] FILE
       vtsnap frames --every SECONDS [--cols N] [--rows N] [--format text|ansi] FILE
       vtsnap check [--cols N] [--rows N] [--ignore CODES] FILE
       vtsnap diff [--cols N] [--rows N] [--ignore-style] A B
```

The following render excerpt uses `build.log`. In the listings, `<ESC>` and `<CR>` denote the byte values 1B and 0D; these markers are explanatory notation, not literal file text.

```text
<ESC>[1;32mcompiling<ESC>[0m core
<ESC>[2Kdownload 10%<CR>download 60%<CR>download 100%
<ESC>[34mdone<ESC>[0m
```

Rendering it with `render --cols 24 --rows 4 build.log` produces:

```text
compiling core
download 100%
done
```

The same command with `--format ansi` produces:

```text
<ESC>[0;1;32mcompiling<ESC>[0m core
download 100%
<ESC>[0;34mdone<ESC>[0m
```

For `check messy.log`, vtsnap exits with status 1 and prints:

```text
messy.log:36: VT004 position 40;1 is outside the screen (24 rows, 80 columns)
messy.log:47: VT001 ignored sequence "<ESC>[5n"
messy.log:59: VT005 invalid SGR parameters "38;5;300"
messy.log:71: VT006 alternate screen is still active at the end
messy.log:71: VT007 cursor is hidden at the end
messy.log:71: VT008 text style is not reset at the end
6 findings
```

## Input

An input is either raw bytes or an asciinema version 2 cast file. A file is treated as a cast when its first line is a JSON object with numeric `"version": 2`; otherwise it is decoded as raw UTF-8 input. Invalid UTF-8 byte sequences are replaced with U+FFFD. Raw input has no size limit.

A cast header may provide integer `width` and `height` values from 1 through 500. Each subsequent nonblank line is an event array containing a nonnegative decimal time in seconds, an event code, and a data string. Times use digits with an optional decimal point and digits, without a sign or exponent; fractional digits beyond six are truncated. Only events with code `"o"` are replayed, in file order. A carriage return before a line feed is ignored. Malformed cast events are reported with their line number.

Use `-` as the input name to read standard input; diagnostics display it as `<stdin>`. Other file paths in diagnostics have backslashes displayed as forward slashes. Explicit `--cols` and `--rows` options override cast dimensions. Without an explicit option or a valid cast dimension, the screen defaults to 80 columns by 24 rows.

## Screen model

vtsnap applies input to a virtual terminal that starts with a blank screen, the cursor at the top left, the default text style, and a visible cursor. Every Unicode code point occupies one cell; there are no character-width tables, and C1 control code points are treated as ordinary characters. Basic controls move the cursor and line-feed or carriage-return as expected. The supported escape handling includes cursor movement and addressing, screen and line erasure, row insertion/deletion and scrolling, scroll regions, saved cursor state, text attributes and colors, and alternate-screen and cursor-visibility modes. OSC 0 and OSC 2 set the screen title. Unsupported or malformed sequences may produce findings rather than changing the screen.

The default `render` format is `text`. Text output removes trailing spaces from each line and drops trailing empty lines. ANSI output preserves styled blank cells, trims trailing default blank cells, and resets the style at the end of each line. JSON output contains the title, cursor state, and all screen rows, including empty rows; each row contains trimmed text and spans for its non-default styles. Cursor positions in JSON are 1-based. The `--scrollback` option prepends main-screen lines that scrolled off the top; it is not available with JSON.

`--at SECONDS` renders a cast after every output event whose time is at or before the requested time. It is available only for casts. Frame rendering likewise uses cast event times and renders the screen at each requested frame time.

## Commands

- `render FILE` replays an input and prints the screen. Use `--format text|ansi|json` to select the format, `--at SECONDS` for an inclusive cast cutoff, or `--scrollback` to include main-screen history (not with JSON).
- `frames --every SECONDS FILE` renders cast frames in text or ANSI. The interval must be greater than zero. Frames start at time zero, continue at each interval through the final output-event time, and include that final time when it is not an interval boundary.
- `check FILE` reports terminal-cleanliness findings or prints `ok` when there are none.
- `diff A B` compares the screens from two inputs without scrollback. It reports a size mismatch, rows with text differences (ignoring trailing spaces) or style differences, and differences in cursor position or title. With `--ignore-style`, style differences are not looked for; text, size, cursor and title are still compared.

The usage block lists the options available to each command. `--cols N` and `--rows N` accept decimal values from 1 through 500 with no leading zero. Value options accept the value as the next argument or after `=`; the last occurrence wins. The value is consumed as-is even if it begins with a hyphen. `--format` is available to `render` and `frames` (JSON only for `render`); `--at` is for `render`; `--every` is for `frames`; and `--scrollback` is a flag for `render`. Seconds values are nonnegative decimal digits with an optional fractional part; fractions are truncated after six digits, and signs, exponents, malformed values, or values outside the supported microsecond range are rejected. `--every` must be greater than zero.

`--ignore-style` (a flag of `diff` only) makes the comparison ignore text styles, which helps when one recording was made with colors and the other without. With `before.log` holding `<ESC>[31merror<ESC>[0m: disk full` and `retry in 5s` on two lines, and `plain.log` holding the same two lines without the escape sequences, `diff before.log plain.log` reports a style difference in row 1 and exits with status 1, while `diff --ignore-style before.log plain.log` prints:

```text
same
```

## Checks

`check` reports findings in input order by position, then code, then order of occurrence. Positions are zero-based code-point indexes. End-state findings use the total number of input code points. Each finding is shown as `FILE:POS: CODE MESSAGE`, followed by a finding count.

`--ignore CODES` (a comma-separated list such as `VT007,VT008`, for `check` only) leaves the findings with those codes out of the report and the count; when none is left, `check` prints `ok` and exits 0. A value that is not a list of codes from `VT001` to `VT009` is a command-line error: `invalid --ignore "VALUE"`.

| Code | Meaning |
| --- | --- |
| VT001 | An escape sequence was ignored. |
| VT002 | An invalid UTF-8 byte sequence was replaced. |
| VT003 | An escape sequence, OSC string, or control string was unterminated. |
| VT004 | A requested cursor position was outside the screen. |
| VT005 | SGR parameters were invalid. |
| VT006 | The alternate screen was still active at the end. |
| VT007 | The cursor was hidden at the end. |
| VT008 | The text style was not reset at the end. |
| VT009 | The scroll region was not reset at the end. |

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | The command succeeded; `check` found no findings or `diff` found no differences. |
| 1 | `check` found one or more findings, or `diff` found differences. |
| 2 | A command-line, file-reading, or cast error occurred. The diagnostic is written to standard error and standard output is empty. |

## Development

Run the build, formatter check, and tests from the repository root:

```sh
dotnet build
dotnet format --verify-no-changes
dotnet test
```

## License

MIT, see [LICENSE](LICENSE).

---

Built with BAKED
