using System.Net;
using System.Text;
using Exoscan.CookieStorage.Abstract;
using Exoscan.HttpRequests.Abstract;
using Exoscan.Loaders.Abstract;
using Microsoft.Extensions.Logging;
using Exoscan.Extensions;

namespace Exoscan.Loaders.Concrete;

public class HttpStaticPageLoader : IStaticPageLoader
{
    private readonly ICookiesStorage _cookiesStorage;
    private readonly ILogger _logger;

    private IPageRequester PageRequester { get; }

    public HttpStaticPageLoader(IPageRequester pageRequester, ICookiesStorage cookiesStorage, ILogger logger)
    {
        ServicePointManager.DefaultConnectionLimit = int.MaxValue;
        ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        PageRequester = pageRequester;
        
        _cookiesStorage = cookiesStorage;
        _logger = logger;
    }

    public async Task<string> Load(string url)
    {
        using var _ = _logger.LogMethodDuration();
        // return await HttpClient.GetStringAsync(url);
        
        PageRequester.CookieContainer = await _cookiesStorage.GetAsync(); // TODO move to init factory func
        
        var response = await PageRequester.GetAsync(url);

        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadAsStringAsync();
        }
        else
        {
            _logger.LogError("Failed to load page {url}. Error code: {statusCode}", url, response.StatusCode);

            throw new InvalidOperationException($"Failed to load page {url}. Error code: {response.StatusCode}")
            {
                Data = { ["url"] = url, ["statusCode"] = response.StatusCode }
            };
        }
    }
}