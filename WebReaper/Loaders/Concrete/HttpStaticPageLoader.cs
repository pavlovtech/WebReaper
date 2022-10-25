using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using WebReaper.Extensions;
using WebReaper.HttpRequests.Abstract;
using WebReaper.Loaders.Abstract;

namespace WebReaper.Loaders.Concrete;

public class HttpStaticPageLoader : IStaticPageLoader
{
    private ILogger logger;

    protected IHttpRequests Requests { get; }

    public HttpStaticPageLoader(IHttpRequests requests, ILogger logger)
    {
        ServicePointManager.DefaultConnectionLimit = int.MaxValue;
        ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        Requests = requests;
        this.logger = logger;
    }

    public async Task<string> Load(string url)
    {
        using var _ = logger.LogMethodDuration();
        // return await HttpClient.GetStringAsync(url);

        var response = await Requests.GetAsync(url);

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