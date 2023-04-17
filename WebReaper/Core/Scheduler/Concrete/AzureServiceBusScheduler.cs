using System.Runtime.CompilerServices;
using Azure.Messaging.ServiceBus;
using Newtonsoft.Json;
using WebReaper.Core.Scheduler.Abstract;
using WebReaper.Domain;
using Azure.Messaging.ServiceBus.Administration;

namespace WebReaper.Core.Scheduler.Concrete;

public class AzureServiceBusScheduler : IScheduler, IAsyncDisposable
{
    private readonly string _queueName;
    private readonly ServiceBusClient _client;

    private readonly ServiceBusReceiver _receiver;

    private readonly ServiceBusSender _sender;
    private readonly ServiceBusAdministrationClient _adminClient;

    public bool DataCleanupOnStart { get; set; }
    
    public Task Initialization { get; }

    public AzureServiceBusScheduler(string serviceBusConnectionString, string queueName, bool dataCleanupOnStart = false)
    {
        _queueName = queueName;
        DataCleanupOnStart = dataCleanupOnStart;
        _client = new ServiceBusClient(serviceBusConnectionString);

        _receiver = _client.CreateReceiver(_queueName, new ServiceBusReceiverOptions
        {
            PrefetchCount = 10
        });

        _sender = _client.CreateSender(_queueName);

        _adminClient = new ServiceBusAdministrationClient(serviceBusConnectionString);

        Initialization = InitializeAsync();
    }
    
    private async Task InitializeAsync()
    {
        if (DataCleanupOnStart)
        {
            await _adminClient.DeleteQueueAsync(_queueName);          
            await _adminClient.CreateQueueAsync(_queueName);
        }
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        await _sender.DisposeAsync();
        await _client.DisposeAsync();
    }

    public async IAsyncEnumerable<Job> GetAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var msg in _receiver.ReceiveMessagesAsync(cancellationToken))
        {
            if (_receiver.IsClosed) break;

            await _receiver.CompleteMessageAsync(msg, cancellationToken);
            var stringBody = msg.Body.ToString();
            var job = JsonConvert.DeserializeObject<Job>(stringBody, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto
            });

            if (job is null) continue;

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

    private string SerializeToJson(Job job)
    {
        var json = JsonConvert.SerializeObject(job, Formatting.Indented, new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Auto
        });

        return json;
    }
}