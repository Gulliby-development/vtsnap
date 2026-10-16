using System.Text;

namespace Vtsnap;

public interface IIo
{
    byte[] ReadFile(string path);

    byte[] ReadStdin();

    void Out(string text);

    void Err(string text);
}

public static class Cli
{
    private const string HelpText =
        "usage: vtsnap render [--cols N] [--rows N] [--format text|ansi|json] [--scrollback] [--at SECONDS] FILE\n"
        + "       vtsnap frames --every SECONDS [--cols N] [--rows N] [--format text|ansi] FILE\n"
        + "       vtsnap check [--cols N] [--rows N] FILE\n"
        + "       vtsnap diff [--cols N] [--rows N] A B\n";

    public static int Run(string[] args, IIo io)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(io);

        try
        {
            Parsed parsed = Args.Parse(args);
            if (parsed.Command == "--help")
            {
                io.Out(HelpText);
                return 0;
            }

            return parsed.Command switch
            {
                "render" => RunRender(parsed, io),
                "frames" => RunFrames(parsed, io),
                "check" => RunCheck(parsed, io),
                "diff" => RunDiff(parsed, io),
                _ => throw new InvalidOperationException("Unsupported parsed command.")
            };
        }
        catch (CliError error)
        {
            io.Err($"vtsnap: {error.Message}\n");
            return 2;
        }
    }

    private static int RunRender(Parsed parsed, IIo io)
    {
        Source source = LoadInput(parsed, parsed.Files[0], io);
        if (parsed.AtMicros is not null && !source.IsCast)
        {
            throw new CliError("--at needs a cast file");
        }

        Played played = Replay.Play(source, parsed.AtMicros);
        Snapshot snapshot = played.Terminal.Snapshot(parsed.Scrollback);
        io.Out(Render(snapshot, parsed.Format));
        return 0;
    }

    private static int RunFrames(Parsed parsed, IIo io)
    {
        Source source = LoadInput(parsed, parsed.Files[0], io);
        if (!source.IsCast)
        {
            throw new CliError("frames needs a cast file");
        }

        var output = new StringBuilder();
        foreach (long time in Replay.FrameTimes(source, parsed.EveryMicros!.Value))
        {
            Played played = Replay.Play(source, time);
            Snapshot snapshot = played.Terminal.Snapshot(withScrollback: false);
            output.Append("=== ");
            output.Append(Text.Seconds(time));
            output.Append(" s ===\n");
            output.Append(Render(snapshot, parsed.Format));
        }

        io.Out(output.ToString());
        return 0;
    }

    private static int RunCheck(Parsed parsed, IIo io)
    {
        string file = parsed.Files[0];
        Source source = LoadInput(parsed, file, io);
        Played played = Replay.Play(source, untilMicros: null);
        IReadOnlyList<Finding> findings = Analysis.Findings(source, played);
        Result result = Analysis.RenderCheck(DisplayPath(file), findings);
        io.Out(result.Text);
        return result.Code;
    }

    private static int RunDiff(Parsed parsed, IIo io)
    {
        Source first = LoadInput(parsed, parsed.Files[0], io);
        Source second = LoadInput(parsed, parsed.Files[1], io);

        Played firstPlayed = Replay.Play(first, untilMicros: null);
        Played secondPlayed = Replay.Play(second, untilMicros: null);
        Snapshot firstSnapshot = firstPlayed.Terminal.Snapshot(withScrollback: false);
        Snapshot secondSnapshot = secondPlayed.Terminal.Snapshot(withScrollback: false);
        Result result = Diff.Compare(firstSnapshot, secondSnapshot);
        io.Out(result.Text);
        return result.Code;
    }

    private static Source LoadInput(Parsed parsed, string file, IIo io)
    {
        byte[] bytes;
        if (file == "-")
        {
            bytes = io.ReadStdin();
        }
        else
        {
            try
            {
                bytes = io.ReadFile(file);
            }
            catch (Exception)
            {
                throw new CliError($"{DisplayPath(file)}: cannot read the file");
            }
        }

        try
        {
            return Replay.Load(bytes, parsed.Cols, parsed.Rows);
        }
        catch (CastException error)
        {
            throw new CliError($"{DisplayPath(file)}:{error.Line}: bad event");
        }
    }

    private static string Render(Snapshot snapshot, string format) =>
        format switch
        {
            "text" => RenderText.Render(snapshot),
            "ansi" => RenderAnsi.Render(snapshot),
            "json" => RenderJson.Render(snapshot),
            _ => throw new InvalidOperationException("Unsupported parsed format.")
        };

    private static string DisplayPath(string path) =>
        path == "-" ? "<stdin>" : path.Replace('\\', '/');
}

internal sealed class ProcessIo : IIo
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public byte[] ReadFile(string path) => File.ReadAllBytes(path);

    public byte[] ReadStdin()
    {
        using var bytes = new MemoryStream();
        Console.OpenStandardInput().CopyTo(bytes);
        return bytes.ToArray();
    }

    public void Out(string text) => Write(Console.OpenStandardOutput(), text);

    public void Err(string text) => Write(Console.OpenStandardError(), text);

    private static void Write(Stream stream, string text)
    {
        byte[] bytes = Utf8.GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
    }
}
