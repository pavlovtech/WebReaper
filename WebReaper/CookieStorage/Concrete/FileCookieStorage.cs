using System.Net;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using WebReaper.CookieStorage.Abstract;

namespace WebReaper.CookieStorage.Concrete;

public class FileCookieStorage: ICookiesStorage
{
    private readonly string _fileName;
    private readonly ILogger _logger;

    public FileCookieStorage(string fileName, ILogger logger)
    {
        _fileName = fileName;
        _logger = logger;
    }
    
    public async Task AddAsync(CookieContainer cookieContainer, TimeSpan timeToLive)
    {
        await File.WriteAllTextAsync(_fileName, JsonConvert.SerializeObject(cookieContainer.GetAllCookies()));
    }

    public async Task<CookieContainer> GetAsync()
    {
        var json = await File.ReadAllTextAsync(_fileName);
        var result = JsonConvert.DeserializeObject<CookieCollection>(json);
        var container = new CookieContainer();
        container.Add(result);
        return container;
    }
}