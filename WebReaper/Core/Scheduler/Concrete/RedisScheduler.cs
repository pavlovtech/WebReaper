using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using WebReaper.Core.Scheduler.Abstract;
using WebReaper.DataAccess;
using WebReaper.Domain;

namespace WebReaper.Core.Scheduler.Concrete;

public class RedisScheduler : RedisBase, IScheduler
{
    private readonly ILogger _logger;
    private readonly string _queueName;

    public bool DataCleanupOnStart { get; set; }
    
    public Task Initialization { get; }
    
    public RedisScheduler(string connectionString, string queueName, ILogger logger, bool dataCleanupOnStart = false) : base(connectionString)
    {
        DataCleanupOnStart = dataCleanupOnStart;
        _queueName = queueName;
        _logger = logger;
        
        Initialization = InitializeAsync();
    }
    
    private async Task InitializeAsync()
    {
        if (!DataCleanupOnStart)
            return;

        var db = Redis.GetDatabase();

        var result = await db.KeyDeleteAsync(_queueName);
    }

    public async IAsyncEnumerable<Job> GetAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Start {class}.{method}", nameof(RedisScheduler), nameof(GetAllAsync));

        var db = Redis.GetDatabase();

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

        var db = Redis.GetDatabase();
        await db.ListRightPushAsync(_queueName, SerializeToJson(job));
    }

    public async Task AddAsync(IEnumerable<Job> jobs, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Start {class}.{method} with {count} jobs", nameof(RedisScheduler), nameof(AddAsync), jobs.Count());

        var db = Redis!.GetDatabase();

        foreach (var job in jobs)
        {
            await db.ListRightPushAsync(_queueName, SerializeToJson(job));
        }
    }
}