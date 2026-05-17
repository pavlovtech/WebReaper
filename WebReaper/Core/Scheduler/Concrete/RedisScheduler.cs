using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using WebReaper.Core.Scheduler.Abstract;
using WebReaper.DataAccess;
using WebReaper.Domain;
using WebReaper.Serialization;

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

            var job = WebReaperJson.DeserializeJob(rawResult!);

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

    // ADR 0008 closes the ADR-0005 asymmetry: the Job is serialised and
    // deserialised through the SAME WebReaperJson grammar as the config
    // payload, so its ImmutableQueue selector chain, ImmutableQueue backlinks
    // and object[] PageAction.Parameters round-trip with full type fidelity.
    // The old TypeNameHandling.None serialize / default deserialize asymmetry
    // is now unrepresentable — there is no TypeNameHandling knob.
    private static string SerializeToJson(Job job) => WebReaperJson.SerializeJob(job);
}