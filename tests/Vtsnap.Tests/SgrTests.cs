namespace Vtsnap.Tests;

public class SgrTests
{
    [Fact]
    public void Empty_parameters_reset_the_entire_style()
    {
        Style initial = new(
            new Color(ColorKind.Indexed, 4),
            new Color(ColorKind.Rgb, 0x102030),
            Attr.Bold | Attr.Italic);
        SgrResult result = Sgr.Apply(initial, Array.Empty<int>());

        Assert.Equal(Style.Default, result.Style);
        Assert.False(result.Invalid);
    }

    [Fact]
    public void Omitted_parameter_is_a_reset()
    {
        Style initial = new(
            new Color(ColorKind.Indexed, 4),
            new Color(ColorKind.Indexed, 9),
            Attr.Bold | Attr.Underline);
        SgrResult result = Sgr.Apply(initial, new[] { -1 });

        Assert.Equal(Style.Default, result.Style);
        Assert.False(result.Invalid);
    }

    [Fact]
    public void Zero_resets_colors_and_attributes()
    {
        Style initial = new(
            new Color(ColorKind.Rgb, 0xabcdef),
            new Color(ColorKind.Indexed, 12),
            Attr.Bold | Attr.Dim | Attr.Strike);
        SgrResult result = Sgr.Apply(initial, new[] { 0 });

        Assert.Equal(Style.Default, result.Style);
        Assert.False(result.Invalid);
    }

    [Fact]
    public void Attribute_codes_enable_each_attribute()
    {
        SgrResult result = Sgr.Apply(Style.Default, new[] { 1, 2, 3, 4, 7, 9 });

        Assert.Equal(Attr.Bold | Attr.Dim | Attr.Italic | Attr.Underline | Attr.Inverse | Attr.Strike, result.Style.Attrs);
        Assert.False(result.Invalid);
    }

    [Fact]
    public void Reset_in_the_middle_of_a_sequence_keeps_only_later_attributes()
    {
        SgrResult result = Sgr.Apply(Style.Default, new[] { 1, 0, 4 });

        Assert.Equal(Attr.Underline, result.Style.Attrs);
        Assert.Equal(Color.Default, result.Style.Fg);
        Assert.Equal(Color.Default, result.Style.Bg);
        Assert.False(result.Invalid);
    }

    [Fact]
    public void Code_22_disables_bold_and_dim_without_clearing_other_attributes()
    {
        Style initial = new(Color.Default, Color.Default, Attr.Bold | Attr.Dim | Attr.Italic | Attr.Strike);
        SgrResult result = Sgr.Apply(initial, new[] { 1, 2, 22 });

        Assert.Equal(Attr.Italic | Attr.Strike, result.Style.Attrs);
        Assert.False(result.Invalid);
    }

    [Fact]
    public void Individual_attribute_off_codes_clear_their_matching_attributes()
    {
        Style initial = new(
            Color.Default,
            Color.Default,
            Attr.Italic | Attr.Underline | Attr.Inverse | Attr.Strike | Attr.Bold);
        SgrResult result = Sgr.Apply(initial, new[] { 23, 24, 27, 29 });

        Assert.Equal(Attr.Bold, result.Style.Attrs);
        Assert.False(result.Invalid);
    }

    [Fact]
    public void Unrecognized_attribute_code_is_silently_ignored()
    {
        Style initial = new(new Color(ColorKind.Indexed, 2), new Color(ColorKind.Indexed, 3), Attr.Bold);
        SgrResult result = Sgr.Apply(initial, new[] { 6 });

        Assert.Equal(initial, result.Style);
        Assert.False(result.Invalid);
    }

    [Theory]
    [InlineData(30, 0)]
    [InlineData(31, 1)]
    [InlineData(32, 2)]
    [InlineData(33, 3)]
    [InlineData(34, 4)]
    [InlineData(35, 5)]
    [InlineData(36, 6)]
    [InlineData(37, 7)]
    [InlineData(90, 8)]
    [InlineData(91, 9)]
    [InlineData(92, 10)]
    [InlineData(93, 11)]
    [InlineData(94, 12)]
    [InlineData(95, 13)]
    [InlineData(96, 14)]
    [InlineData(97, 15)]
    public void Basic_foreground_codes_select_the_documented_palette_index(int code, int index)
    {
        Style initial = new(new Color(ColorKind.Indexed, 23), new Color(ColorKind.Rgb, 0x102030), Attr.Dim | Attr.Strike);
        SgrResult result = Sgr.Apply(initial, new[] { code });

        Assert.Equal(new Color(ColorKind.Indexed, index), result.Style.Fg);
        Assert.Equal(initial.Bg, result.Style.Bg);
        Assert.Equal(initial.Attrs, result.Style.Attrs);
        Assert.False(result.Invalid);
    }

    [Theory]
    [InlineData(40, 0)]
    [InlineData(41, 1)]
    [InlineData(42, 2)]
    [InlineData(43, 3)]
    [InlineData(44, 4)]
    [InlineData(45, 5)]
    [InlineData(46, 6)]
    [InlineData(47, 7)]
    [InlineData(100, 8)]
    [InlineData(101, 9)]
    [InlineData(102, 10)]
    [InlineData(103, 11)]
    [InlineData(104, 12)]
    [InlineData(105, 13)]
    [InlineData(106, 14)]
    [InlineData(107, 15)]
    public void Basic_background_codes_select_the_documented_palette_index(int code, int index)
    {
        Style initial = new(new Color(ColorKind.Rgb, 0x102030), new Color(ColorKind.Indexed, 23), Attr.Dim | Attr.Strike);
        SgrResult result = Sgr.Apply(initial, new[] { code });

        Assert.Equal(initial.Fg, result.Style.Fg);
        Assert.Equal(new Color(ColorKind.Indexed, index), result.Style.Bg);
        Assert.Equal(initial.Attrs, result.Style.Attrs);
        Assert.False(result.Invalid);
    }

