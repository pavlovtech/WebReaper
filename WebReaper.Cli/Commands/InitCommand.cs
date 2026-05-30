using System.Reflection;

namespace WebReaper.Cli.Commands;

// `webreaper init` — the funnel-installation mechanism (firecrawl's
// `init --all` shape). Writes the embedded SKILL.md to the agent's
// expected location. v1 ships Claude Code (.claude/skills/webreaper/);
// v2 adds --for cursor/windsurf/etc.
internal static class InitCommand
{
    private const string DefaultDir = ".claude/skills/webreaper";

    public static int Run(ParsedArgs args)
    {
        var dir = args.GetFlag("dir") ?? DefaultDir;
        var force = args.HasFlag("force");

        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, "SKILL.md");

        if (File.Exists(path) && !force)
        {
            Console.Error.WriteLine(
                $"SKILL.md already exists at '{path}'. Use --force to overwrite.");
            return 3;
        }

        var skillContent = LoadEmbeddedSkill();
        File.WriteAllText(path, skillContent);

        Console.WriteLine($"Wrote WebReaper Agent Skill to {path}");
        Console.WriteLine();
        Console.WriteLine("Try it out:");
        Console.WriteLine("  webreaper scrape https://example.com   # one page as Markdown (or JSON with --schema)");
        Console.WriteLine("  webreaper crawl  https://example.com   # the whole site as JSON Lines");
        Console.WriteLine("  webreaper map    https://example.com   # list the site's URLs");
        return 0;
    }

    private static string LoadEmbeddedSkill()
    {
        // The skill is shipped as an embedded resource so init has no
        // working-directory dependency.
        var assembly = typeof(InitCommand).Assembly;
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("SKILL.md", StringComparison.Ordinal))
            ?? throw new CliException(
                "Embedded SKILL.md resource not found. This is a CLI build bug.");

        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new CliException(
                $"Embedded resource '{name}' could not be opened.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
