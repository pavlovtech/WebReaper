using WebReaper.DataAccess;

namespace WebReaper.UnitTests;

// ADR-0011: the three pre-write responsibilities, asserted once at the
// helper's own interface (the deduplicated home of a previously copy-pasted,
// bug-prone policy). FilePersistencePrep is internal; this assembly has
// InternalsVisibleTo access.
public class FilePersistencePrepTests
{
    private static string FreshRoot() =>
        Path.Combine(Path.GetTempPath(), $"wr-fpp-{Guid.NewGuid():N}");

    [Fact]
    public void EnsureDirectory_creates_missing_nested_directory()
    {
        var root = FreshRoot();
        try
        {
            var path = Path.Combine(root, "a", "b", "c.txt"); // a/, b/ do not exist
            FilePersistencePrep.EnsureDirectory(path);
            Assert.True(Directory.Exists(Path.GetDirectoryName(path)));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void EnsureDirectory_is_idempotent_and_tolerates_a_bare_filename()
    {
        var root = FreshRoot();
        try
        {
            var path = Path.Combine(root, "x.txt");
            FilePersistencePrep.EnsureDirectory(path);
            FilePersistencePrep.EnsureDirectory(path);          // second call: no throw
            FilePersistencePrep.EnsureDirectory("bare-name.txt"); // no directory part: no throw
            Assert.True(Directory.Exists(root));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void CleanupOnStart_deletes_an_existing_file_when_true_with_zero_writes()
    {
        var root = FreshRoot();
        try
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "stale.txt");
            File.WriteAllText(path, "stale");

            FilePersistencePrep.CleanupOnStart(path, dataCleanupOnStart: true);

            Assert.False(File.Exists(path)); // gone deterministically, no write needed
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void CleanupOnStart_keeps_the_file_when_false()
    {
        var root = FreshRoot();
        try
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "keep.txt");
            File.WriteAllText(path, "keep");

            FilePersistencePrep.CleanupOnStart(path, dataCleanupOnStart: false);

            Assert.True(File.Exists(path));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ReadAllTextOrNullAsync_returns_null_for_an_absent_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wr-fpp-absent-{Guid.NewGuid():N}.txt");
        Assert.Null(await FilePersistencePrep.ReadAllTextOrNullAsync(path));
    }

    [Fact]
    public async Task ReadAllTextOrNullAsync_returns_content_for_a_present_file()
    {
        var root = FreshRoot();
        try
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "p.txt");
            await File.WriteAllTextAsync(path, "hello");
            Assert.Equal("hello", await FilePersistencePrep.ReadAllTextOrNullAsync(path));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
