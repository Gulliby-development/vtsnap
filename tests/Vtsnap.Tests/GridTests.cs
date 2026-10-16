namespace Vtsnap.Tests;

public class GridTests
{
    [Fact]
    public void Model_defaults_and_enum_values_match_the_public_contract()
    {
        Assert.Equal(new Color(ColorKind.Default, 0), Color.Default);
        Assert.Equal(new Style(Color.Default, Color.Default, Attr.None), Style.Default);
        Assert.Equal(new Cell(32, Style.Default), Cell.Blank);
        Assert.Equal(0, (int)ColorKind.Default);
        Assert.Equal(1, (int)ColorKind.Indexed);
        Assert.Equal(2, (int)ColorKind.Rgb);
        Assert.Equal(0, (int)Attr.None);
        Assert.Equal(1, (int)Attr.Bold);
        Assert.Equal(2, (int)Attr.Dim);
        Assert.Equal(4, (int)Attr.Italic);
        Assert.Equal(8, (int)Attr.Underline);
        Assert.Equal(16, (int)Attr.Inverse);
        Assert.Equal(32, (int)Attr.Strike);
        Assert.Equal(Attr.Bold | Attr.Italic | Attr.Strike, Attr.Bold | Attr.Italic | Attr.Strike);
    }

    [Fact]
    public void Model_record_structs_use_value_equality()
    {
        Assert.Equal(new Color(ColorKind.Rgb, 0x123456), new Color(ColorKind.Rgb, 0x123456));
        Assert.NotEqual(new Color(ColorKind.Indexed, 1), new Color(ColorKind.Indexed, 2));
        Assert.Equal(
            new Style(new Color(ColorKind.Indexed, 4), Color.Default, Attr.Bold),
            new Style(new Color(ColorKind.Indexed, 4), Color.Default, Attr.Bold));
        Assert.Equal(new Cell('Q', Style.Default), new Cell('Q', Style.Default));
        Assert.NotEqual(new Cell('Q', Style.Default), new Cell('R', Style.Default));
    }

    [Fact]
    public void Snapshot_keeps_scrollback_before_screen_rows_and_screen_relative_cursor()
    {
        Cell[] scrollback = { C('s') };
        Cell[] screenTop = { C('t') };
        Cell[] screenBottom = { C('b') };
        var lines = new Cell[][] { scrollback, screenTop, screenBottom };
        var snapshot = new Snapshot(1, 2, "title", 1, 0, true, lines);

        Assert.Equal(1, snapshot.Cols);
        Assert.Equal(2, snapshot.Rows);
        Assert.Equal("title", snapshot.Title);
        Assert.Equal(1, snapshot.CursorRow);
        Assert.Equal(0, snapshot.CursorCol);
        Assert.True(snapshot.CursorVisible);
        Assert.Same(scrollback, snapshot.Lines[0]);
        Assert.Same(screenTop, snapshot.Lines[1]);
        Assert.Same(screenBottom, snapshot.Lines[2]);
    }

    [Fact]
    public void CliError_preserves_its_message()
    {
        var error = new CliError("bad input");

        Assert.Equal("bad input", error.Message);
    }

