using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using WebReaper.Core.CookieStorage.Abstract;
using WebReaper.Core.Loaders.Abstract;
using WebReaper.Extensions;
using WebReaper.HttpRequests.Abstract;

namespace WebReaper.Core.Loaders.Concrete;

public class HttpStaticPageLoader : IStaticPageLoader
{
    private readonly ICookiesStorage _cookiesStorage;
    private readonly ILogger _logger;

    public HttpStaticPageLoader(IPageRequester pageRequester, ICookiesStorage cookiesStorage, ILogger logger)
    {
        ServicePointManager.DefaultConnectionLimit = int.MaxValue;
        ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        PageRequester = pageRequester;

        _cookiesStorage = cookiesStorage;
        _logger = logger;
    }

    private IPageRequester PageRequester { get; }

    public async Task<string> Load(string url)
    {
        using var _ = _logger.LogMethodDuration();
        // return await HttpClient.GetStringAsync(url);

        PageRequester.CookieContainer = await _cookiesStorage.GetAsync(); // TODO move to init factory func

        var response = await PageRequester.GetAsync(url);

        if (response.IsSuccessStatusCode) return await response.Content.ReadAsStringAsync();

        _logger.LogError("Failed to load page {url}. Error code: {statusCode}", url, response.StatusCode);

        throw new InvalidOperationException($"Failed to load page {url}. Error code: {response.StatusCode}")
        {
            Data = { ["url"] = url, ["statusCode"] = response.StatusCode }
        };
    }
}