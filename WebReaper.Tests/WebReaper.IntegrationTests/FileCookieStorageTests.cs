using System.Net;
using WebReaper.CookieStorage.Concrete;
using Xunit.Abstractions;

namespace WebReaper.IntegrationTests
{
    public class FileCookieStorageTests
    {
        private readonly TestOutputLogger logger;

        public FileCookieStorageTests(ITestOutputHelper output)
        {
            logger = new TestOutputLogger(output);
        }

        [Fact]
        public async Task FileCookieStorage_Serializes_And_Deserializes()
        {
            var storage = new FileCookieStorage("cookies.txt", logger);

            var cookies = new CookieContainer();
            cookies.Add(new Cookie("test", "1")
            {
                Domain = "localhost"
            });
            
            await storage.AddAsync(cookies, TimeSpan.FromMinutes(1));
            var result = await storage.GetAsync();
            
            Assert.Equal(1,result.Count);
        }
    }
}