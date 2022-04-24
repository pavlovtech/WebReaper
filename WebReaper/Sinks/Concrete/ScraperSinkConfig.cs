using WebReaper.Scraper.Abstract;
using WebReaper.Sinks.Concrete;

namespace WebReaper.Scraper.Concrete
{
    public class ScraperSinkConfig
    {
        public IScraper Scraper { get; }

        public ScraperSinkConfig(IScraper scraper)
        {
            this.Scraper = scraper;

        }
        public IScraper Console()
        {
            Scraper.AddSink(new ConsoleSink());
            return Scraper;
        }

        public IScraper File(string filePath)
        {
            Scraper.AddSink(new FileSink(filePath));
            return Scraper;
        }
    }
}