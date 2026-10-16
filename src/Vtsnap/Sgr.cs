namespace Vtsnap;

public readonly record struct SgrResult(Style Style, bool Invalid);

public static class Sgr
{
    public static SgrResult Apply(Style style, IReadOnlyList<int> parms)
    {
        ArgumentNullException.ThrowIfNull(parms);

        if (parms.Count == 0)
        {
            return new SgrResult(Style.Default, Invalid: false);
        }

        bool invalid = false;
        int index = 0;
        while (index < parms.Count)
        {
            int parameter = parms[index++];
            if (parameter == -1)
            {
                parameter = 0;
            }

            switch (parameter)
            {
                case 0:
                    style = Style.Default;
                    break;
                case 1:
                    style = style with { Attrs = style.Attrs | Attr.Bold };
                    break;
                case 2:
                    style = style with { Attrs = style.Attrs | Attr.Dim };
                    break;
                case 3:
                    style = style with { Attrs = style.Attrs | Attr.Italic };
                    break;
                case 4:
                    style = style with { Attrs = style.Attrs | Attr.Underline };
                    break;
                case 7:
                    style = style with { Attrs = style.Attrs | Attr.Inverse };
                    break;
                case 9:
                    style = style with { Attrs = style.Attrs | Attr.Strike };
                    break;
                case 22:
                    style = style with { Attrs = style.Attrs & ~(Attr.Bold | Attr.Dim) };
                    break;
                case 23:
                    style = style with { Attrs = style.Attrs & ~Attr.Italic };
                    break;
                case 24:
                    style = style with { Attrs = style.Attrs & ~Attr.Underline };
                    break;
                case 27:
                    style = style with { Attrs = style.Attrs & ~Attr.Inverse };
                    break;
                case 29:
                    style = style with { Attrs = style.Attrs & ~Attr.Strike };
                    break;
                case >= 30 and <= 37:
                    style = style with { Fg = new Color(ColorKind.Indexed, parameter - 30) };
                    break;
                case >= 40 and <= 47:
                    style = style with { Bg = new Color(ColorKind.Indexed, parameter - 40) };
                    break;
                case >= 90 and <= 97:
                    style = style with { Fg = new Color(ColorKind.Indexed, parameter - 90 + 8) };
                    break;
                case >= 100 and <= 107:
                    style = style with { Bg = new Color(ColorKind.Indexed, parameter - 100 + 8) };
                    break;
                case 39:
                    style = style with { Fg = Color.Default };
                    break;
                case 49:
                    style = style with { Bg = Color.Default };
                    break;
                case 38:
                case 48:
                    if (!TryReadExtendedColor(parms, ref index, out Color color))
                    {
                        invalid = true;
                        return new SgrResult(style, invalid);
                    }

                    style = parameter == 38
                        ? style with { Fg = color }
                        : style with { Bg = color };
                    break;
            }
        }

        return new SgrResult(style, invalid);
    }

    private static bool TryReadExtendedColor(
        IReadOnlyList<int> parms,
        ref int index,
        out Color color)
    {
        color = Color.Default;
        if (index >= parms.Count)
        {
            return false;
        }

        int selector = parms[index++];
        if (selector == 5)
        {
            if (index >= parms.Count)
            {
                return false;
            }

            int value = parms[index++];
            if (value is < 0 or > 255)
            {
                return false;
            }

            color = new Color(ColorKind.Indexed, value);
            return true;
        }

        if (selector == 2)
        {
            if (parms.Count - index < 3)
            {
                return false;
            }

            int red = parms[index++];
            int green = parms[index++];
            int blue = parms[index++];
            if (red is < 0 or > 255
                || green is < 0 or > 255
                || blue is < 0 or > 255)
            {
                return false;
            }

            color = new Color(ColorKind.Rgb, (red << 16) | (green << 8) | blue);
            return true;
        }

        return false;
    }
}
