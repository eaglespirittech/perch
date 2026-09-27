using System.Globalization;

namespace Perch.Cli;

sealed class Options
{
    public string? Command { get; private init; }
    public string? Argument { get; private init; }
    public string? Device { get; private init; }
    public int TimeoutSeconds { get; private init; } = 60;
    public bool Json { get; private init; }
    public bool Quiet { get; private init; }
    public bool Direct { get; private init; }
    public bool WantsHelp { get; private init; }

    public static Options Parse(string[] args)
    {
        string? command = null, argument = null, device = null;
        var timeout = 60;
        bool json = false, quiet = false, help = false, direct = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            switch (arg)
            {
                case "--json":
                    json = true;
                    continue;
                case "--quiet" or "-q":
                    quiet = true;
                    continue;
                case "--direct":
                    direct = true;
                    continue;
                case "--help" or "-h" or "-?" or "/?":
                    help = true;
                    continue;
                case "--device" or "-d":
                    device = Next(args, ref i, "--device");
                    continue;
                case "--timeout" or "-t":
                    var raw = Next(args, ref i, "--timeout");
                    if (!int.TryParse(raw, out timeout) || timeout <= 0)
                        throw new ArgumentException($"--timeout needs a positive number of seconds, not \"{raw}\".");
                    continue;
            }

            // A negative number is an argument, not an option: "nudge -1.5".
            if (arg.StartsWith('-') && !IsNumber(arg))
                throw new ArgumentException($"Unknown option \"{arg}\".");

            if (command is null) command = arg.ToLowerInvariant();
            else if (argument is null) argument = arg;
            else throw new ArgumentException($"Unexpected extra argument \"{arg}\".");
        }

        return new Options
        {
            Command = command,
            Argument = argument,
            Device = device,
            TimeoutSeconds = timeout,
            Json = json,
            Quiet = quiet,
            Direct = direct,
            WantsHelp = help
        };
    }

    static bool IsNumber(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    static string Next(string[] args, ref int i, string option)
    {
        if (i + 1 >= args.Length) throw new ArgumentException($"{option} needs a value.");
        return args[++i];
    }
}
