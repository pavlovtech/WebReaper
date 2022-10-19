using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Amqp.Framing;
using Newtonsoft.Json;
using System.Runtime.CompilerServices;
using WebReaper.Domain;
using WebReaper.Scheduler.Abstract;

namespace WebReaper.Scheduler.Concrete;

public class AzureServiceBusScheduler : IScheduler
{
    protected ServiceBusClient client;

    protected ServiceBusReceiver receiver;

    protected ServiceBusSender sender;

    public AzureServiceBusScheduler(string serviceBusConnectionString, string queueName)
    {
        client = new(serviceBusConnectionString);

        receiver = client.CreateReceiver(queueName, new ServiceBusReceiverOptions()
        {
            PrefetchCount = 10
        });

        sender = client.CreateSender(queueName);
    }

    public async ValueTask<Job> GetAsync(CancellationToken cancellationToken = default)
    {
        var msg = await receiver.ReceiveMessageAsync(null, cancellationToken);
        await receiver.CompleteMessageAsync(msg, cancellationToken);
        var stringBody = msg.Body.ToString();
        var job = JsonConvert.DeserializeObject<Job>(stringBody, new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Auto
        });

        return job;
    }

    public async IAsyncEnumerable<Job> GetAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var msg in receiver.ReceiveMessagesAsync(cancellationToken))
        {
            if (receiver.IsClosed)
            {
                break;
            }

            await receiver.CompleteMessageAsync(msg, cancellationToken);
            var stringBody = msg.Body.ToString();
            var job = JsonConvert.DeserializeObject<Job>(stringBody, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto
            });

            yield return job;
        }
    }

    public async ValueTask AddAsync(Job job, CancellationToken cancellationToken = default)
    {
        var msg = new ServiceBusMessage(SerializeToJson(job));
        await sender.SendMessageAsync(msg, cancellationToken);
    }

    public async ValueTask AddAsync(IEnumerable<Job> jobs, CancellationToken cancellationToken = default)
    {
        var messages = jobs.Select(job => new ServiceBusMessage(SerializeToJson(job)));
        await sender.SendMessagesAsync(messages, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await sender.DisposeAsync();
        await client.DisposeAsync();
    }

    protected string SerializeToJson(Job job)
    {
        var json = JsonConvert.SerializeObject(job, Formatting.Indented, new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Auto
        });

        return json;
    }
}