using System.Globalization;
using System.Text;

namespace Vtsnap;

public static class Text
{
    private const ulong MicrosPerSecond = 1_000_000;
    private const ulong MaxWholeSeconds = (ulong)long.MaxValue / MicrosPerSecond;
    private const ulong MaxRemainingMicros = (ulong)long.MaxValue % MicrosPerSecond;

    public static string Plural(int n, string one, string many)
    {
        ArgumentNullException.ThrowIfNull(one);
        ArgumentNullException.ThrowIfNull(many);
        return n.ToString(CultureInfo.InvariantCulture) + " " + (n == 1 ? one : many);
    }

    public static string Seconds(long micros)
    {
        bool negative = micros < 0;
        ulong magnitude = negative
            ? (ulong)(-(micros + 1)) + 1
            : (ulong)micros;
        ulong wholeSeconds = magnitude / MicrosPerSecond;
        ulong milliseconds = magnitude % MicrosPerSecond / 1_000;
        string result = wholeSeconds.ToString(CultureInfo.InvariantCulture)
            + "."
            + milliseconds.ToString("D3", CultureInfo.InvariantCulture);
        return negative ? "-" + result : result;
    }

    public static long? ParseSeconds(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (s.Length == 0)
        {
            return null;
        }

        ulong wholeSeconds = 0;
        bool wholeOverflow = false;
        int index = 0;
        int wholeDigits = 0;

        while (index < s.Length && IsAsciiDigit(s[index]))
        {
            int digit = s[index] - '0';
            wholeDigits++;
            if (!wholeOverflow)
            {
                if (wholeSeconds > (MaxWholeSeconds - (uint)digit) / 10)
                {
                    wholeOverflow = true;
                }
                else
                {
                    wholeSeconds = wholeSeconds * 10 + (uint)digit;
                }
            }

            index++;
        }

        if (wholeDigits == 0)
        {
            return null;
        }

        ulong fractionalMicros = 0;
        int fractionalDigits = 0;
        if (index < s.Length && s[index] == '.')
        {
            index++;
            int fractionStart = index;
            while (index < s.Length && IsAsciiDigit(s[index]))
            {
                if (fractionalDigits < 6)
                {
                    fractionalMicros = fractionalMicros * 10 + (uint)(s[index] - '0');
                    fractionalDigits++;
                }

                index++;
            }

            if (index == fractionStart)
            {
                return null;
            }
        }

        if (index != s.Length)
        {
            return null;
        }

        if (wholeOverflow)
        {
            return null;
        }

        while (fractionalDigits < 6)
        {
            fractionalMicros *= 10;
            fractionalDigits++;
        }

        if (wholeSeconds > MaxWholeSeconds
            || wholeSeconds == MaxWholeSeconds && fractionalMicros > MaxRemainingMicros)
        {
            return null;
        }

        ulong totalMicros = wholeSeconds * MicrosPerSecond + fractionalMicros;
        return (long)totalMicros;
    }

    public static string JsonString(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var builder = new StringBuilder(s.Length);
        builder.Append('"');
        foreach (char value in s)
        {
            switch (value)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (value < 0x20 || value == 0x7F)
                    {
                        builder.Append("\\u");
                        builder.Append(((int)value).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(value);
                    }

                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }

    public static int[] CodePoints(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var codePoints = new List<int>(s.Length);
        for (int index = 0; index < s.Length; index++)
        {
            char current = s[index];
            if (char.IsHighSurrogate(current)
                && index + 1 < s.Length
                && char.IsLowSurrogate(s[index + 1]))
            {
                codePoints.Add(char.ConvertToUtf32(current, s[index + 1]));
                index++;
            }
            else if (char.IsSurrogate(current))
            {
                codePoints.Add(0xFFFD);
            }
            else
            {
                codePoints.Add(current);
            }
        }

        return codePoints.ToArray();
    }

    public static string FromCodePoint(int cp)
    {
        if (cp < 0 || cp > 0x10FFFF || cp is >= 0xD800 and <= 0xDFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(cp));
        }

        return char.ConvertFromUtf32(cp);
    }

    private static bool IsAsciiDigit(char value) => value is >= '0' and <= '9';
}
