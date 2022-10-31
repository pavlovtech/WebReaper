using System.Text;
using WebReaper.Parser.Concrete;
using Xunit.Abstractions;

namespace WebReaper.UnitTests
{
    public class ParserTests
    {
        private readonly TestOutputLogger logger;

        public ParserTests(ITestOutputHelper output)
        {
            logger = new TestOutputLogger(output);
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        [Fact]
        public void ParserSimpleTest()
        {
            var parser = new ContentParser(logger);
            var page = File.ReadAllText("TestData\\TestPage.html", Encoding.GetEncoding("windows-1251"));
            var result = parser.Parse(page, new()
            {
                new("name", "#topic-title"),
                new("category", "td.nav.t-breadcrumb-top.w100.pad_2>a:nth-child(3)"),
                new("subcategory", "td.nav.t-breadcrumb-top.w100.pad_2>a:nth-child(5)"),
                new("torrentSize", "div.attach_link.guest>ul>li:nth-child(2)"),
                new("torrentLink", ".magnet-link", "href"),
                new("coverImageUrl", ".postImg", "src")
            });

            Assert.Equal("Эшбах Андреас - Выжжено [Оробчук Сергей, (ЛИ), 2022, 128 kbps, MP3]", result["name"]?.ToString());
            Assert.Equal("Фантастика, фэнтези, мистика, ужасы, фанфики", result["category"]?.ToString());
            Assert.Equal("[Аудио] Зарубежная фантастика, фэнтези, мистика, ужасы, фанфики", result["subcategory"]?.ToString());
            Assert.Equal("1.07 GB", result["torrentSize"]?.ToString());
            Assert.Equal("magnet:?xt=urn:btih:462F275C47EA0608B25BB8DFB1E202BDAFC68DE7&tr=http%3A%2F%2Fbt4.t-ru.org%2Fann%3Fmagnet", result["torrentLink"]?.ToString());
            Assert.Equal("https://www.hostpic.org/images/2210182146080117.jpg", result["coverImageUrl"]?.ToString());
        }

        [Fact]
        public void ParserSimpleHtmlParsingTest()
        {
            var parser = new ContentParser(logger);
            var page = File.ReadAllText("TestData/TestPage.html", Encoding.GetEncoding("windows-1251"));
            var result = parser.Parse(page, new()
            {
                new("link", ".attach_link.guest", true)
            });

            var expectedHtml = "\r\n\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t<ul class=\"inlined middot-separated\">\r\n\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t<li>\r\n\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t<a href=\"magnet:?xt=urn:btih:462F275C47EA0608B25BB8DFB1E202BDAFC68DE7&tr=http%3A%2F%2Fbt4.t-ru.org%2Fann%3Fmagnet\" class=\"magnet-link\" data-topic_id=\"6273642\">\r\n\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t<img src=\"https://static.t-ru.org/templates/v1/images/magnet_1.svg\" alt=\"\">Скачать раздачу по magnet-ссылке\r\n\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t</a>\r\n\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t</li>\r\n\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t<li>1.07 GB</li>\r\n\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t</ul>\r\n\t\t\t\t\t\t\t\t\t\t\t\t\t\t";

            Assert.Equal(expectedHtml, result["link"]?.ToString());
        }
    }
}