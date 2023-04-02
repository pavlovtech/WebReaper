using WebReaper.Domain;

namespace WebReaper.ConfigStorage.Abstract;

public interface IScraperConfigStorage
{
    Task CreateConfigAsync(ScraperConfig config);

    Task<ScraperConfig> GetConfigAsync();
}