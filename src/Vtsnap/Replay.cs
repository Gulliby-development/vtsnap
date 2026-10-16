namespace Vtsnap;

public sealed record Source(
    bool IsCast,
    int Cols,
    int Rows,
    IReadOnlyList<CastEvent> Chunks,
    int[] Replaced,
    int TotalCodePoints);

public sealed record Played(Terminal Terminal, IReadOnlyList<VtEvent> Events);

public static class Replay
{
    public static Source Load(byte[] bytes, int? cols, int? rows)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ValidateDimension(cols, nameof(cols));
        ValidateDimension(rows, nameof(rows));

        if (Cast.LooksLikeCast(bytes))
        {
            CastFile cast = Cast.Parse(bytes);
            int resolvedCols = cols ?? cast.Width ?? 80;
            int resolvedRows = rows ?? cast.Height ?? 24;
            return new Source(
                true,
                resolvedCols,
                resolvedRows,
                cast.Output,
                Array.Empty<int>(),
                CountCodePoints(cast.Output));
        }

        DecodeResult decoded = Utf8Decoder.Decode(bytes);
        var chunks = new[] { new CastEvent(0, decoded.CodePoints) };
        return new Source(
            false,
            cols ?? 80,
            rows ?? 24,
            chunks,
            decoded.Replaced,
            decoded.CodePoints.Length);
    }

    public static Played Play(Source source, long? untilMicros)
    {
        ArgumentNullException.ThrowIfNull(source);

        var terminal = new Terminal(source.Cols, source.Rows);
        var parser = new VtParser();
        var events = new List<VtEvent>();

        foreach (CastEvent chunk in source.Chunks)
        {
            if (untilMicros is not null && chunk.Micros > untilMicros.Value)
            {
                continue;
            }

            Apply(parser.Feed(chunk.CodePoints), terminal, events);
        }

        if (untilMicros is null)
        {
            Apply(parser.Finish(), terminal, events);
        }

        return new Played(terminal, events.AsReadOnly());
    }

    public static long[] FrameTimes(Source source, long everyMicros)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (everyMicros <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(everyMicros));
        }

        long maximum = 0;
        foreach (CastEvent chunk in source.Chunks)
        {
            maximum = Math.Max(maximum, chunk.Micros);
        }

        var times = new List<long> { 0 };
        long next = everyMicros;
        while (next <= maximum)
        {
            times.Add(next);
            if (maximum - next < everyMicros)
            {
                break;
            }

            next += everyMicros;
        }

        if (times[^1] != maximum)
        {
            times.Add(maximum);
        }

        return times.ToArray();
    }

    private static void Apply(
        IReadOnlyList<VtEvent> newEvents,
        Terminal terminal,
        List<VtEvent> events)
    {
        foreach (VtEvent ev in newEvents)
        {
            terminal.Apply(ev);
            events.Add(ev);
        }
    }

    private static int CountCodePoints(IReadOnlyList<CastEvent> chunks)
    {
        int total = 0;
        foreach (CastEvent chunk in chunks)
        {
            total = checked(total + chunk.CodePoints.Length);
        }

        return total;
    }

    private static void ValidateDimension(int? dimension, string parameterName)
    {
        if (dimension is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
