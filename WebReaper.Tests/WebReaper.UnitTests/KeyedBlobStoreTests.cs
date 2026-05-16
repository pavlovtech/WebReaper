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
}
