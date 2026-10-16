using System.Globalization;

namespace Vtsnap.Tests;

public class RenderTextAnsiTests
{
    [Fact]
    public void Text_returns_empty_for_all_default_blanks()
    {
        Snapshot snapshot = CreateSnapshot(2, 2,
            new[] { Plain(' '), Plain(' ') },
            new[] { Plain(' '), Plain(' ') });

        Assert.Equal(string.Empty, RenderText.Render(snapshot));
    }

    [Fact]
    public void Text_trims_styled_spaces_and_drops_all_empty_rows()
    {
        Style red = Foreground(1);
        Snapshot snapshot = CreateSnapshot(2, 2,
            new[] { Styled(' ', red), Styled(' ', red) },
            new[] { Plain(' '), Styled(' ', Foreground(4)) });

        Assert.Equal(string.Empty, RenderText.Render(snapshot));
    }

    [Fact]
    public void Text_trims_spaces_regardless_of_style_after_nonempty_text()
    {
        Snapshot snapshot = CreateSnapshot(4, 1,
            new[] { Plain('a'), Plain(' '), Styled(' ', Foreground(1)), Styled(' ', Background(4)) });

        Assert.Equal("a\n", RenderText.Render(snapshot));
    }

    [Fact]
    public void Text_preserves_internal_blank_rows_and_drops_trailing_blank_rows()
    {
        Snapshot snapshot = CreateSnapshot(1, 5,
            new[] { Plain(' ') },
            new[] { Plain('a') },
            new[] { Plain(' ') },
            new[] { Plain('b') },
            new[] { Plain(' ') });

        Assert.Equal("\na\n\nb\n", RenderText.Render(snapshot));
    }

    [Fact]
    public void Text_preserves_leading_spaces()
    {
        Snapshot snapshot = CreateSnapshot(2, 1, new[] { Plain(' '), Plain('a') });

        Assert.Equal(" a\n", RenderText.Render(snapshot));
    }

    [Fact]
    public void Text_renders_scrollback_before_the_screen()
    {
        Snapshot snapshot = CreateSnapshot(1, 1,
            new[] { Plain('s') },
            new[] { Plain('a') });

        Assert.Equal("s\na\n", RenderText.Render(snapshot));
    }

    [Fact]
    public void Text_preserves_supplementary_scalars_and_nonbreaking_spaces()
    {
        Snapshot snapshot = CreateSnapshot(2, 1,
            new[] { Plain(0x1F600), Plain(' ') });
        Snapshot nonbreakingSpace = CreateSnapshot(2, 1,
            new[] { Plain('a'), Plain(0x00A0) });

        Assert.Equal("😀\n", RenderText.Render(snapshot));
        Assert.Equal("a \n", RenderText.Render(nonbreakingSpace));
    }

    [Fact]
    public void Text_is_repeatable_and_does_not_change_snapshot_metadata_or_cells()
    {
        Cell[][] lines =
        {
            new[] { Plain('a'), Styled(' ', Foreground(1)) },
            new[] { Plain(0x1F600), Plain(' ') }
        };
        Snapshot snapshot = new(2, 2, "unchanged", 1, 1, false, lines);

        Assert.Equal("a\n😀\n", RenderText.Render(snapshot));
        Assert.Equal("a\n😀\n", RenderText.Render(snapshot));
        Assert.Equal(2, snapshot.Cols);
        Assert.Equal(2, snapshot.Rows);
        Assert.Equal("unchanged", snapshot.Title);
        Assert.Equal(1, snapshot.CursorRow);
        Assert.Equal(1, snapshot.CursorCol);
        Assert.False(snapshot.CursorVisible);
        Assert.Same(lines, snapshot.Lines);
        Assert.Same(lines[0], snapshot.Lines[0]);
        Assert.Same(lines[1], snapshot.Lines[1]);
        Assert.Equal(new[] { Plain('a'), Styled(' ', Foreground(1)) }, lines[0]);
        Assert.Equal(new[] { Plain(0x1F600), Plain(' ') }, lines[1]);
    }

