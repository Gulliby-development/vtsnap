using System.Globalization;

namespace Vtsnap.Tests;

public class RenderJsonTests
{
    [Fact]
    public void Render_emits_exact_layout_with_first_cursor_position_and_visible_cursor()
    {
        Snapshot snapshot = CreateSnapshot(2, 1, "title", 0, 0, true,
            new[] { Plain('a'), Plain(' ') });

        Assert.Equal(
            "{\"cols\":2,\"rows\":1,\"title\":\"title\",\"cursor\":{\"row\":1,\"col\":1,\"visible\":true},\"lines\":[\n"
            + "{\"text\":\"a\",\"spans\":[]}\n"
            + "]}\n",
            RenderJson.Render(snapshot));
    }

    [Fact]
    public void Render_keeps_blank_screen_rows_and_uses_commas_only_between_rows()
    {
        Snapshot snapshot = CreateSnapshot(1, 2, string.Empty, 1, 0, false,
            new[] { Plain(' ') },
            new[] { Plain(' ') });

        Assert.Equal(
            "{\"cols\":1,\"rows\":2,\"title\":\"\",\"cursor\":{\"row\":2,\"col\":1,\"visible\":false},\"lines\":[\n"
            + "{\"text\":\"\",\"spans\":[]},\n"
            + "{\"text\":\"\",\"spans\":[]}\n"
            + "]}\n",
            RenderJson.Render(snapshot));
    }

    [Fact]
    public void Render_excludes_prefixed_scrollback_and_preserves_dimensions()
    {
        Snapshot snapshot = CreateSnapshot(2, 2, "screen", 0, 1, true,
            new[] { Styled('s', IndexedForeground(1)), Plain(' ') },
            new[] { Plain('a'), Plain(' ') },
            new[] { Plain('b'), Plain(' ') });

        Assert.Equal(
            "{\"cols\":2,\"rows\":2,\"title\":\"screen\",\"cursor\":{\"row\":1,\"col\":2,\"visible\":true},\"lines\":[\n"
            + "{\"text\":\"a\",\"spans\":[]},\n"
            + "{\"text\":\"b\",\"spans\":[]}\n"
            + "]}\n",
            RenderJson.Render(snapshot));
    }

    [Fact]
    public void Render_escapes_title_and_text_controls_and_preserves_unicode_scalars()
    {
        Snapshot snapshot = CreateSnapshot(9, 1, "\"\\\n\r\t\u0001\u007Fé😀", 0, 8, false,
            new[]
            {
                Plain('"'),
                Plain('\\'),
                Plain('\n'),
                Plain('\r'),
                Plain('\t'),
                Plain(0x0001),
                Plain(0x007F),
                Plain(0x00E9),
                Plain(0x1F600)
            });

        Assert.Equal(
            "{\"cols\":9,\"rows\":1,\"title\":\"\\\"\\\\\\n\\r\\t\\u0001\\u007fé😀\",\"cursor\":{\"row\":1,\"col\":9,\"visible\":false},\"lines\":[\n"
            + "{\"text\":\"\\\"\\\\\\n\\r\\t\\u0001\\u007fé😀\",\"spans\":[]}\n"
            + "]}\n",
            RenderJson.Render(snapshot));
    }

    [Fact]
    public void Render_trims_text_spaces_but_keeps_a_styled_blank_span()
    {
        Snapshot snapshot = CreateSnapshot(3, 1, string.Empty, 0, 0, true,
            new[]
            {
                Plain('x'),
                Styled(' ', IndexedForeground(255)),
                Plain(' ')
            });

        Assert.Equal(
            "{\"cols\":3,\"rows\":1,\"title\":\"\",\"cursor\":{\"row\":1,\"col\":1,\"visible\":true},\"lines\":[\n"
            + "{\"text\":\"x\",\"spans\":[{\"col\":2,\"len\":1,\"fg\":255,\"bg\":null,\"attrs\":[]}]}\n"
            + "]}\n",
            RenderJson.Render(snapshot));
    }

