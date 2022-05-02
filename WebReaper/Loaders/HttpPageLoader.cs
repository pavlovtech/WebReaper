using System.Net;
using System.Text;
using WebReaper.Abstractions.Loaders.PageLoader;

public class HttpPageLoader : IPageLoader
{
    protected HttpClient HttpClient { get; }

    public HttpPageLoader(HttpClient httpClient)
    {
        ServicePointManager.DefaultConnectionLimit = int.MaxValue;
        ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        HttpClient = httpClient;
    }

    public async Task<string> Load(string url)
    {
        return await HttpClient.GetStringAsync(url);
    }
}