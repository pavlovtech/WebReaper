using System.Net;

namespace WebReaper.Loaders.Abstract
{
    public interface IHttpRequests
    {
        Task<HttpResponseMessage> GetAsync(string url);
        public CookieContainer CookieContainer { get; set; }
    }
}
