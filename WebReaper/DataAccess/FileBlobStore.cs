namespace WebReaper.DataAccess;

/// <summary>
/// File-backed <see cref="IKeyedBlobStore"/>. The key <em>is</em> the file
/// path — this preserves the historical
/// <c>new FileScraperConfigStorage("config.json")</c> behaviour of writing to
/// exactly that file. UTF-8, no BOM (the <see cref="File"/> default).
/// </summary>
public class FileBlobStore : IKeyedBlobStore
{
    public Task PutAsync(string key, string value)
        => File.WriteAllTextAsync(key, value);

    public async Task<string?> GetAsync(string key)
        => File.Exists(key) ? await File.ReadAllTextAsync(key) : null;
}
