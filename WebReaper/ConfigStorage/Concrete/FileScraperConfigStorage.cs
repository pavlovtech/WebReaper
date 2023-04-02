using Newtonsoft.Json;
using WebReaper.ConfigStorage.Abstract;
using WebReaper.Domain;

namespace WebReaper.ConfigStorage.Concrete;

/// <inheritdoc />
public class FileScraperConfigStorage : IScraperConfigStorage
{
    private readonly string _fileName;

    public FileScraperConfigStorage(string fileName)
    {
        _fileName = fileName;
    }

    public async Task CreateConfigAsync(ScraperConfig config)
    {
        await File.WriteAllTextAsync(_fileName, SerializeToJson(config));
    }

    public async Task<ScraperConfig> GetConfigAsync()
    {
        var text = await File.ReadAllTextAsync(_fileName);
        var config = JsonConvert.DeserializeObject<ScraperConfig>(text);

        if (config is null)
            throw new NullReferenceException($"Error during config deserialization from {_fileName}");

        return config;
    }

    private string SerializeToJson(ScraperConfig config)
    {
        var json = JsonConvert.SerializeObject(config, Formatting.Indented, new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Auto,
            NullValueHandling = NullValueHandling.Ignore,
            TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple
        });

        return json;
    }
}