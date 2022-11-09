using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using WebReaper.Extensions;
using WebReaper.HttpRequests.Abstract;
using WebReaper.Loaders.Abstract;

namespace WebReaper.Loaders.Concrete;

public class HttpStaticPageLoader : IStaticPageLoader
{
    private readonly ILogger _logger;

    private IPageRequester Requests { get; }

    public HttpStaticPageLoader(IPageRequester requests, ILogger logger)
    {
        ServicePointManager.DefaultConnectionLimit = int.MaxValue;
        ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        Requests = requests;
        _logger = logger;
    }

    public async Task<string> Load(string url)
    {
        using var _ = _logger.LogMethodDuration();
        // return await HttpClient.GetStringAsync(url);

        var response = await Requests.GetAsync(url);

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