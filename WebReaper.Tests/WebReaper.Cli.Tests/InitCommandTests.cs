using WebReaper.Cli;
using WebReaper.Cli.Commands;

namespace WebReaper.Cli.Tests;

// ADR-0043: `webreaper init` writes the embedded skill to disk. Tests
// run in a temp directory so the host's .claude/ is never touched.
public class InitCommandTests : IDisposable
{
    private readonly string _tempDir;

    public InitCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "webreaper-init-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public void Init_writes_skill_md_to_target_dir()
    {
        var args = Args.Parse(new[] { "init", "--dir", _tempDir });

        var code = InitCommand.Run(args);

        Assert.Equal(0, code);
        var path = Path.Combine(_tempDir, "SKILL.md");
        Assert.True(File.Exists(path), "SKILL.md should have been written");
        var content = File.ReadAllText(path);
        Assert.Contains("webreaper", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("description", content);
    }

    [Fact]
    public void Init_refuses_to_overwrite_without_force()
    {
        var path = Path.Combine(_tempDir, "SKILL.md");
        File.WriteAllText(path, "pre-existing");

        var args = Args.Parse(new[] { "init", "--dir", _tempDir });
        var code = InitCommand.Run(args);

        // Non-zero exit code; original content preserved.
        Assert.NotEqual(0, code);
        Assert.Equal("pre-existing", File.ReadAllText(path));
    }

    [Fact]
    public void Init_force_overwrites_existing_skill()
    {
        var path = Path.Combine(_tempDir, "SKILL.md");
        File.WriteAllText(path, "pre-existing");

        var args = Args.Parse(new[] { "init", "--dir", _tempDir, "--force" });
        var code = InitCommand.Run(args);

        Assert.Equal(0, code);
        var content = File.ReadAllText(path);
        Assert.DoesNotContain("pre-existing", content);
        Assert.Contains("webreaper", content, StringComparison.OrdinalIgnoreCase);
    }
}
