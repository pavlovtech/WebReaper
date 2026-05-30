using System.Diagnostics;
using System.Text;
using WebReaper.Sinks.Models;

namespace WebReaper.Cli;

// Shared record emission for `scrape` and `crawl` (ADR-0043 / ADR-0081).
// Default: one JSON object per line (JSON Lines) to stdout, or to a single
// file with --output. ADR-0084 piece 3b adds --output-dir (one file per page)
// plus a TTY-gated location hint and an opt-in --open reveal.
internal static class RecordOutput
{
    public static async Task WriteAsync(IReadOnlyList<ParsedData> records, string? output)
    {
        var sb = new StringBuilder();
        foreach (var r in records)
        {
            sb.Append(r.Data.ToJsonString());
            sb.Append('\n');
        }

        var text = sb.ToString().TrimEnd('\n');

        if (output is not null)
            await File.WriteAllTextAsync(output, text);
        else
            Console.WriteLine(text);
    }

    // ADR-0084 Q6: resolve the output target flags shared by scrape and crawl.
    // --output-dir without a value defaults to ./webreaper-out (cwd, never
    // ~/Documents). --output and --output-dir are mutually exclusive.
    public static (string? Output, string? OutputDir, bool Open) ParseTarget(ParsedArgs args)
    {
        string? outputDir = null;
        if (args.HasFlag("output-dir"))
        {
            var value = args.GetFlag("output-dir");
            outputDir = value is null or "true" ? "./webreaper-out" : value;
        }

        var output = args.GetFlag("output");
        if (output is not null && outputDir is not null)
            throw new CliException(
                "Use either --output (one file) or --output-dir (one file per page), not both.");

        return (output, outputDir, args.HasFlag("open"));
    }

    // ADR-0084 Q6: emit to the resolved target. Directory => one file per page
    // plus the hint + optional reveal; single file => the JSON Lines file plus
    // the hint + optional reveal; otherwise stdout (no location to hint).
    public static async Task EmitAsync(
        IReadOnlyList<ParsedData> records, string? output, string? outputDir, bool open, bool asMarkdown)
    {
        if (outputDir is not null)
        {
            await WriteToDirectoryAsync(records, outputDir, asMarkdown);
            PrintLocationHint(outputDir, records.Count);
            if (open) TryOpen(outputDir);
            return;
        }

        await WriteAsync(records, output);
        if (output is not null)
        {
            PrintLocationHint(output, records.Count);
            if (open)
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(output));
                TryOpen(string.IsNullOrEmpty(dir) ? "." : dir);
            }
        }
    }

    // ADR-0084 Q6: one file per page under <directory>. Markdown mode writes the
    // page's Markdown to a .md file; every other mode (schema / prompt / infer)
    // writes the record's JSON to a .json file. Filenames are URL slugs, made
    // unique on collision.
    public static async Task WriteToDirectoryAsync(
        IReadOnlyList<ParsedData> records, string directory, bool asMarkdown)
    {
        Directory.CreateDirectory(directory);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ext = asMarkdown ? ".md" : ".json";

        foreach (var record in records)
        {
            var name = UniqueName(Slug(record.Url), ext, used);
            var content = asMarkdown ? MarkdownOf(record) : record.Data.ToJsonString();
            await File.WriteAllTextAsync(Path.Combine(directory, name), content);
        }
    }

    // ADR-0084 Q6: a one-line "where it landed" hint, stderr + TTY-gated so it
    // never clutters a pipe or CI. Only emitted when output went to a file or
    // directory (stdout has no location).
    public static void PrintLocationHint(string path, int count)
    {
        if (Console.IsErrorRedirected) return;
        Console.Error.WriteLine($"✓ Wrote {count} record(s) to {path}");
    }

    // ADR-0084 Q6: opt-in --open reveal. Best-effort; never fails the scrape
    // because a file manager could not be launched (headless host, no xdg-open).
    public static void TryOpen(string path)
    {
        try
        {
            var command = OperatingSystem.IsMacOS() ? "open"
                : OperatingSystem.IsWindows() ? "explorer"
                : "xdg-open";
            var psi = new ProcessStartInfo(command) { UseShellExecute = false };
            psi.ArgumentList.Add(path);
            using var _ = Process.Start(psi);
        }
        catch
        {
            // intentionally swallowed: revealing the folder is a convenience.
        }
    }

    private static string MarkdownOf(ParsedData record) =>
        record.Data.TryGetPropertyValue("markdown", out var node) && node is not null
            ? node.GetValue<string>()
            : record.Data.ToJsonString();

    // Scheme-stripped, lower-cased, non-alphanumerics collapsed to single
    // dashes, capped at 100 chars. "https://co.com/leadership" -> "co-com-leadership".
    private static string Slug(string url)
    {
        var s = url;
        var scheme = s.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) s = s[(scheme + 3)..];

        var sb = new StringBuilder(s.Length);
        var lastDash = false;
        foreach (var c in s)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
                lastDash = false;
            }
            else if (!lastDash)
            {
                sb.Append('-');
                lastDash = true;
            }
        }

        var slug = sb.ToString().Trim('-');
        if (slug.Length > 100) slug = slug[..100].Trim('-');
        return slug.Length == 0 ? "page" : slug;
    }

    private static string UniqueName(string baseName, string ext, HashSet<string> used)
    {
        var name = baseName + ext;
        var i = 2;
        while (!used.Add(name))
            name = $"{baseName}-{i++}{ext}";
        return name;
    }
}
