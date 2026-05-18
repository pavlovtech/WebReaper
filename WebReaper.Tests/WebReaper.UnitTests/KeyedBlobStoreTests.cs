using WebReaper.DataAccess;

namespace WebReaper.UnitTests;

// The keyed blob store contract, tested directly through its interface — the
// payload-free half of ADR 0003. The same four assertions run against every
// offline adapter (InMemory, File); Redis/Mongo are the same ~15-line
// delegations and are exercised by the integration suite. The "put twice"
// case pins upsert/replace — the class of bug the old Mongo adapters had
// (InsertOneAsync + FirstOrDefault = append then read oldest).
public class KeyedBlobStoreTests
{
    private sealed class TempFile(string path) : IDisposable
    {
        public string Path { get; } = path;
        public void Dispose() { if (File.Exists(Path)) File.Delete(Path); }
    }

    private static (IKeyedBlobStore store, string key, IDisposable cleanup) Make(string kind)
    {
        if (kind == "file")
        {
            var temp = new TempFile(System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"wr-blob-{Guid.NewGuid():N}.txt"));
            return (new FileBlobStore(), temp.Path, temp);
        }

        return (new InMemoryBlobStore(), "k", new TempFile(""));
    }

    [Theory]
    [InlineData("inmemory")]
    [InlineData("file")]
    public async Task Put_then_Get_returns_the_value(string kind)
    {
        var (store, key, cleanup) = Make(kind);
        using (cleanup)
        {
            await store.PutAsync(key, "hello");
            Assert.Equal("hello", await store.GetAsync(key));
        }
    }

    [Theory]
    [InlineData("inmemory")]
    [InlineData("file")]
    public async Task Get_absent_key_returns_null(string kind)
    {
        var (store, key, cleanup) = Make(kind);
        using (cleanup)
            Assert.Null(await store.GetAsync(key));
    }

    [Theory]
    [InlineData("inmemory")]
    [InlineData("file")]
    public async Task Put_twice_replaces_and_does_not_append(string kind)
    {
        var (store, key, cleanup) = Make(kind);
        using (cleanup)
        {
            await store.PutAsync(key, "first");
            await store.PutAsync(key, "second");
            Assert.Equal("second", await store.GetAsync(key));
        }
    }

    [Theory]
    [InlineData("inmemory")]
    [InlineData("file")]
    public async Task Round_trips_unicode_and_multiline(string kind)
    {
        var (store, key, cleanup) = Make(kind);
        using (cleanup)
        {
            const string payload = "{\n  \"emoji\": \"🕷\",\n  \"ru\": \"привет\"\n}";
            await store.PutAsync(key, payload);
            Assert.Equal(payload, await store.GetAsync(key));
        }
    }

    // ADR-0011 bug fix, pinned through the public IKeyedBlobStore interface:
    // FileBlobStore's key IS the path; a key in a not-yet-existing directory
    // previously threw (no directory creation). FilePersistencePrep now
    // ensures the directory eagerly, so this round-trips.
    [Fact]
    public async Task FileBlobStore_Put_to_a_key_in_a_missing_directory_succeeds()
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"wr-blob-nested-{Guid.NewGuid():N}");
        try
        {
            var key = System.IO.Path.Combine(root, "nested", "config.json"); // dir absent
            var store = new FileBlobStore();

            await store.PutAsync(key, "v"); // previously threw DirectoryNotFoundException

            Assert.Equal("v", await store.GetAsync(key));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