    [Fact]
    public void Default_color_codes_reset_only_the_selected_channels()
    {
        Style initial = new(
            new Color(ColorKind.Indexed, 5),
            new Color(ColorKind.Rgb, 0x010203),
            Attr.Bold | Attr.Underline);
        SgrResult result = Sgr.Apply(initial, new[] { 39, 49 });

        Assert.Equal(Color.Default, result.Style.Fg);
        Assert.Equal(Color.Default, result.Style.Bg);
        Assert.Equal(initial.Attrs, result.Style.Attrs);
        Assert.False(result.Invalid);
    }

    [Fact]
    public void Indexed_extended_colors_accept_both_inclusive_boundaries()
    {
        SgrResult result = Sgr.Apply(Style.Default, new[] { 38, 5, 0, 48, 5, 255 });

        Assert.Equal(new Color(ColorKind.Indexed, 0), result.Style.Fg);
        Assert.Equal(new Color(ColorKind.Indexed, 255), result.Style.Bg);
        Assert.False(result.Invalid);
    }

    [Fact]
    public void Rgb_extended_colors_preserve_zero_and_255_components()
    {
        SgrResult result = Sgr.Apply(Style.Default, new[] { 38, 2, 0, 128, 255, 48, 2, 255, 0, 1 });

        Assert.Equal(new Color(ColorKind.Rgb, 0x0080ff), result.Style.Fg);
        Assert.Equal(new Color(ColorKind.Rgb, 0xff0001), result.Style.Bg);
        Assert.False(result.Invalid);
    }

    [Fact]
    public void Invalid_foreground_index_retains_prior_effects_and_stops_before_reset()
    {
        Style initial = new(
            new Color(ColorKind.Indexed, 14),
            new Color(ColorKind.Rgb, 0x123456),
            Attr.Italic);
        SgrResult result = Sgr.Apply(initial, new[] { 1, 38, 5, 300, 0 });

        Assert.True(result.Invalid);
        Assert.Equal(initial.Fg, result.Style.Fg);
        Assert.Equal(initial.Bg, result.Style.Bg);
        Assert.Equal(Attr.Bold | Attr.Italic, result.Style.Attrs);
    }

    [Fact]
    public void Cut_off_background_rgb_does_not_partially_replace_the_color()
    {
        Style initial = new(
            new Color(ColorKind.Rgb, 0x123456),
            new Color(ColorKind.Indexed, 17),
            Attr.Dim);
        SgrResult result = Sgr.Apply(initial, new[] { 48, 2, 1, 2 });

        Assert.True(result.Invalid);
        Assert.Equal(initial, result.Style);
    }

    [Fact]
    public void Cut_off_foreground_extended_color_is_invalid_without_changing_style()
    {
        Style initial = new(
            new Color(ColorKind.Indexed, 7),
            new Color(ColorKind.Indexed, 9),
            Attr.Strike);
        SgrResult result = Sgr.Apply(initial, new[] { 38 });

        Assert.True(result.Invalid);
        Assert.Equal(initial, result.Style);
    }

    [Fact]
    public void Unsupported_foreground_extended_selector_is_invalid_without_changing_style()
    {
        Style initial = new(
            new Color(ColorKind.Indexed, 7),
            new Color(ColorKind.Indexed, 9),
            Attr.Strike);
        SgrResult result = Sgr.Apply(initial, new[] { 38, 9 });

        Assert.True(result.Invalid);
        Assert.Equal(initial, result.Style);
    }

    [Fact]
    public void Negative_indexed_foreground_component_is_invalid()
    {
        SgrResult result = Sgr.Apply(Style.Default, new[] { 38, 5, -1 });

        Assert.True(result.Invalid);
        Assert.Equal(Style.Default, result.Style);
    }

    [Fact]
    public void Indexed_background_component_above_255_is_invalid()
    {
        Style initial = new(Color.Default, new Color(ColorKind.Rgb, 0x000001), Attr.Bold);
        SgrResult result = Sgr.Apply(initial, new[] { 48, 5, 256 });

        Assert.True(result.Invalid);
        Assert.Equal(initial, result.Style);
    }

    [Fact]
    public void Rgb_foreground_component_above_255_is_invalid()
    {
        SgrResult result = Sgr.Apply(Style.Default, new[] { 38, 2, 0, 256, 0 });

        Assert.True(result.Invalid);
        Assert.Equal(Style.Default, result.Style);
    }

    [Fact]
    public void Missing_background_rgb_components_are_invalid_without_partial_color()
    {
        Style initial = new(new Color(ColorKind.Indexed, 3), new Color(ColorKind.Indexed, 4), Attr.Underline);
        SgrResult result = Sgr.Apply(initial, new[] { 48, 2, 0, 128 });

        Assert.True(result.Invalid);
        Assert.Equal(initial, result.Style);
    }

    [Fact]
    public void Unsupported_background_extended_selector_is_invalid_without_changing_style()
    {
        Style initial = new(new Color(ColorKind.Indexed, 3), new Color(ColorKind.Indexed, 4), Attr.Underline);
        SgrResult result = Sgr.Apply(initial, new[] { 48, 9 });

        Assert.True(result.Invalid);
        Assert.Equal(initial, result.Style);
    }

    [Fact]
    public void Maximum_parser_saturated_component_is_out_of_range()
    {
        SgrResult result = Sgr.Apply(Style.Default, new[] { 38, 5, 65535 });

        Assert.True(result.Invalid);
        Assert.Equal(Style.Default, result.Style);
    }
}
