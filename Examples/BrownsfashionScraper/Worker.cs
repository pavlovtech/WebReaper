using System.Net;
using WebReaper.Builders;
using WebReaper.Core;
using WebReaper.Domain.Selectors;

namespace BrownsfashionScraper
{
    public class ScrapingWorker : BackgroundService
    {
        private readonly ILogger<ScrapingWorker> _logger;
        private ScraperEngine _scraper;

        public ScrapingWorker(ILogger<ScrapingWorker> logger)
        {
            _logger = logger;
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
             _scraper = await new ScraperEngineBuilder()
                .WithLogger(_logger)
                .SetCookies(cookies =>
                {
                    cookies.Add(new CookieCollection
                    {
                        new Cookie("cf_clearance", "db1e4fa31a72a24313e520e2551757ae4977a8ca-1666375170-0-150", "/", ".brownsfashion.com"),
                        new Cookie("dfUserSub", "%2Fua", "/", "www.brownsfashion.com"),
                        new Cookie("benefit", "264CBB89ED4B5B752363C1AAF409FF", "/", "www.brownsfashion.com"),
                        new Cookie("csi", "23106c23-4520-41a4-9826-e6ee4f090384", "/", "www.brownsfashion.com"),
                        new Cookie("optimizelyEndUserId", "oeu1666375172161r0.640619021207925", "/", ".brownsfashion.com"),
                        new Cookie("__cfruid", "a6298bb06771abc6b53ad949db331f14b0eb31f8-1666375174", "/", ".brownsfashion.com"),
                        new Cookie("_gcl_au", "1.1.1208435839.1666375174", "/", ".brownsfashion.com"),
                        new Cookie("tms_VisitorID", "r1d2usr5ea", "/", "www.brownsfashion.com"),
                        new Cookie("_ga", "GA1.2.2077369448.1666375175", "/", ".brownsfashion.com"),
                        new Cookie("rskxRunCookie", "0", "/", ".brownsfashion.com"),
                        new Cookie("rCookie", "kswqgtt5wzklzomyxiy4l9isr606", "/", ".brownsfashion.com"),
                        new Cookie("_pin_unauth", "dWlkPVptVTRPREEzTnpjdE5qbGlPUzAwWkRGa0xXRXhOVEV0TlRWaU5tWmxNMk01WmpJdw", "/", ".brownsfashion.com"),
                        new Cookie("_tt_enable_cookie", "1", "/", ".brownsfashion.com"),
                        new Cookie("_ttp", "d298defd-8e90-4c05-93b5-9d917898cca9", "/", ".brownsfashion.com"),
                        new Cookie("_hjSessionUser_1781677", "eyJpZCI6IjhkZGRjZmY5LWIzOWUtNTZkZS05MjdmLTZjOTE0NDEyMWZkMyIsImNyZWF0ZWQiOjE2NjYzNzUxNzU2NzEsImV4aXN0aW5nIjp0cnVlfQ==", "/", ".brownsfashion.com"),
                        new Cookie("__cuid", "8c128d45cfce49a7b9241dba4af6c92b", "/", ".brownsfashion.com"),
                        new Cookie("ctx", "%7b%22u%22%3a5000016837932866%2c%22g%22%3a1%7d", "/", "www.brownsfashion.com"),
                        new Cookie("ss", "TfVVtCugoqw1mnsJv02cPbdKufumnKpP1BUIFhmCgRlTC2_vFiMUgUJAnTflkkzEfWLNVGKBFMq0VTt-WqM-jM9Bvrl34h_my7sDgx3HaBgeJ2IJr7e317xtxZehs7FE_IuTplUtNceLubtqp81JLyo3tFglfq3VwpJM9g5tB0H1IPjE0_vsa0ddZnHpT8-ikGBT605_5lzs_H6IsJ04jjfrgXko2WCtufsZYiOSE5g", "/", "www.brownsfashion.com"),
                        new Cookie("_gid", "GA1.2.1101342465.1666602930", "/", ".brownsfashion.com"),
                        new Cookie("_gat_UA-699627-7", "1", "/", ".brownsfashion.com"),
                        new Cookie("_uetsid", "687b1570537c11edaa82a773b24294d2", "/", ".brownsfashion.com"),
                        new Cookie("_uetvid", "1f425410516a11edac0de3557a6799c2", "/", ".brownsfashion.com"),
                        new Cookie("lastRskxRun", "1666602931619", "/", ".brownsfashion.com"),
                        new Cookie("forterToken", "88d88ed652bd4dda83cbb35faf2c9c0f_1666602928885__UDF43_11ck", "/", ".brownsfashion.com"),
                        new Cookie("tms_wsip", "1", "/", "www.brownsfashion.com"),
                        new Cookie("_hjIncludedInSessionSample", "0", "/", "www.brownsfashion.com"),
                        new Cookie("_hjSession_1781677", "eyJpZCI6ImE2N2I5N2Q5LTEzZTctNDA0YS1iNGEzLTc2MDhhMDAwODZkZSIsImNyZWF0ZWQiOjE2NjY2MDI5MzE4ODIsImluU2FtcGxlIjpmYWxzZX0=", "/", ".brownsfashion.com"),
                        new Cookie("_hjAbsoluteSessionInProgress", "0", "/", ".brownsfashion.com"),
                        new Cookie("__cf_bm", "zaLmrlghpaPoNgnXfER47CGmGLF1humo0N6PBN4vjN8-1666602931-0-AQNY7LFfGAg1YVwxWCU91YtsPElDOBPewJUFemHqJaHdrNL+N/zDiQ+E/AWKjgZK1LtGQTQu4L0yGSXwbRq+l8ZKKfxH8hPHX+SGwolgOj0s9OTb/bniUG8V9mZk/aoAwBVSUHMzH9nfKaC+rL23wmTa/jqp2Jf5jlFdovXpgbnN", "/", ".brownsfashion.com"),
                        new Cookie("_clck", "1pm5gys|1|f5z|0", "/", ".brownsfashion.com"),
                        new Cookie("_clsk", "uhygul|1666602933570|1|1|e.clarity.ms/collect", "/", ".brownsfashion.com")
                    });
                })
                .Get("https://www.brownsfashion.com/ua/shopping/man-clothing")
                .PaginateWithBrowser("._1GX2o>a", ".AlEkI")
                .Parse(new()
                {
                    new("brand", "a[data-test=\"product-brand\"]"),
                    new("product", "span[data-test=\"product-name\"]"),
                    new("price", "span[data-test=\"product-price\"]"),
                    new("description", "span[data-test=\"product-accordionOption\"]"),
                    new("category", "#modal-controller-container > main > nav > ol > li:nth-child(1) > a"),
                    new("subcategory", "#modal-controller-container > main > nav > ol > li:nth-child(2) > a"),
                    new("subcategory2", "#modal-controller-container > main > nav > ol > li:nth-child(3) > a]"),

                })
                .WriteToCsvFile("result.csv", true)
                .WithParallelismDegree(10)
                .BuildAsync();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await _scraper.RunAsync(stoppingToken);
        }
    }
}