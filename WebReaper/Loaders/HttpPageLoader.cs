using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using WebReaper.Abstractions.Loaders;
using WebReaper.Core.Extensions;

namespace WebReaper.Core.Loaders;

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
        // return await HttpClient.GetStringAsync(url);

        var response = await HttpClient.GetAsync(url);

        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadAsStringAsync();
        }
        else
        {
            logger.LogError("Failed to load page {url}. Error code: {statusCode}", url, response.StatusCode);

            throw new InvalidOperationException($"Failed to load page {url}. Error code: {response.StatusCode}")
            {
                Data = { ["url"] = url, ["statusCode"] = response.StatusCode }
            };
        }
    }
}