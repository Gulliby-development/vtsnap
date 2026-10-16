using System.Buffers;
using System.Text;

namespace Vtsnap;

public sealed record DecodeResult(int[] CodePoints, int[] Replaced);

public static class Utf8Decoder
{
    public static DecodeResult Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var codePoints = new List<int>(bytes.Length);
        var replaced = new List<int>();

        int offset = 0;
        while (offset < bytes.Length)
        {
            OperationStatus status = Rune.DecodeFromUtf8(
                bytes.AsSpan(offset),
                out Rune rune,
                out int bytesConsumed);
            if (status == OperationStatus.Done)
            {
                codePoints.Add(rune.Value);
                offset += bytesConsumed;
                continue;
            }

            replaced.Add(codePoints.Count);
            codePoints.Add(0xFFFD);
            offset += InvalidSubpartLength(bytes, offset);
        }

        return new DecodeResult(codePoints.ToArray(), replaced.ToArray());
    }

    private static int InvalidSubpartLength(byte[] bytes, int offset)
    {
        int remaining = bytes.Length - offset;
        byte first = bytes[offset];
        int expectedLength;
        byte secondMinimum = 0x80;
        byte secondMaximum = 0xBF;

        if (first is >= 0xC2 and <= 0xDF)
        {
            expectedLength = 2;
        }
        else if (first is >= 0xE0 and <= 0xEF)
        {
            expectedLength = 3;
            if (first == 0xE0)
            {
                secondMinimum = 0xA0;
            }
            else if (first == 0xED)
            {
                secondMaximum = 0x9F;
            }
        }
        else if (first is >= 0xF0 and <= 0xF4)
        {
            expectedLength = 4;
            if (first == 0xF0)
            {
                secondMinimum = 0x90;
            }
            else if (first == 0xF4)
            {
                secondMaximum = 0x8F;
            }
        }
        else
        {
            return 1;
        }

        if (remaining < 2)
        {
            return 1;
        }

        byte second = bytes[offset + 1];
        if (second < secondMinimum || second > secondMaximum)
        {
            return 1;
        }

        for (int relative = 2; relative < expectedLength; relative++)
        {
            if (relative >= remaining)
            {
                return remaining;
            }

            byte next = bytes[offset + relative];
            if (next is < 0x80 or > 0xBF)
            {
                return relative;
            }
        }

        return expectedLength;
    }
}
