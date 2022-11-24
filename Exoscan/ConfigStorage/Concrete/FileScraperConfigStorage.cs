using Exoscan.ConfigStorage.Abstract;
using Newtonsoft.Json;

namespace Exoscan.ConfigStorage.Concrete;

public class FileScraperConfigStorage: IScraperConfigStorage
{
    private readonly string _fileName;

    public FileScraperConfigStorage(string fileName)
    {
        _fileName = fileName;
    }
    
    public async Task CreateConfigAsync(ScraperConfig config)
    {
        await File.WriteAllTextAsync(_fileName, config.ToJson());
    }

    public async Task<ScraperConfig> GetConfigAsync()
    {
        var text = await File.ReadAllTextAsync(_fileName);
        return JsonConvert.DeserializeObject<ScraperConfig>(text);
    }
}