namespace Vtsnap;

/// <summary>
/// A mutable, zero-based grid of cells. Rows returned from or supplied to this
/// class are copied so callers cannot mutate grid storage through an alias.
/// </summary>
public sealed class Grid
{
    private readonly Cell[][] _rows;

    /// <summary>Creates a blank grid whose dimensions are each between 1 and 500.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is outside 1..500.</exception>
    public Grid(int cols, int rows)
    {
        if (cols is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(cols));
        }

        if (rows is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        Cols = cols;
        Rows = rows;
        _rows = new Cell[rows][];
        for (int row = 0; row < rows; row++)
        {
            _rows[row] = new Cell[cols];
            Array.Fill(_rows[row], Cell.Blank);
        }
    }

    /// <summary>Gets the number of columns.</summary>
    public int Cols { get; }

    /// <summary>Gets the number of rows.</summary>
    public int Rows { get; }

    /// <summary>Gets or sets the cell at a zero-based row and column.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate is outside the grid.</exception>
    public Cell this[int row, int col]
    {
        get
        {
            ValidateRow(row);
            ValidateColumn(col);
            return _rows[row][col];
        }
        set
        {
            ValidateRow(row);
            ValidateColumn(col);
            _rows[row][col] = value;
        }
    }

    /// <summary>Returns an independent copy of a zero-based row.</summary>
    public Cell[] GetRow(int row)
    {
        ValidateRow(row);
        return (Cell[])_rows[row].Clone();
    }

    /// <summary>Replaces a row with a copy of the supplied cells.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The row is outside the grid.</exception>
    /// <exception cref="ArgumentException">The supplied row has the wrong width.</exception>
    public void SetRow(int row, Cell[] cells)
    {
        ValidateRow(row);
        ArgumentNullException.ThrowIfNull(cells);
        if (cells.Length != Cols)
        {
            throw new ArgumentException("The row width must match the grid.", nameof(cells));
        }

        _rows[row] = (Cell[])cells.Clone();
    }

    /// <summary>Returns an independent copy of every row.</summary>
    public Cell[][] SnapshotRows()
    {
        var copy = new Cell[Rows][];
        for (int row = 0; row < Rows; row++)
        {
            copy[row] = (Cell[])_rows[row].Clone();
        }

        return copy;
    }

    /// <summary>Creates a grid with the same dimensions and independent cell storage.</summary>
    public Grid Copy()
    {
        var copy = new Grid(Cols, Rows);
        for (int row = 0; row < Rows; row++)
        {
            Array.Copy(_rows[row], copy._rows[row], Cols);
        }

        return copy;
    }

    /// <summary>Fills every cell with the exact supplied cell.</summary>
    public void Clear(Cell fill)
    {
        for (int row = 0; row < Rows; row++)
        {
            Array.Fill(_rows[row], fill);
        }
    }

    /// <summary>
    /// Fills a half-open span of a zero-based row. An empty span may start at
    /// <see cref="Cols"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The row or span is invalid.</exception>
    public void FillRow(int row, int startCol, int count, Cell fill)
    {
        ValidateRow(row);
        if (startCol < 0 || startCol > Cols)
        {
            throw new ArgumentOutOfRangeException(nameof(startCol));
        }

        if (count < 0 || count > Cols - startCol)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        Array.Fill(_rows[row], fill, startCol, count);
    }

    /// <summary>Fills the inclusive zero-based row range.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The range is invalid or reversed.</exception>
    public void FillRegion(int top, int bottom, Cell fill)
    {
        ValidateRegion(top, bottom);
        for (int row = top; row <= bottom; row++)
        {
            Array.Fill(_rows[row], fill);
        }
    }

    /// <summary>Inserts cells at a zero-based column, shifting cells right.</summary>
    /// <remarks>The count is clamped to the remaining columns; displaced cells are discarded. A zero count is a no-op after validation.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The row or column is invalid, or count is negative.</exception>
    public void InsertCells(int row, int col, int count, Cell fill)
    {
        ValidateCellShift(row, col, count);
        int actualCount = Math.Min(count, Cols - col);
        if (actualCount == 0)
        {
            return;
        }

        Cell[] cells = _rows[row];
        Array.Copy(cells, col, cells, col + actualCount, Cols - col - actualCount);
        Array.Fill(cells, fill, col, actualCount);
    }

    /// <summary>Deletes cells at a zero-based column, shifting cells left.</summary>
    /// <remarks>The count is clamped to the remaining columns; exposed cells receive fill. A zero count is a no-op after validation.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The row or column is invalid, or count is negative.</exception>
    public void DeleteCells(int row, int col, int count, Cell fill)
    {
        ValidateCellShift(row, col, count);
        int actualCount = Math.Min(count, Cols - col);
        if (actualCount == 0)
        {
            return;
        }

        Cell[] cells = _rows[row];
        Array.Copy(cells, col + actualCount, cells, col, Cols - col - actualCount);
        Array.Fill(cells, fill, Cols - actualCount, actualCount);
    }

