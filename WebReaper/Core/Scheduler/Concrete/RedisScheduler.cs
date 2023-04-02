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

    public RedisScheduler(string connectionString, string queueName, ILogger logger) : base(connectionString)
    {
        _queueName = queueName;
        _logger = logger;
    }

    public async IAsyncEnumerable<Job> GetAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation($"Start {nameof(RedisScheduler)}.{nameof(GetAllAsync)}");

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
        _logger.LogInformation($"Start {nameof(RedisScheduler)}.{nameof(AddAsync)}");

        var db = Redis.GetDatabase();
        await db.ListRightPushAsync(_queueName, SerializeToJson(job));
    }

    public async Task AddAsync(IEnumerable<Job> jobs, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation($"Start {nameof(RedisScheduler)}.{nameof(AddAsync)} with multiple jobs");

        var db = Redis!.GetDatabase();

        foreach (var job in jobs) await db.ListRightPushAsync(_queueName, SerializeToJson(job));
    }
}