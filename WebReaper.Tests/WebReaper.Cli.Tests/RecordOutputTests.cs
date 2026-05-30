using System.Text.Json.Nodes;
using WebReaper.Cli;
using WebReaper.Sinks.Models;
using Xunit;

namespace WebReaper.Cli.Tests;

// ADR-0084 piece 3b: --output-dir (one file per page), the location hint, and
// --open. These pin the directory writer (slug, .md vs .json, collisions) and
// the target-flag resolution.
public class RecordOutputTests
{
    private static ParsedData Rec(string url, string? markdown = null)
    {
        var obj = new JsonObject { ["title"] = "T" };
        if (markdown is not null) obj["markdown"] = markdown;
        return new ParsedData(url, obj);
    }

    [Fact]
    public async Task WriteToDirectory_markdown_mode_writes_md_with_markdown_body()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            await RecordOutput.WriteToDirectoryAsync(
                [Rec("https://x.test/leadership", "# Execs")], dir, asMarkdown: true);

            var files = Directory.GetFiles(dir, "*.md");
            Assert.Single(files);
            Assert.Equal("# Execs", await File.ReadAllTextAsync(files[0]));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task WriteToDirectory_non_markdown_writes_json()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            await RecordOutput.WriteToDirectoryAsync([Rec("https://x.test/a")], dir, asMarkdown: false);

            var files = Directory.GetFiles(dir, "*.json");
            Assert.Single(files);
            Assert.Contains("\"title\"", await File.ReadAllTextAsync(files[0]));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task WriteToDirectory_disambiguates_colliding_slugs()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            await RecordOutput.WriteToDirectoryAsync(
                [Rec("https://x.test/a"), Rec("https://x.test/a")], dir, asMarkdown: false);

            Assert.Equal(2, Directory.GetFiles(dir, "*.json").Length);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ParseTarget_output_dir_defaults_when_valueless()
        => Assert.Equal("./webreaper-out",
            RecordOutput.ParseTarget(Args.Parse(["crawl", "u", "--output-dir"])).OutputDir);

    [Fact]
    public void ParseTarget_output_dir_takes_value()
        => Assert.Equal("out",
            RecordOutput.ParseTarget(Args.Parse(["crawl", "u", "--output-dir", "out"])).OutputDir);

    [Fact]
    public void ParseTarget_rejects_output_and_output_dir_together()
        => Assert.Throws<CliException>(() =>
            RecordOutput.ParseTarget(Args.Parse(["crawl", "u", "--output", "f.jsonl", "--output-dir", "d"])));

    [Fact]
    public void ParseTarget_open_flag_is_read()
        => Assert.True(RecordOutput.ParseTarget(Args.Parse(["scrape", "u", "--open"])).Open);
}
