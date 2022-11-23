﻿namespace Exoscan.Configuration.Concrete;

public class InMemoryScraperConfigStorage: IScraperConfigStorage
{
    private ScraperConfig _config;

    public async Task CreateConfigAsync(ScraperConfig config) => _config = config;

    public async Task<ScraperConfig> GetConfigAsync() => _config;
}