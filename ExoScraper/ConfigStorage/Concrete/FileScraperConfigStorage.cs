using ExoScraper.ConfigStorage.Abstract;
using Newtonsoft.Json;

namespace ExoScraper.ConfigStorage.Concrete;

public class FileScraperConfigStorage: IScraperConfigStorage
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
        return JsonConvert.DeserializeObject<ScraperConfig>(text);
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