    [Fact]
    public void Render_merges_equal_styles_and_counts_supplementary_cells_once()
    {
        Style red = IndexedForeground(1);
        Snapshot snapshot = CreateSnapshot(5, 1, string.Empty, 0, 0, true,
            new[]
            {
                Styled(0x1F600, red),
                Styled('a', red),
                Plain('b'),
                Styled('c', red),
                Styled(0x1F600, IndexedBackground(4))
            });

        Assert.Equal(
            "{\"cols\":5,\"rows\":1,\"title\":\"\",\"cursor\":{\"row\":1,\"col\":1,\"visible\":true},\"lines\":[\n"
            + "{\"text\":\"😀abc😀\",\"spans\":[{\"col\":1,\"len\":2,\"fg\":1,\"bg\":null,\"attrs\":[]},{\"col\":4,\"len\":1,\"fg\":1,\"bg\":null,\"attrs\":[]},{\"col\":5,\"len\":1,\"fg\":null,\"bg\":4,\"attrs\":[]}]}\n"
            + "]}\n",
            RenderJson.Render(snapshot));
    }

    [Fact]
    public void Render_omits_default_spans_and_does_not_merge_across_default_gaps()
    {
        Snapshot snapshot = CreateSnapshot(5, 1, string.Empty, 0, 0, true,
            new[]
            {
                Plain('a'),
                Styled('b', IndexedForeground(0)),
                Plain('c'),
                Styled('d', IndexedForeground(0)),
                Plain('e')
            });

        Assert.Equal(
            "{\"cols\":5,\"rows\":1,\"title\":\"\",\"cursor\":{\"row\":1,\"col\":1,\"visible\":true},\"lines\":[\n"
            + "{\"text\":\"abcde\",\"spans\":[{\"col\":2,\"len\":1,\"fg\":0,\"bg\":null,\"attrs\":[]},{\"col\":4,\"len\":1,\"fg\":0,\"bg\":null,\"attrs\":[]}]}\n"
            + "]}\n",
            RenderJson.Render(snapshot));
    }

    [Fact]
    public void Render_encodes_default_colors_as_null_and_empty_attributes_as_empty_array()
    {
        Snapshot snapshot = CreateSnapshot(1, 1, string.Empty, 0, 0, true,
            new[] { Styled('x', new Style(Color.Default, Color.Default, Attr.Bold)) });

        Assert.Equal(
            "{\"cols\":1,\"rows\":1,\"title\":\"\",\"cursor\":{\"row\":1,\"col\":1,\"visible\":true},\"lines\":[\n"
            + "{\"text\":\"x\",\"spans\":[{\"col\":1,\"len\":1,\"fg\":null,\"bg\":null,\"attrs\":[\"bold\"]}]}\n"
            + "]}\n",
            RenderJson.Render(snapshot));
    }

    [Fact]
    public void Render_encodes_indexed_background_zero_and_255()
    {
        Snapshot snapshot = CreateSnapshot(2, 1, string.Empty, 0, 0, true,
            new[]
            {
                Styled('a', IndexedBackground(0)),
                Styled('b', IndexedBackground(255))
            });

        Assert.Equal(
            "{\"cols\":2,\"rows\":1,\"title\":\"\",\"cursor\":{\"row\":1,\"col\":1,\"visible\":true},\"lines\":[\n"
            + "{\"text\":\"ab\",\"spans\":[{\"col\":1,\"len\":1,\"fg\":null,\"bg\":0,\"attrs\":[]},{\"col\":2,\"len\":1,\"fg\":null,\"bg\":255,\"attrs\":[]}]}\n"
            + "]}\n",
            RenderJson.Render(snapshot));
    }

    [Fact]
    public void Render_encodes_rgb_colors_as_lowercase_six_digit_hex()
    {
        Snapshot snapshot = CreateSnapshot(2, 1, string.Empty, 0, 0, true,
            new[]
            {
                Styled('a', RgbForeground(0x00010F)),
                Styled('b', RgbBackground(0xFFFFFF))
            });

        Assert.Equal(
            "{\"cols\":2,\"rows\":1,\"title\":\"\",\"cursor\":{\"row\":1,\"col\":1,\"visible\":true},\"lines\":[\n"
            + "{\"text\":\"ab\",\"spans\":[{\"col\":1,\"len\":1,\"fg\":\"#00010f\",\"bg\":null,\"attrs\":[]},{\"col\":2,\"len\":1,\"fg\":null,\"bg\":\"#ffffff\",\"attrs\":[]}]}\n"
            + "]}\n",
            RenderJson.Render(snapshot));
    }

