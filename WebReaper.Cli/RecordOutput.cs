using System.Text;
using WebReaper.Sinks.Models;

namespace WebReaper.Cli;

// Shared record emission for `scrape` and `crawl` (ADR-0043 / ADR-0081). One
// JSON object per line (JSON Lines): a single record is one line, multiple
// records are one-per-line. Writes to stdout, or to a file with --output.
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
}
