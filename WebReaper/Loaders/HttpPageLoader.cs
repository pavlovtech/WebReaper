using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using WebReaper.Abstractions.Loaders.PageLoader;
using WebReaper.Extensions;

namespace WebReaper.Loaders;

public class HttpPageLoader : IPageLoader
{
    private ILogger logger;

    protected HttpClient HttpClient { get; }

    public HttpPageLoader(HttpClient httpClient, ILogger logger)
    {
        ServicePointManager.DefaultConnectionLimit = int.MaxValue;
        ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        HttpClient = httpClient;
        this.logger = logger;
    }

    public async Task<string> Load(string url)
    {
        using var _ = logger.LogMethodDuration();
        return await HttpClient.GetStringAsync(url);
    }
}