using Azure.Messaging.ServiceBus;
using Newtonsoft.Json;
using System.Runtime.CompilerServices;
using ExoScraper.Domain;
using ExoScraper.Scheduler.Abstract;

namespace ExoScraper.Scheduler.Concrete;

public class AzureServiceBusScheduler : IScheduler, IAsyncDisposable
{
    private readonly ServiceBusClient _client;

    private readonly ServiceBusReceiver _receiver;

    private readonly ServiceBusSender _sender;

    public AzureServiceBusScheduler(string serviceBusConnectionString, string queueName)
    {
        _client = new(serviceBusConnectionString);

        _receiver = _client.CreateReceiver(queueName, new ServiceBusReceiverOptions()
        {
            PrefetchCount = 10
        });

        _sender = _client.CreateSender(queueName);
    }

    public async IAsyncEnumerable<Job> GetAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var msg in _receiver.ReceiveMessagesAsync(cancellationToken))
        {
            if (_receiver.IsClosed)
            {
                break;
            }

            await _receiver.CompleteMessageAsync(msg, cancellationToken);
            var stringBody = msg.Body.ToString();
            var job = JsonConvert.DeserializeObject<Job>(stringBody, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto
            });

            if (job is null)
            {
                continue;
            }

            yield return job;
        }
    }

    public async Task AddAsync(Job job, CancellationToken cancellationToken = default)
    {
        var msg = new ServiceBusMessage(SerializeToJson(job));
        await _sender.SendMessageAsync(msg, cancellationToken);
    }

    public async Task AddAsync(IEnumerable<Job> jobs, CancellationToken cancellationToken = default)
    {
        var messages = jobs.Select(job => new ServiceBusMessage(SerializeToJson(job)));
        await _sender.SendMessagesAsync(messages, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        await _sender.DisposeAsync();
        await _client.DisposeAsync();
    }

    private string SerializeToJson(Job job)
    {
        var json = JsonConvert.SerializeObject(job, Formatting.Indented, new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Auto
        });

        return json;
    }
}