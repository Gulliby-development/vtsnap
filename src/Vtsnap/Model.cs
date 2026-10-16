namespace Vtsnap;

public enum ColorKind
{
    Default,
    Indexed,
    Rgb
}

public readonly record struct Color(ColorKind Kind, int Value)
{
    public static Color Default => new(ColorKind.Default, 0);
}

[Flags]
public enum Attr
{
    None = 0,
    Bold = 1,
    Dim = 2,
    Italic = 4,
    Underline = 8,
    Inverse = 16,
    Strike = 32
}

public readonly record struct Style(Color Fg, Color Bg, Attr Attrs)
{
    public static Style Default => new(Color.Default, Color.Default, Attr.None);
}

public readonly record struct Cell(int Char, Style Style)
{
    public static Cell Blank => new(32, Style.Default);
}

public sealed record Snapshot(
    int Cols,
    int Rows,
    string Title,
    int CursorRow,
    int CursorCol,
    bool CursorVisible,
    IReadOnlyList<Cell[]> Lines);

public sealed record Finding(int Pos, string Code, string Message);

public sealed record Result(string Text, int Code);

public class CliError : Exception
{
    public CliError(string message) : base(message)
    {
    }
}
