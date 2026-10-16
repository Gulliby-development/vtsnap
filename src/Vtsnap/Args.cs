using System.Collections.ObjectModel;
using System.Globalization;

namespace Vtsnap;

public sealed record Parsed(
    string Command,
    int? Cols,
    int? Rows,
    string Format,
    bool Scrollback,
    long? AtMicros,
    long? EveryMicros,
    IReadOnlyList<string> Files)
{
    public bool IgnoreStyle { get; init; }
}

public static class Args
{
    public static Parsed Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0)
        {
            throw new CliError("missing command (render, frames, check or diff)");
        }

        if (args.Length == 1 && args[0] == "--help")
        {
            return new Parsed(
                "--help",
                null,
                null,
                "text",
                false,
                null,
                null,
                Array.Empty<string>());
        }

        string command = args[0];
        if (command is not ("render" or "frames" or "check" or "diff"))
        {
            throw new CliError($"unknown command \"{command}\"");
        }

        string? colsValue = null;
        string? rowsValue = null;
        string? formatValue = null;
        string? atValue = null;
        string? everyValue = null;
        bool scrollback = false;
        bool ignoreStyle = false;
        var files = new List<string>();

        for (int index = 1; index < args.Length; index++)
        {
            string argument = args[index];
            if (argument.StartsWith("-", StringComparison.Ordinal) && argument != "-")
            {
                int equalsIndex = argument.IndexOf('=');
                bool hasEquals = equalsIndex >= 0;
                string option = hasEquals ? argument[..equalsIndex] : argument;

                if (!IsValueOption(command, option)
                    && !(command == "render" && option == "--scrollback")
                    && !(command == "diff" && option == "--ignore-style"))
                {
                    throw new CliError($"unknown option \"{argument}\"");
                }

                if (option == "--scrollback")
                {
                    if (hasEquals)
                    {
                        throw new CliError($"unknown option \"{argument}\"");
                    }

                    scrollback = true;
                    continue;
                }

                if (option == "--ignore-style")
                {
                    if (hasEquals)
                    {
                        throw new CliError($"unknown option \"{argument}\"");
                    }

                    ignoreStyle = true;
                    continue;
                }

                string value;
                if (hasEquals)
                {
                    value = argument[(equalsIndex + 1)..];
                }
                else
                {
                    if (index + 1 >= args.Length)
                    {
                        throw new CliError($"{option} needs a value");
                    }

                    value = args[++index];
                }

                switch (option)
                {
                    case "--cols":
                        colsValue = value;
                        break;
                    case "--rows":
                        rowsValue = value;
                        break;
                    case "--format":
                        formatValue = value;
                        break;
                    case "--at":
                        atValue = value;
                        break;
                    case "--every":
                        everyValue = value;
                        break;
                }
            }
            else
            {
                files.Add(argument);
            }
        }

        int? cols = ParseDimension("--cols", colsValue);
        int? rows = ParseDimension("--rows", rowsValue);
        string format = ParseFormat(formatValue);

        if (command == "frames" && format == "json")
        {
            throw new CliError("--format json is not available for frames");
        }

        long? atMicros = ParseSecondsOption("--at", atValue);

        long? everyMicros = null;
        if (command == "frames")
        {
            if (everyValue is null)
            {
                throw new CliError("missing --every");
            }

            everyMicros = Text.ParseSeconds(everyValue);
            if (everyMicros is null || everyMicros <= 0)
            {
                throw new CliError($"invalid --every \"{everyValue}\"");
            }
        }

        if (command == "render" && scrollback && format == "json")
        {
            throw new CliError("--scrollback is not available with --format json");
        }

        ValidateFiles(command, files);

        return new Parsed(
            command,
            cols,
            rows,
            format,
            scrollback,
            atMicros,
            everyMicros,
            new ReadOnlyCollection<string>(files.ToArray()))
        {
            IgnoreStyle = ignoreStyle
        };
    }

    private static bool IsValueOption(string command, string option) =>
        option switch
        {
            "--cols" or "--rows" => true,
            "--format" => command is "render" or "frames",
            "--at" => command == "render",
            "--every" => command == "frames",
            _ => false
        };

    private static int? ParseDimension(string option, string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value.Length == 0
            || value.Length > 1 && value[0] == '0'
            || !ContainsOnlyAsciiDigits(value)
            || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int dimension)
            || dimension is < 1 or > 500)
        {
            throw new CliError($"invalid {option} \"{value}\"");
        }

        return dimension;
    }

    private static string ParseFormat(string? value)
    {
        if (value is null)
        {
            return "text";
        }

        if (value is not ("text" or "ansi" or "json"))
        {
            throw new CliError($"invalid --format \"{value}\"");
        }

        return value;
    }

    private static long? ParseSecondsOption(string option, string? value)
    {
        if (value is null)
        {
            return null;
        }

        long? micros = Text.ParseSeconds(value);
        if (micros is null)
        {
            throw new CliError($"invalid {option} \"{value}\"");
        }

        return micros;
    }

    private static void ValidateFiles(string command, IReadOnlyList<string> files)
    {
        int required = command == "diff" ? 2 : 1;
        if (files.Count < required)
        {
            string missing = command == "diff"
                ? files.Count == 0 ? "A" : "B"
                : "FILE";
            throw new CliError($"missing {missing}");
        }

        if (files.Count > required)
        {
            throw new CliError("too many arguments");
        }

        if (command == "diff" && files[0] == "-" && files[1] == "-")
        {
            throw new CliError("- can be given once");
        }
    }

    private static bool ContainsOnlyAsciiDigits(string value)
    {
        foreach (char character in value)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}
