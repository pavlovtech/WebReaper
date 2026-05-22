using System.Globalization;

namespace WebReaper.Cli;

// ADR-0043: a tiny, AOT-clean argument parser. ~120 lines, zero deps,
// no reflection. The CLI surface is small enough that
// System.CommandLine 2.0's preview AOT warnings, transitive deps, and
// reflection-driven binders are not the right trade.
//
// Grammar:
//   webreaper <command> [positional...] [--flag value]... [--bool-flag]...
//
// A bool flag is detected by absence of a value before the next --flag
// or end-of-args. `--flag=value` is also accepted (one token).

internal readonly record struct ParsedArgs(
    string Command,
    IReadOnlyList<string> Positional,
    IReadOnlyDictionary<string, string> Flags)
{
    public string? GetFlag(string name) => Flags.TryGetValue(name, out var v) ? v : null;

    public string RequireFlag(string name) =>
        GetFlag(name) ?? throw new CliException($"Required flag --{name} was not supplied.");

    public bool HasFlag(string name) => Flags.ContainsKey(name);

    public int GetIntFlag(string name, int fallback)
    {
        var raw = GetFlag(name);
        if (raw is null) return fallback;
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            throw new CliException($"Flag --{name} must be an integer; got '{raw}'.");
        return v;
    }

    public TimeSpan? GetTimeSpanFlag(string name)
    {
        var raw = GetFlag(name);
        if (raw is null) return null;

        // Accept either a parseable TimeSpan (00:00:30) or a suffixed
        // shorthand (30s, 5m, 2h, 1d) — agents and humans both reach
        // for the latter; humans accept either.
        if (TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out var ts)) return ts;

        if (raw.Length >= 2)
        {
            var unit = raw[^1];
            var numeric = raw[..^1];
            if (int.TryParse(numeric, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            {
                return unit switch
                {
                    's' => TimeSpan.FromSeconds(n),
                    'm' => TimeSpan.FromMinutes(n),
                    'h' => TimeSpan.FromHours(n),
                    'd' => TimeSpan.FromDays(n),
                    _ => throw new CliException(
                        $"Flag --{name} has an unrecognised time unit '{unit}'; use s/m/h/d.")
                };
            }
        }

        throw new CliException(
            $"Flag --{name} must be a TimeSpan (00:00:30) or shorthand (30s/5m/2h/1d); got '{raw}'.");
    }
}

internal static class Args
{
    public static ParsedArgs Parse(string[] args)
    {
        if (args.Length == 0)
            throw new CliException("Missing command. Run 'webreaper --help'.");

        var command = args[0];

        if (command is "--help" or "-h")
            return new ParsedArgs("help", Array.Empty<string>(),
                new Dictionary<string, string>());

        if (command is "--version" or "-v")
            return new ParsedArgs("version", Array.Empty<string>(),
                new Dictionary<string, string>());

        var positional = new List<string>();
        var flags = new Dictionary<string, string>(StringComparer.Ordinal);

        for (int i = 1; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                var name = arg[2..];

                // --flag=value form.
                var eq = name.IndexOf('=');
                if (eq >= 0)
                {
                    flags[name[..eq]] = name[(eq + 1)..];
                    continue;
                }

                // --flag value form (value if next is non-flag).
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    flags[name] = args[i + 1];
                    i++;
                }
                else
                {
                    // Boolean flag (presence = true).
                    flags[name] = "true";
                }
            }
            else
            {
                positional.Add(arg);
            }
        }

        return new ParsedArgs(command, positional, flags);
    }
}

// A controlled-exit exception class. Program.Main catches and prints
// the message + sets a non-zero exit code, without a stack trace —
// the user reading "Required flag --schema was not supplied." learns
// what went wrong without scrolling past 30 lines of stack.
internal sealed class CliException : Exception
{
    public CliException(string message) : base(message) { }
}