    [Fact]
    public void Grid_constructs_blank_one_by_one_and_maximum_grids()
    {
        var single = new Grid(1, 1);
        Assert.Equal(1, single.Cols);
        Assert.Equal(1, single.Rows);
        Assert.Equal(Cell.Blank, single[0, 0]);

        var maximum = new Grid(500, 500);
        Assert.Equal(500, maximum.Cols);
        Assert.Equal(500, maximum.Rows);
        Assert.Equal(Cell.Blank, maximum[0, 0]);
        Assert.Equal(Cell.Blank, maximum[499, 499]);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(501, 1)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 501)]
    public void Grid_rejects_dimensions_outside_one_through_five_hundred(int cols, int rows)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Grid(cols, rows));
    }

    [Fact]
    public void Indexer_reads_and_writes_exact_cells_at_each_corner()
    {
        var grid = new Grid(2, 2);
        Cell topLeft = new('A', S(1, 2, Attr.Bold));
        Cell bottomRight = new('Z', S(3, 4, Attr.Italic));

        grid[0, 0] = topLeft;
        grid[1, 1] = bottomRight;

        Assert.Equal(topLeft, grid[0, 0]);
        Assert.Equal(Cell.Blank, grid[0, 1]);
        Assert.Equal(Cell.Blank, grid[1, 0]);
        Assert.Equal(bottomRight, grid[1, 1]);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(1, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 1)]
    public void Indexer_rejects_negative_and_upper_bound_coordinates(int row, int col)
    {
        var grid = new Grid(1, 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => grid[row, col]);
        Assert.Throws<ArgumentOutOfRangeException>(() => grid[row, col] = Cell.Blank);
    }

    [Fact]
    public void SetRow_copies_input_and_GetRow_returns_an_independent_array()
    {
        var grid = new Grid(3, 1);
        Cell[] input = { C('A'), C('B'), C('C') };
        grid.SetRow(0, input);
        input[0] = C('x');

        Cell[] result = grid.GetRow(0);
        Assert.Equal(new[] { C('A'), C('B'), C('C') }, result);
        Assert.NotSame(input, result);

        result[1] = C('y');
        Assert.Equal(C('B'), grid[0, 1]);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void GetRow_and_SetRow_reject_invalid_rows(int row)
    {
        var grid = new Grid(2, 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => grid.GetRow(row));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.SetRow(row, new[] { C('A'), C('B') }));
    }

    [Fact]
    public void SetRow_rejects_a_row_with_the_wrong_width()
    {
        var grid = new Grid(2, 1);

        Assert.Throws<ArgumentException>(() => grid.SetRow(0, new[] { C('A') }));
        Assert.Throws<ArgumentException>(() => grid.SetRow(0, new[] { C('A'), C('B'), C('C') }));
    }

    [Fact]
    public void SnapshotRows_and_Copy_own_independent_rows_and_cells()
    {
        var grid = new Grid(2, 2);
        grid.SetRow(0, new[] { C('A'), C('B') });
        grid.SetRow(1, new[] { C('C'), C('D') });

        Cell[][] snapshot = grid.SnapshotRows();
        Grid copy = grid.Copy();
        Assert.NotSame(snapshot[0], snapshot[1]);
        Assert.NotSame(snapshot[0], grid.GetRow(0));
        Assert.Equal(new[] { C('A'), C('B') }, snapshot[0]);
        Assert.Equal(new[] { C('C'), C('D') }, snapshot[1]);

        snapshot[0][0] = C('x');
        snapshot[1] = new[] { C('y'), C('z') };
        copy[0, 1] = C('q');
        grid[1, 1] = C('w');

        Assert.Equal(C('A'), grid[0, 0]);
        Assert.Equal(C('B'), grid[0, 1]);
        Assert.Equal(C('D'), copy[1, 1]);
        Assert.Equal(C('B'), snapshot[0][1]);
    }

    [Fact]
    public void Clear_fills_every_cell_with_the_exact_styled_cell()
    {
        var grid = new Grid(2, 2);
        Cell fill = new(' ', S(2, 9, Attr.Dim | Attr.Underline));

        grid.Clear(fill);

        Assert.Equal(fill, grid[0, 0]);
        Assert.Equal(fill, grid[0, 1]);
        Assert.Equal(fill, grid[1, 0]);
        Assert.Equal(fill, grid[1, 1]);
    }

    [Fact]
    public void FillRow_fills_half_open_span_and_accepts_empty_span_at_end()
    {
        var grid = new Grid(4, 1);
        grid.SetRow(0, new[] { C('A'), C('B'), C('C'), C('D') });
        Cell fill = new('x', S(1, 6, Attr.Bold));

        grid.FillRow(0, 1, 2, fill);
        Assert.Equal(new[] { C('A'), fill, fill, C('D') }, grid.GetRow(0));

        grid.FillRow(0, 4, 0, C('z'));
        Assert.Equal(new[] { C('A'), fill, fill, C('D') }, grid.GetRow(0));
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 5, 0)]
    [InlineData(0, 3, 2)]
    [InlineData(0, 1, -1)]
    [InlineData(0, 1, 2147483647)]
    public void FillRow_rejects_invalid_rows_and_overflow_safe_spans(int row, int start, int count)
    {
        var grid = new Grid(4, 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => grid.FillRow(row, start, count, C('x')));
    }

    [Fact]
    public void FillRegion_includes_both_endpoints_and_leaves_other_rows_unchanged()
    {
        var grid = new Grid(2, 4);
        grid.SetRow(0, new[] { C('A'), C('a') });
        grid.SetRow(1, new[] { C('B'), C('b') });
        grid.SetRow(2, new[] { C('C'), C('c') });
        grid.SetRow(3, new[] { C('D'), C('d') });
        Cell fill = new('x', S(5, 8, Attr.Inverse));

        grid.FillRegion(1, 2, fill);

        Assert.Equal(new[] { C('A'), C('a') }, grid.GetRow(0));
        Assert.Equal(new[] { fill, fill }, grid.GetRow(1));
        Assert.Equal(new[] { fill, fill }, grid.GetRow(2));
        Assert.Equal(new[] { C('D'), C('d') }, grid.GetRow(3));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 4)]
    [InlineData(2, 1)]
    public void FillRegion_rejects_invalid_or_reversed_endpoints(int top, int bottom)
    {
        var grid = new Grid(1, 3);

        Assert.Throws<ArgumentOutOfRangeException>(() => grid.FillRegion(top, bottom, C('x')));
    }

    [Fact]
    public void InsertCells_shifts_a_suffix_right_and_fills_the_inserted_cells()
    {
        var grid = new Grid(5, 1);
        grid.SetRow(0, new[] { C('A'), C('B'), C('C'), C('D'), C('E') });
        Cell fill = new('x', S(3, 7, Attr.Strike));

        grid.InsertCells(0, 1, 2, fill);

        Assert.Equal(new[] { C('A'), fill, fill, C('B'), C('C') }, grid.GetRow(0));
    }

    [Fact]
    public void InsertCells_clamps_large_counts_and_zero_count_is_a_no_op()
    {
        var grid = new Grid(4, 1);
        grid.SetRow(0, new[] { C('A'), C('B'), C('C'), C('D') });
        Cell fill = C('x');

        grid.InsertCells(0, 2, 0, fill);
        Assert.Equal(new[] { C('A'), C('B'), C('C'), C('D') }, grid.GetRow(0));

        grid.InsertCells(0, 2, int.MaxValue, fill);
        Assert.Equal(new[] { C('A'), C('B'), fill, fill }, grid.GetRow(0));
    }

    [Fact]
    public void DeleteCells_shifts_a_suffix_left_and_fills_the_exposed_cells()
    {
        var grid = new Grid(5, 1);
        grid.SetRow(0, new[] { C('A'), C('B'), C('C'), C('D'), C('E') });
        Cell fill = new('x', S(3, 7, Attr.Strike));

        grid.DeleteCells(0, 1, 2, fill);

        Assert.Equal(new[] { C('A'), C('D'), C('E'), fill, fill }, grid.GetRow(0));
    }

    [Fact]
    public void DeleteCells_clamps_large_counts_and_zero_count_is_a_no_op()
    {
        var grid = new Grid(4, 1);
        grid.SetRow(0, new[] { C('A'), C('B'), C('C'), C('D') });
        Cell fill = C('x');

        grid.DeleteCells(0, 1, 0, fill);
        Assert.Equal(new[] { C('A'), C('B'), C('C'), C('D') }, grid.GetRow(0));

        grid.DeleteCells(0, 2, int.MaxValue, fill);
        Assert.Equal(new[] { C('A'), C('B'), fill, fill }, grid.GetRow(0));
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 1, -1)]
    public void Cell_shifts_validate_coordinates_and_negative_counts_even_for_zero_work(
        int row,
        int col,
        int count)
    {
        var grid = new Grid(1, 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => grid.InsertCells(row, col, count, Cell.Blank));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.DeleteCells(row, col, count, Cell.Blank));
    }

    [Fact]
    public void ScrollUp_returns_removed_rows_in_order_and_changes_only_the_region()
    {
        var grid = new Grid(1, 4);
        grid.SetRow(0, new[] { C('A') });
        grid.SetRow(1, new[] { C('B') });
        grid.SetRow(2, new[] { C('C') });
        grid.SetRow(3, new[] { C('D') });
        Cell fill = new('x', S(1, 5, Attr.Dim));

        Cell[][] removed = grid.ScrollUp(1, 3, 2, fill);

        Assert.Equal(new[] { C('B') }, removed[0]);
        Assert.Equal(new[] { C('C') }, removed[1]);
        Assert.Equal(C('A'), grid[0, 0]);
        Assert.Equal(C('D'), grid[1, 0]);
        Assert.Equal(fill, grid[2, 0]);
        Assert.Equal(fill, grid[3, 0]);
        removed[0][0] = C('x');
        Assert.Equal(C('D'), grid[1, 0]);
    }

    [Fact]
    public void ScrollDown_returns_removed_rows_in_original_top_to_bottom_order()
    {
        var grid = new Grid(1, 4);
        grid.SetRow(0, new[] { C('A') });
        grid.SetRow(1, new[] { C('B') });
        grid.SetRow(2, new[] { C('C') });
        grid.SetRow(3, new[] { C('D') });
        Cell fill = new('x', S(1, 5, Attr.Dim));

        Cell[][] removed = grid.ScrollDown(0, 3, 2, fill);

        Assert.Equal(new[] { C('C') }, removed[0]);
        Assert.Equal(new[] { C('D') }, removed[1]);
        Assert.Equal(fill, grid[0, 0]);
        Assert.Equal(fill, grid[1, 0]);
        Assert.Equal(C('A'), grid[2, 0]);
        Assert.Equal(C('B'), grid[3, 0]);
        removed[1][0] = C('x');
        Assert.Equal(C('B'), grid[3, 0]);
    }

    [Fact]
    public void Scrolls_clamp_to_region_height_and_accept_zero_counts()
    {
        var grid = new Grid(1, 2);
        grid.SetRow(0, new[] { C('A') });
        grid.SetRow(1, new[] { C('B') });

        Assert.Empty(grid.ScrollUp(0, 1, 0, C('x')));
        Assert.Equal(C('A'), grid[0, 0]);
        Assert.Equal(C('B'), grid[1, 0]);

        Cell[][] removed = grid.ScrollDown(0, 1, int.MaxValue, C('x'));
        Assert.Equal(new[] { C('A') }, removed[0]);
        Assert.Equal(new[] { C('B') }, removed[1]);
        Assert.Equal(C('x'), grid[0, 0]);
        Assert.Equal(C('x'), grid[1, 0]);
    }

    [Fact]
    public void InsertRows_shifts_only_the_selected_region_down()
    {
        var grid = new Grid(1, 4);
        grid.SetRow(0, new[] { C('A') });
        grid.SetRow(1, new[] { C('B') });
        grid.SetRow(2, new[] { C('C') });
        grid.SetRow(3, new[] { C('D') });

        grid.InsertRows(1, 3, 1, C('x'));

        Assert.Equal(C('A'), grid[0, 0]);
        Assert.Equal(C('x'), grid[1, 0]);
        Assert.Equal(C('B'), grid[2, 0]);
        Assert.Equal(C('C'), grid[3, 0]);
    }

    [Fact]
    public void DeleteRows_shifts_only_the_selected_region_up()
    {
        var grid = new Grid(1, 4);
        grid.SetRow(0, new[] { C('A') });
        grid.SetRow(1, new[] { C('B') });
        grid.SetRow(2, new[] { C('C') });
        grid.SetRow(3, new[] { C('D') });

        grid.DeleteRows(1, 3, 1, C('x'));

        Assert.Equal(C('A'), grid[0, 0]);
        Assert.Equal(C('C'), grid[1, 0]);
        Assert.Equal(C('D'), grid[2, 0]);
        Assert.Equal(C('x'), grid[3, 0]);
    }

    [Fact]
    public void Row_shifts_clamp_excessive_counts_and_zero_counts_still_validate()
    {
        var grid = new Grid(1, 3);
        grid.SetRow(0, new[] { C('A') });
        grid.SetRow(1, new[] { C('B') });
        grid.SetRow(2, new[] { C('C') });

        grid.InsertRows(1, 2, 0, C('x'));
        Assert.Equal(C('B'), grid[1, 0]);
        grid.InsertRows(1, 2, int.MaxValue, C('x'));
        Assert.Equal(C('A'), grid[0, 0]);
        Assert.Equal(C('x'), grid[1, 0]);
        Assert.Equal(C('x'), grid[2, 0]);

        Assert.Throws<ArgumentOutOfRangeException>(() => grid.DeleteRows(1, 0, 0, C('x')));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.InsertRows(0, 2, -1, C('x')));
    }

    [Fact]
    public void Row_shifts_reject_invalid_row_endpoints()
    {
        var grid = new Grid(1, 2);

        Assert.Throws<ArgumentOutOfRangeException>(() => grid.InsertRows(-1, 1, 0, C('x')));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.DeleteRows(0, 2, 0, C('x')));
    }

    [Fact]
    public void Blank_keeps_only_background_as_a_plain_space()
    {
        Style source = S(3, 17, Attr.Bold | Attr.Inverse | Attr.Strike);
        Cell expected = new(' ', new Style(Color.Default, new Color(ColorKind.Indexed, 17), Attr.None));

        Assert.Equal(expected, Grid.Blank(source));
    }

    private static Cell C(char value) => new(value, Style.Default);

    private static Style S(int foreground, int background, Attr attrs) =>
        new(new Color(ColorKind.Indexed, foreground), new Color(ColorKind.Indexed, background), attrs);
}
