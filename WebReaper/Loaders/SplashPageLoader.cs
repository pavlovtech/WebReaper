//curl 'http://localhost:8050/render.html?url=http://kniga.io&timeout=10&wait=0.5'

using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using WebReaper.Abstractions.Loaders.PageLoader;
using WebReaper.Extensions;

namespace WebReaper.Loaders;

public class SplashPageLoader : IPageLoader
{
    protected HttpClient HttpClient { get; }
    private ILogger _logger { get; }

    public SplashPageLoader(HttpClient httpClient, ILogger logger)
    {
        ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        HttpClient = httpClient;
        _logger = logger;
    }

    public async Task<string> Load(string url)
    {
        using var _ = _logger.LogMethodDuration();
        var result = await HttpClient.GetStringAsync($"http://localhost:8050/render.html?url={url}");
        return result;
    }
}