    [Fact]
    public void Ansi_returns_empty_for_all_default_blanks()
    {
        Snapshot snapshot = CreateSnapshot(2, 2,
            new[] { Plain(' '), Plain(' ') },
            new[] { Plain(' '), Plain(' ') });

        Assert.Equal(string.Empty, RenderAnsi.Render(snapshot));
    }

    [Fact]
    public void Ansi_preserves_styled_spaces_at_the_end_of_a_line()
    {
        Snapshot snapshot = CreateSnapshot(1, 1,
            new[] { Styled(' ', Foreground(1)) });

        Assert.Equal("\u001b[0;31m \u001b[0m\n", RenderAnsi.Render(snapshot));
    }

    [Fact]
    public void Ansi_trims_only_default_blank_cells()
    {
        Snapshot snapshot = CreateSnapshot(3, 1,
            new[] { Plain('a'), Plain(' '), Styled(' ', Foreground(1)) });

        Assert.Equal("a \u001b[0;31m \u001b[0m\n", RenderAnsi.Render(snapshot));
    }

    [Fact]
    public void Ansi_emits_a_reset_when_style_returns_to_default()
    {
        Snapshot snapshot = CreateSnapshot(2, 1,
            new[] { Styled('a', Foreground(1)), Plain('b') });

        Assert.Equal("\u001b[0;31ma\u001b[0mb\n", RenderAnsi.Render(snapshot));
    }

    [Fact]
    public void Ansi_emits_one_complete_style_for_equal_neighbors()
    {
        Style red = Foreground(1);
        Snapshot snapshot = CreateSnapshot(3, 1,
            new[] { Styled('a', red), Styled('b', red), Styled('c', red) });

        Assert.Equal("\u001b[0;31mabc\u001b[0m\n", RenderAnsi.Render(snapshot));
    }

    [Fact]
    public void Ansi_resets_style_at_each_line_and_preserves_internal_empty_rows()
    {
        Snapshot snapshot = CreateSnapshot(1, 3,
            new[] { Styled('a', Foreground(1)) },
            new[] { Plain(' ') },
            new[] { Styled('b', Foreground(1)) });

        Assert.Equal("\u001b[0;31ma\u001b[0m\n\n\u001b[0;31mb\u001b[0m\n", RenderAnsi.Render(snapshot));
    }

    [Fact]
    public void Ansi_emits_all_attributes_before_foreground_and_background()
    {
        Style style = new(
            new Color(ColorKind.Indexed, 1),
            new Color(ColorKind.Indexed, 4),
            Attr.Bold | Attr.Dim | Attr.Italic | Attr.Underline | Attr.Inverse | Attr.Strike);
        Snapshot snapshot = CreateSnapshot(1, 1, new[] { Styled('X', style) });

        Assert.Equal("\u001b[0;1;2;3;4;7;9;31;44mX\u001b[0m\n", RenderAnsi.Render(snapshot));
    }

    [Theory]
    [InlineData(true, 0, "30")]
    [InlineData(true, 7, "37")]
    [InlineData(true, 8, "90")]
    [InlineData(true, 15, "97")]
    [InlineData(true, 16, "38;5;16")]
    [InlineData(true, 255, "38;5;255")]
    [InlineData(false, 0, "40")]
    [InlineData(false, 7, "47")]
    [InlineData(false, 8, "100")]
    [InlineData(false, 15, "107")]
    [InlineData(false, 16, "48;5;16")]
    [InlineData(false, 255, "48;5;255")]
    public void Ansi_encodes_indexed_color_boundaries(bool foreground, int index, string expectedCodes)
    {
        Style style = foreground ? Foreground(index) : Background(index);
        Snapshot snapshot = CreateSnapshot(1, 1, new[] { Styled('X', style) });

        Assert.Equal("\u001b[0;" + expectedCodes + "mX\u001b[0m\n", RenderAnsi.Render(snapshot));
    }