    [Fact]
    public void Render_writes_all_attributes_in_contract_order()
    {
        Style style = new(
            Color.Default,
            Color.Default,
            Attr.Bold | Attr.Dim | Attr.Italic | Attr.Underline | Attr.Inverse | Attr.Strike);
        Snapshot snapshot = CreateSnapshot(1, 1, string.Empty, 0, 0, true,
            new[] { Styled('x', style) });

        Assert.Equal(
            "{\"cols\":1,\"rows\":1,\"title\":\"\",\"cursor\":{\"row\":1,\"col\":1,\"visible\":true},\"lines\":[\n"
            + "{\"text\":\"x\",\"spans\":[{\"col\":1,\"len\":1,\"fg\":null,\"bg\":null,\"attrs\":[\"bold\",\"dim\",\"italic\",\"underline\",\"inverse\",\"strike\"]}]}\n"
            + "]}\n",
            RenderJson.Render(snapshot));
    }

    [Fact]
    public void Render_is_repeatable_and_does_not_change_snapshot_metadata_or_cells()
    {
        Cell[][] lines =
        {
            new[] { Styled('a', IndexedForeground(1)), Plain(' ') },
            new[] { Plain(0x1F600), Styled(' ', IndexedBackground(4)) }
        };
        Snapshot snapshot = new(2, 2, "unchanged", 1, 1, false, lines);
        const string expected =
            "{\"cols\":2,\"rows\":2,\"title\":\"unchanged\",\"cursor\":{\"row\":2,\"col\":2,\"visible\":false},\"lines\":[\n"
            + "{\"text\":\"a\",\"spans\":[{\"col\":1,\"len\":1,\"fg\":1,\"bg\":null,\"attrs\":[]}]},\n"
            + "{\"text\":\"😀\",\"spans\":[{\"col\":2,\"len\":1,\"fg\":null,\"bg\":4,\"attrs\":[]}]}\n"
            + "]}\n";

        string actual = RenderJson.Render(snapshot);
        Assert.Equal(expected, actual);
        Assert.Equal(actual, RenderJson.Render(snapshot));
        Assert.Equal(2, snapshot.Cols);
        Assert.Equal(2, snapshot.Rows);
        Assert.Equal("unchanged", snapshot.Title);
        Assert.Equal(1, snapshot.CursorRow);
        Assert.Equal(1, snapshot.CursorCol);
        Assert.False(snapshot.CursorVisible);
        Assert.Same(lines, snapshot.Lines);
        Assert.Same(lines[0], snapshot.Lines[0]);
        Assert.Same(lines[1], snapshot.Lines[1]);
        Assert.Equal(new[] { Styled('a', IndexedForeground(1)), Plain(' ') }, lines[0]);
        Assert.Equal(new[] { Plain(0x1F600), Styled(' ', IndexedBackground(4)) }, lines[1]);
    }

    [Fact]
    public void Render_is_culture_independent()
    {
        Snapshot snapshot = CreateSnapshot(2, 1, "culture", 0, 1, true,
            new[] { Styled('x', RgbForeground(0x00010F)), Plain(' ') });
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        const string expected =
            "{\"cols\":2,\"rows\":1,\"title\":\"culture\",\"cursor\":{\"row\":1,\"col\":2,\"visible\":true},\"lines\":[\n"
            + "{\"text\":\"x\",\"spans\":[{\"col\":1,\"len\":1,\"fg\":\"#00010f\",\"bg\":null,\"attrs\":[]}]}\n"
            + "]}\n";

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            string enOutput = RenderJson.Render(snapshot);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            string frOutput = RenderJson.Render(snapshot);

            Assert.Equal(expected, enOutput);
            Assert.Equal(enOutput, frOutput);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    private static Snapshot CreateSnapshot(
        int cols,
        int rows,
        string title,
        int cursorRow,
        int cursorCol,
        bool cursorVisible,
        params Cell[][] lines) =>
        new(cols, rows, title, cursorRow, cursorCol, cursorVisible, lines);

    private static Cell Plain(int codePoint) => new(codePoint, Style.Default);

    private static Cell Styled(int codePoint, Style style) => new(codePoint, style);

    private static Style IndexedForeground(int index) =>
        new(new Color(ColorKind.Indexed, index), Color.Default, Attr.None);

    private static Style IndexedBackground(int index) =>
        new(Color.Default, new Color(ColorKind.Indexed, index), Attr.None);

    private static Style RgbForeground(int rgb) =>
        new(new Color(ColorKind.Rgb, rgb), Color.Default, Attr.None);

    private static Style RgbBackground(int rgb) =>
        new(Color.Default, new Color(ColorKind.Rgb, rgb), Attr.None);
}
