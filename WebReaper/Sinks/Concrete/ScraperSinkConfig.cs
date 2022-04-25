using WebReaper.Scraper.Abstract;
using WebReaper.Sinks.Concrete;

namespace WebReaper.Scraper.Concrete
{
    public class ScraperSinkConfig
    {
        public IScraper Scraper { get; }

        public ScraperSinkConfig(IScraper scraper) =>
            this.Scraper = scraper;

        public IScraper Console() =>
            Scraper.AddSink(new ConsoleSink());

        public IScraper JsonFile(string filePath) =>
            Scraper.AddSink(new JsonFileSink(filePath));

        public IScraper CsvFile(string filePath) =>
            Scraper.AddSink(new CsvFileSink(filePath));
    }
}