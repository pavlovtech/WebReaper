using System.Net;

namespace ExoScraper.HttpRequests.Abstract;

public interface IPageRequester
{
    Task<HttpResponseMessage> GetAsync(string url);
    public CookieContainer CookieContainer { get; set; }
}