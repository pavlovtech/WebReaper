using System.Net;

namespace WebReaper.HttpRequests.Abstract;

public interface IPageRequester
{
    public CookieContainer CookieContainer { get; set; }
    Task<HttpResponseMessage> GetAsync(string url);
}