// ADR-0043: WebReaper.Cli — the primitive agent surface. A
// hand-rolled dispatcher over a tiny arg parser; one handler per
// command. Exits 0 on success, non-zero on argument or runtime error
// (the error message goes to stderr, no stack trace).

using WebReaper.Cli;
using WebReaper.Cli.Commands;

return await Run(args);

static async Task<int> Run(string[] args)
{
    try
    {
        var parsed = Args.Parse(args);

        return parsed.Command switch
        {
            "scrape" => await ScrapeCommand.RunAsync(parsed),
            "map" => await MapCommand.RunAsync(parsed),
            "init" => InitCommand.Run(parsed),
            "version" => VersionCommand.Run(),
            "browser" => await BrowserCommand.RunAsync(parsed),
            "stealth" => await StealthCommand.RunAsync(parsed),
            "help" => HelpAndExit(0),
            _ => HelpAndExit(2, $"Unknown command: '{parsed.Command}'")
        };
    }
    catch (CliException ex)
    {
        // Controlled CLI errors — print one human-readable line, no
        // stack. The user reading "Required flag --schema was not
        // supplied." learns what went wrong without scrolling.
        Console.Error.WriteLine(ex.Message);
        return 2;
    }
    catch (Exception ex)
    {
        // Anything else — the WebReaper library or BCL threw. Print
        // the type and message; suppress the stack unless WEBREAPER_DEBUG
        // is set (operator opt-in for the rare hard bug).
        var debug = Environment.GetEnvironmentVariable("WEBREAPER_DEBUG");
        if (string.Equals(debug, "1", StringComparison.Ordinal) || string.Equals(debug, "true", StringComparison.OrdinalIgnoreCase))
            Console.Error.WriteLine(ex.ToString());
        else
            Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}");
        return 1;
    }
}

static int HelpAndExit(int code, string? prefix = null)
{
    if (prefix is not null) Console.Error.WriteLine(prefix);
    var writer = code == 0 ? Console.Out : Console.Error;
    writer.WriteLine(Help.Top);
    return code;
}
