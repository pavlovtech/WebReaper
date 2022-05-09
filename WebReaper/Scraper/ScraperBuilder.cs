using System.Net;
using WebReaper.Domain;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net.Security;
using WebReaper.Domain.Selectors;
using WebReaper.Absctracts.Sinks;
using WebReaper.Abastracts.Spider;
using WebReaper.Abstractions.Parsers;
using WebReaper.LinkTracker.Abstract;
using WebReaper.Abstractions.JobQueue;
using WebReaper.Parser;
using WebReaper.Queue;
using WebReaper.Sinks;
using WebReaper.Domain.Parsing;
using WebReaper.LinkTracker;
using WebReaper.Loaders;
using Microsoft.Extensions.Logging.Abstractions;
using WebReaper.Queue.InMemory;
using WebReaper.Spiders;

namespace WebReaper.Scraper;

public class ScraperBuilder
{
    protected ScraperConfigBuilder ConfigBuilder { get; set; }
    protected SpiderBuilder SpiderBuilder { get; set; }
    protected ScraperRunner Runner { get; set; }
}
