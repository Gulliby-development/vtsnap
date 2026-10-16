using System.Text;
using System.Text.Json;

namespace Vtsnap;

public sealed record CastFile(
    int? Width,
    int? Height,
    IReadOnlyList<CastEvent> Output,
    int EventCount,
    long Duration);

public sealed record CastEvent(long Micros, int[] CodePoints);

public class CastException : Exception
{
    public CastException(int Line) : base($"Invalid cast data on line {Line}.")
    {
        this.Line = Line;
    }

    public int Line { get; }
}

public static class Cast
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static CastFile Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        List<byte[]> lines = SplitLines(bytes);
        if (lines.Count == 0
            || !TryReadHeader(lines[0], out int? width, out int? height))
        {
            throw new CastException(1);
        }

        var output = new List<CastEvent>();
        int eventCount = 0;
        long duration = 0;

        for (int index = 1; index < lines.Count; index++)
        {
            int lineNumber = index + 1;
            string lineText;

            try
            {
                lineText = StrictUtf8.GetString(lines[index]);
            }
            catch (DecoderFallbackException)
            {
                throw new CastException(lineNumber);
            }

            if (string.IsNullOrWhiteSpace(lineText))
            {
                continue;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(lineText);
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Array
                    || root.GetArrayLength() != 3)
                {
                    throw new CastException(lineNumber);
                }

                JsonElement timestamp = root[0];
                JsonElement eventCode = root[1];
                JsonElement data = root[2];
                if (timestamp.ValueKind != JsonValueKind.Number
                    || eventCode.ValueKind != JsonValueKind.String
                    || data.ValueKind != JsonValueKind.String)
                {
                    throw new CastException(lineNumber);
                }

                string timestampText = timestamp.GetRawText();
                long? micros = Text.ParseSeconds(timestampText);
                if (micros is null)
                {
                    throw new CastException(lineNumber);
                }

                string decodedCode = DecodeJsonString(eventCode);
                string decodedData = DecodeJsonString(data);
                eventCount++;

                if (decodedCode == "o")
                {
                    int[] codePoints = Text.CodePoints(decodedData);
                    output.Add(new CastEvent(micros.Value, codePoints));
                    duration = Math.Max(duration, micros.Value);
                }
            }
            catch (JsonException)
            {
                throw new CastException(lineNumber);
            }
        }

        return new CastFile(width, height, output, eventCount, duration);
    }

    public static bool LooksLikeCast(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        int lineEnd = Array.IndexOf(bytes, (byte)'\n');
        if (lineEnd < 0)
        {
            lineEnd = bytes.Length;
        }

        if (lineEnd > 0 && bytes[lineEnd - 1] == (byte)'\r')
        {
            lineEnd--;
        }

        byte[] header = bytes.AsSpan(0, lineEnd).ToArray();
        return TryReadHeader(header, out _, out _);
    }

    private static bool TryReadHeader(byte[] line, out int? width, out int? height)
    {
        width = null;
        height = null;

        try
        {
            string text = StrictUtf8.GetString(line);
            using JsonDocument document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = int.MaxValue });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("version", out JsonElement version)
                || version.ValueKind != JsonValueKind.Number
                || !IsExactlyTwo(version.GetRawText()))
            {
                return false;
            }

            if (root.TryGetProperty("width", out JsonElement widthElement)
                && TryGetDimension(widthElement, out int parsedWidth))
            {
                width = parsedWidth;
            }

            if (root.TryGetProperty("height", out JsonElement heightElement)
                && TryGetDimension(heightElement, out int parsedHeight))
            {
                height = parsedHeight;
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool TryGetDimension(JsonElement element, out int dimension)
    {
        dimension = 0;
        if (element.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        string raw = element.GetRawText();
        if (!TryGetNumberParts(
                raw,
                out string digits,
                out int firstNonZero,
                out int lastNonZero,
                out long decimalPosition,
                out bool negative)
            || negative
            || decimalPosition is < 1 or > 3
            || lastNonZero >= firstNonZero + decimalPosition)
        {
            return false;
        }

        int value = 0;
        for (int offset = 0; offset < decimalPosition; offset++)
        {
            int digitIndex = firstNonZero + offset;
            int digit = digitIndex < digits.Length ? digits[digitIndex] - '0' : 0;
            value = value * 10 + digit;
        }

        if (value is < 1 or > 500)
        {
            return false;
        }

        dimension = value;
        return true;
    }

    private static bool IsExactlyTwo(string raw)
    {
        if (!TryGetNumberParts(
                raw,
                out string digits,
                out int firstNonZero,
                out int lastNonZero,
                out long decimalPosition,
                out bool negative)
            || negative
            || decimalPosition != 1
            || digits[firstNonZero] != '2')
        {
            return false;
        }

        for (int index = firstNonZero + 1; index <= lastNonZero; index++)
        {
            if (digits[index] != '0')
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetNumberParts(
        string raw,
        out string digits,
        out int firstNonZero,
        out int lastNonZero,
        out long decimalPosition,
        out bool negative)
    {
        negative = raw.StartsWith("-", StringComparison.Ordinal);
        int start = negative ? 1 : 0;
        int exponentIndex = raw.IndexOfAny(['e', 'E'], start);
        int mantissaEnd = exponentIndex < 0 ? raw.Length : exponentIndex;
        int decimalIndex = raw.IndexOf('.', start, mantissaEnd - start);
        int integerDigits = decimalIndex < 0 ? mantissaEnd - start : decimalIndex - start;

        var digitBuilder = new StringBuilder(mantissaEnd - start);
        for (int index = start; index < mantissaEnd; index++)
        {
            if (raw[index] != '.')
            {
                digitBuilder.Append(raw[index]);
            }
        }

        digits = digitBuilder.ToString();
        firstNonZero = -1;
        lastNonZero = -1;
        for (int index = 0; index < digits.Length; index++)
        {
            if (digits[index] != '0')
            {
                if (firstNonZero < 0)
                {
                    firstNonZero = index;
                }

                lastNonZero = index;
            }
        }

        decimalPosition = 0;
        if (firstNonZero < 0)
        {
            return false;
        }

        long exponent = exponentIndex < 0 ? 0 : ParseExponent(raw, exponentIndex + 1);
        decimalPosition = integerDigits - (long)firstNonZero + exponent;
        return true;
    }

    private static long ParseExponent(string raw, int index)
    {
        bool negative = false;
        if (index < raw.Length && raw[index] is '+' or '-')
        {
            negative = raw[index] == '-';
            index++;
        }

        long exponent = 0;
        for (; index < raw.Length; index++)
        {
            int digit = raw[index] - '0';
            if (exponent > (long.MaxValue - digit) / 10)
            {
                return negative ? -long.MaxValue : long.MaxValue;
            }

            exponent = exponent * 10 + digit;
        }

        return negative ? -exponent : exponent;
    }

    private static string DecodeJsonString(JsonElement element)
    {
        string raw = element.GetRawText();
        var decoded = new StringBuilder(raw.Length - 2);

        for (int index = 1; index < raw.Length - 1; index++)
        {
            char value = raw[index];
            if (value != '\\')
            {
                decoded.Append(value);
                continue;
            }

            char escape = raw[++index];
            switch (escape)
            {
                case '"':
                    decoded.Append('"');
                    break;
                case '\\':
                    decoded.Append('\\');
                    break;
                case '/':
                    decoded.Append('/');
                    break;
                case 'b':
                    decoded.Append('\b');
                    break;
                case 'f':
                    decoded.Append('\f');
                    break;
                case 'n':
                    decoded.Append('\n');
                    break;
                case 'r':
                    decoded.Append('\r');
                    break;
                case 't':
                    decoded.Append('\t');
                    break;
                case 'u':
                    decoded.Append((char)ParseHexCodeUnit(raw, index + 1));
                    index += 4;
                    break;
                default:
                    throw new JsonException("Invalid JSON string escape.");
            }
        }

        return decoded.ToString();
    }

    private static int ParseHexCodeUnit(string raw, int start)
    {
        int value = 0;
        for (int index = start; index < start + 4; index++)
        {
            char digit = raw[index];
            value = value * 16 + (digit switch
            {
                >= '0' and <= '9' => digit - '0',
                >= 'a' and <= 'f' => digit - 'a' + 10,
                >= 'A' and <= 'F' => digit - 'A' + 10,
                _ => throw new JsonException("Invalid JSON Unicode escape.")
            });
        }

        return value;
    }

    private static List<byte[]> SplitLines(byte[] bytes)
    {
        var lines = new List<byte[]>();
        int lineStart = 0;

        for (int index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] != (byte)'\n')
            {
                continue;
            }

            int lineEnd = index;
            if (lineEnd > lineStart && bytes[lineEnd - 1] == (byte)'\r')
            {
                lineEnd--;
            }

            lines.Add(bytes.AsSpan(lineStart, lineEnd - lineStart).ToArray());
            lineStart = index + 1;
        }

        lines.Add(bytes.AsSpan(lineStart).ToArray());
        return lines;
    }
}