    [Theory]
    [InlineData(true, 0x0001FF, "38;2;0;1;255")]
    [InlineData(false, 0xFF0100, "48;2;255;1;0")]
    public void Ansi_encodes_rgb_channels_in_red_green_blue_order(bool foreground, int rgb, string expectedCodes)
    {
        Style style = foreground ? RgbForeground(rgb) : RgbBackground(rgb);
        Snapshot snapshot = CreateSnapshot(1, 1, new[] { Styled('X', style) });

        Assert.Equal("\u001b[0;" + expectedCodes + "mX\u001b[0m\n", RenderAnsi.Render(snapshot));
    }

    [Fact]
    public void Ansi_keeps_scrollback_and_supplementary_scalars()
    {
        Snapshot snapshot = CreateSnapshot(2, 1,
            new[] { Styled('s', Foreground(2)), Plain(' ') },
            new[] { Plain(0x1F600), Plain(' ') });

        Assert.Equal("\u001b[0;32ms\u001b[0m\n😀\n", RenderAnsi.Render(snapshot));
    }

    [Fact]
    public void Ansi_is_repeatable_and_does_not_change_snapshot_metadata_or_cells()
    {
        Cell[][] lines =
        {
            new[] { Styled('a', Foreground(1)), Plain(' ') },
            new[] { Plain(0x1F600), Plain(' ') }
        };
        Snapshot snapshot = new(2, 2, "unchanged", 1, 1, false, lines);
        const string expected = "\u001b[0;31ma\u001b[0m\n😀\n";

        Assert.Equal(expected, RenderAnsi.Render(snapshot));
        Assert.Equal(expected, RenderAnsi.Render(snapshot));
        Assert.Equal(2, snapshot.Cols);
        Assert.Equal(2, snapshot.Rows);
        Assert.Equal("unchanged", snapshot.Title);
        Assert.Equal(1, snapshot.CursorRow);
        Assert.Equal(1, snapshot.CursorCol);
        Assert.False(snapshot.CursorVisible);
        Assert.Same(lines, snapshot.Lines);
        Assert.Same(lines[0], snapshot.Lines[0]);
        Assert.Same(lines[1], snapshot.Lines[1]);
        Assert.Equal(new[] { Styled('a', Foreground(1)), Plain(' ') }, lines[0]);
        Assert.Equal(new[] { Plain(0x1F600), Plain(' ') }, lines[1]);
    }

    [Fact]
    public void Text_and_ansi_are_culture_independent()
    {
        Snapshot snapshot = CreateSnapshot(1, 1,
            new[] { Styled('X', Foreground(255)) });
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            string textEn = RenderText.Render(snapshot);
            string ansiEn = RenderAnsi.Render(snapshot);

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal(textEn, RenderText.Render(snapshot));
            Assert.Equal(ansiEn, RenderAnsi.Render(snapshot));
            Assert.Equal("X\n", textEn);
            Assert.Equal("\u001b[0;38;5;255mX\u001b[0m\n", ansiEn);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    private static Snapshot CreateSnapshot(int cols, int rows, params Cell[][] lines) =>
        new(cols, rows, string.Empty, 0, 0, true, lines);

    private static Cell Plain(int codePoint) => new(codePoint, Style.Default);

    private static Cell Styled(int codePoint, Style style) => new(codePoint, style);

    private static Style Foreground(int index) =>
        new(new Color(ColorKind.Indexed, index), Color.Default, Attr.None);

    private static Style Background(int index) =>
        new(Color.Default, new Color(ColorKind.Indexed, index), Attr.None);

    private static Style RgbForeground(int rgb) =>
        new(new Color(ColorKind.Rgb, rgb), Color.Default, Attr.None);

    private static Style RgbBackground(int rgb) =>
        new(Color.Default, new Color(ColorKind.Rgb, rgb), Attr.None);
}
