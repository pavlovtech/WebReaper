namespace Exoscan.Configuration;

public interface IScraperConfigStorage
{
    Task CreateConfigAsync(ScraperConfig config);
    Task<ScraperConfig> GetConfigAsync();
}