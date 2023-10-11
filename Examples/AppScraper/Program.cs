using WebReaper.Builders;

var engine = await new ScraperEngineBuilder()
    .Get(
        "https://www.olx.ua/uk/nedvizhimost/kvartiry/dolgosrochnaya-arenda-kvartir/kiev/q-%D0%BE%D1%82%D0%BA%D1%80%D1%8B%D1%82%D1%8B%D0%B9-%D0%B1%D0%B0%D0%BB%D0%BA%D0%BE%D0%BD/?currency=UAH&search%5Bfilter_float_price:from%5D=14000&search%5Bfilter_float_price:to%5D=18000&search%5Bfilter_float_total_area:from%5D=50",
        "https://www.olx.ua/uk/nedvizhimost/kvartiry/dolgosrochnaya-arenda-kvartir/kiev/q-%D0%B2%D1%96%D0%B4%D0%BA%D1%80%D0%B8%D1%82%D0%B8%D0%B9-%D0%B1%D0%B0%D0%BB%D0%BA%D0%BE%D0%BD/?currency=UAH&search%5Bfilter_float_price:from%5D=14000&search%5Bfilter_float_price:to%5D=18000&search%5Bfilter_float_total_area:from%5D=50"
        )
    .Follow(".css-rc5s2u")
    .Parse(new()
    {
        new("title", ".css-1dhh6hr.er34gjf0"),
        new("price", ".css-1twl9tf.er34gjf0"),
        new("date", ".css-19yf5ek"),
        new("description", ".css-1t507yq.er34gjf0")
    })
    .WriteToCsvFile("output.csv", true)
    .PageCrawlLimit(50)
    .WithParallelismDegree(4)
    .LogToConsole()
    .BuildAsync();

await engine.RunAsync();

Console.ReadLine();