    /// <summary>Scrolls an inclusive zero-based region up and returns removed rows.</summary>
    /// <remarks>Count is clamped to the region height. Removed rows are independent copies in their original top-to-bottom order.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The region is invalid or reversed, or count is negative.</exception>
    public Cell[][] ScrollUp(int top, int bottom, int count, Cell fill)
    {
        ValidateShiftRegion(top, bottom, count);
        int height = bottom - top + 1;
        int actualCount = Math.Min(count, height);
        var removed = new Cell[actualCount][];
        for (int i = 0; i < actualCount; i++)
        {
            removed[i] = (Cell[])_rows[top + i].Clone();
        }

        if (actualCount == 0)
        {
            return removed;
        }

        for (int row = top; row <= bottom - actualCount; row++)
        {
            Array.Copy(_rows[row + actualCount], _rows[row], Cols);
        }

        for (int row = bottom - actualCount + 1; row <= bottom; row++)
        {
            Array.Fill(_rows[row], fill);
        }

        return removed;
    }

    /// <summary>Scrolls an inclusive zero-based region down and returns removed rows.</summary>
    /// <remarks>Count is clamped to the region height. Removed rows are independent copies in their original top-to-bottom order.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The region is invalid or reversed, or count is negative.</exception>
    public Cell[][] ScrollDown(int top, int bottom, int count, Cell fill)
    {
        ValidateShiftRegion(top, bottom, count);
        int height = bottom - top + 1;
        int actualCount = Math.Min(count, height);
        var removed = new Cell[actualCount][];
        for (int i = 0; i < actualCount; i++)
        {
            removed[i] = (Cell[])_rows[bottom - actualCount + 1 + i].Clone();
        }

        if (actualCount == 0)
        {
            return removed;
        }

        for (int row = bottom; row >= top + actualCount; row--)
        {
            Array.Copy(_rows[row - actualCount], _rows[row], Cols);
        }

        for (int row = top; row < top + actualCount; row++)
        {
            Array.Fill(_rows[row], fill);
        }

        return removed;
    }

    /// <summary>Inserts rows at row within the inclusive zero-based row..bottom region.</summary>
    /// <remarks>The count is clamped to the region height; displaced bottom rows are discarded. A zero count is a no-op after validation.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The region is invalid or reversed, or count is negative.</exception>
    public void InsertRows(int row, int bottom, int count, Cell fill)
    {
        ValidateShiftRegion(row, bottom, count);
        int height = bottom - row + 1;
        int actualCount = Math.Min(count, height);
        if (actualCount == 0)
        {
            return;
        }

        for (int target = bottom; target >= row + actualCount; target--)
        {
            Array.Copy(_rows[target - actualCount], _rows[target], Cols);
        }

        for (int target = row; target < row + actualCount; target++)
        {
            Array.Fill(_rows[target], fill);
        }
    }

    /// <summary>Deletes rows at row within the inclusive zero-based row..bottom region.</summary>
    /// <remarks>The count is clamped to the region height; exposed bottom rows receive fill. A zero count is a no-op after validation.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The region is invalid or reversed, or count is negative.</exception>
    public void DeleteRows(int row, int bottom, int count, Cell fill)
    {
        ValidateShiftRegion(row, bottom, count);
        int height = bottom - row + 1;
        int actualCount = Math.Min(count, height);
        if (actualCount == 0)
        {
            return;
        }

        for (int target = row; target <= bottom - actualCount; target++)
        {
            Array.Copy(_rows[target + actualCount], _rows[target], Cols);
        }

        for (int target = bottom - actualCount + 1; target <= bottom; target++)
        {
            Array.Fill(_rows[target], fill);
        }
    }

    /// <summary>Creates a blank cell preserving only the style's background color.</summary>
    public static Cell Blank(Style style) =>
        new(32, new Style(Color.Default, style.Bg, Attr.None));

    private void ValidateRow(int row)
    {
        if ((uint)row >= (uint)Rows)
        {
            throw new ArgumentOutOfRangeException(nameof(row));
        }
    }

    private void ValidateColumn(int col)
    {
        if ((uint)col >= (uint)Cols)
        {
            throw new ArgumentOutOfRangeException(nameof(col));
        }
    }

    private void ValidateRegion(int top, int bottom)
    {
        ValidateRow(top);
        ValidateRow(bottom);
        if (top > bottom)
        {
            throw new ArgumentOutOfRangeException(nameof(top), "The region cannot be reversed.");
        }
    }

    private void ValidateCellShift(int row, int col, int count)
    {
        ValidateRow(row);
        ValidateColumn(col);
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }
    }

    private void ValidateShiftRegion(int top, int bottom, int count)
    {
        ValidateRegion(top, bottom);
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }
    }
}
