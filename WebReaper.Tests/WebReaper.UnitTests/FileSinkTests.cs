using System.Text.Json.Nodes;
using WebReaper.Sinks.Concrete;
using WebReaper.Sinks.Models;

namespace WebReaper.UnitTests;

// The file-sink deepening (ADR 0006) plus the file-persistence-prep
// migration (ADR-0011). The buffered drain has one home (BufferedFileSink);
// the only legitimate per-format variation is behind IFileSinkFormat. The
// two pure format tests pin the quarantined quirk exactly (no IO, the
// cheapest surface). The directory-creation and DataCleanupOnStart
// assertions that used to live here moved to FilePersistencePrepTests when
// BufferedFileSink was migrated to delegate that prep (ADR-0011); the two
// drain tests (kept) still prove the producer→consumer→file path and the
// header-once ordering — and exercise the delegated directory creation
// implicitly, since they write under a not-yet-existing temp directory.
public class FileSinkTests
{
    private sealed class TempDir : IDisposable
    {
        public string Root { get; } =
            Path.Combine(Path.GetTempPath(), $"wr-sink-{Guid.NewGuid():N}");

        public string At(params string[] parts) =>
            System.IO.Path.Combine(new[] { Root }.Concat(parts).ToArray());

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    private static ParsedData Row(string url, string name) =>
        new(url, new JsonObject { ["name"] = name });

    private static async Task<string[]> WaitForLinesAsync(string path, int count)
    {
        for (var i = 0; i < 120; i++) // ~3s max
        {
            if (File.Exists(path))
            {
                var lines = await File.ReadAllLinesAsync(path);
                if (lines.Length >= count) return lines;
            }

            await Task.Delay(25);
        }

        return File.Exists(path) ? await File.ReadAllLinesAsync(path) : Array.Empty<string>();
    }

    [Fact]
    public void JsonLinesFormat_has_no_header_and_renders_one_compact_object()
    {
        var format = new JsonLinesFormat();
        var row = new JsonObject { ["name"] = "a", ["url"] = "http://x/1" };

        Assert.Null(format.Header(row));
        Assert.Equal("{\"name\":\"a\",\"url\":\"http://x/1\"}", format.FormatRow(row));
    }

    [Fact]
    public void CsvFormat_header_is_the_first_rows_leaf_names_and_rows_are_quoted()
    {
        var format = new CsvFormat();
        var row = new JsonObject { ["name"] = "a", ["url"] = "http://x/1" };

        Assert.Equal("name,url", format.Header(row));
        Assert.Equal("\"a\",\"http://x/1\"", format.FormatRow(row));
    }

    [Fact]
    public async Task JsonLines_drains_rows_with_the_url_injected_and_no_header()
    {
        using var tmp = new TempDir();
        var path = tmp.At("out.jsonl");
        var sink = new JsonLinesFileSink(path, dataCleanupOnStart: false);

        await sink.EmitAsync(Row("http://x/1", "a"));
        await sink.EmitAsync(Row("http://x/2", "b"));

        var lines = await WaitForLinesAsync(path, 2);

        Assert.Equal(2, lines.Length);
        Assert.Equal("{\"name\":\"a\",\"url\":\"http://x/1\"}", lines[0]);
        Assert.Equal("{\"name\":\"b\",\"url\":\"http://x/2\"}", lines[1]);
    }

    [Fact]
    public async Task Csv_writes_the_header_once_then_quoted_rows()
    {
        using var tmp = new TempDir();
        var path = tmp.At("out.csv");
        var sink = new CsvFileSink(path, dataCleanupOnStart: false);

        await sink.EmitAsync(Row("http://x/1", "a"));
        await sink.EmitAsync(Row("http://x/2", "b"));

        var lines = await WaitForLinesAsync(path, 3);

        Assert.Equal(3, lines.Length);
        Assert.Equal("name,url", lines[0]);
        Assert.Equal("\"a\",\"http://x/1\"", lines[1]);
        Assert.Equal("\"b\",\"http://x/2\"", lines[2]);
    }
}
