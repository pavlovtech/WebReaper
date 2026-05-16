using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;
using WebReaper.Core.Scheduler.Abstract;
using WebReaper.DataAccess;
using WebReaper.Domain;

namespace WebReaper.Core.Scheduler.Concrete;

public class RedisScheduler : IScheduler
{
    private readonly IDatabase _db;
    private readonly ILogger _logger;
    private readonly string _queueName;

    public bool DataCleanupOnStart { get; set; }

    public Task Initialization { get; }

    public RedisScheduler(string connectionString, string queueName, ILogger logger, bool dataCleanupOnStart = false)
    {
        _db = RedisConnectionPool.GetDatabase(connectionString);
        DataCleanupOnStart = dataCleanupOnStart;
        _queueName = queueName;
        _logger = logger;
        
        Initialization = InitializeAsync();
    }
    
    private async Task InitializeAsync()
    {
        if (!DataCleanupOnStart)
            return;

        var db = _db;

        var result = await db.KeyDeleteAsync(_queueName);
    }

    public async IAsyncEnumerable<Job> GetAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Start {class}.{method}", nameof(RedisScheduler), nameof(GetAllAsync));

        var db = _db;

        while (!cancellationToken.IsCancellationRequested)
        {
            var rawResult = await db.ListLeftPopAsync(_queueName);

            if (!rawResult.HasValue)
            {
                await Task.Delay(300, cancellationToken);
                continue;
            }

            var job = JsonConvert.DeserializeObject<Job>(rawResult);

            yield return job;
        }
    }

    public async Task AddAsync(Job job, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Start {class}.{method}", nameof(RedisScheduler), nameof(AddAsync));

        var db = _db;
        await db.ListRightPushAsync(_queueName, SerializeToJson(job));
    }

    public async Task AddAsync(IEnumerable<Job> jobs, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Start {class}.{method} with {count} jobs", nameof(RedisScheduler), nameof(AddAsync), jobs.Count());

        var db = _db;

        foreach (var job in jobs)
        {
            await db.ListRightPushAsync(_queueName, SerializeToJson(job));
        }
    }

    // Moved verbatim from the removed RedisBase (its only caller). Preserved
    // as-is: TypeNameHandling.None means a Job's ImmutableQueue selector chain
    // and PageAction.Parameters are not round-tripped with type metadata here
    // (it deserializes with defaults) — the same serialize/deserialize
    // asymmetry ADR 0003 fixed for the config path, untouched in this
    // RedisBase retirement and flagged as a separate finding (ADR 0005).
    private static string SerializeToJson(object config)
    {
        var json = JsonConvert.SerializeObject(config, Formatting.Indented, new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None,
            NullValueHandling = NullValueHandling.Ignore,
            TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple
        });

        return json;
    }
}