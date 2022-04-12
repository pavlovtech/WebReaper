using WebReaper;

// await new Scraper("https://rutracker.org/forum/viewforum.php?f=402")
//     .FollowLinks(".torTopic.bold.tt-text")
//     .Paginate(".pg:contains('След.')")
//     .WithScheme(new WebEl[] {
//         new("title", "span[style='font-size: 24px; line-height: normal;']"),
//         new("image", ".postImg", JsonType.Image)
//     })
//     .Limit(100)
//     .To("output.json")
//     .Run();

await new Scraper("https://rutracker.org/forum/viewforum.php?f=402")
    .FollowLinks(".torTopic.bold.tt-text")
    .Paginate(".pg")
    .WithScheme(new WebEl[] {
        new("title", "span[style='font-size: 24px; line-height: normal;']"),
        new("image", ".postImg", JsonType.Image)
    })
    .Limit(10000)
    .To("output.json")
    .Run();