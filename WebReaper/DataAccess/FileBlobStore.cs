namespace WebReaper.DataAccess;

/// <summary>
/// File-backed <see cref="IKeyedBlobStore"/>. The key <em>is</em> the file
/// path — this preserves the historical
/// <c>new FileScraperConfigStorage("config.json")</c> behaviour of writing to
/// exactly that file. UTF-8, no BOM (the <see cref="File"/> default).
///
/// Directory creation and the missing-file policy are delegated to
/// <see cref="FilePersistencePrep"/> (ADR-0011); whole-file replace
/// (last-write-wins) is this adapter's own essence. No lock is taken — the
/// engine writes a config once at build time.
/// </summary>
public class FileBlobStore : IKeyedBlobStore
{
    public Task PutAsync(string key, string value)
    {
        FilePersistencePrep.EnsureDirectory(key);
        return File.WriteAllTextAsync(key, value);
    }

    public Task<string?> GetAsync(string key)
        => FilePersistencePrep.ReadAllTextOrNullAsync(key);
}
