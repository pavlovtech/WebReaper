using Azure.Messaging.ServiceBus;
using Newtonsoft.Json;
using WebReaper.Domain;

namespace WebReaper.Core;

public class AzureServiceBusScheduler : IScheduler
{
    private ServiceBusClient client;

    private ServiceBusReceiver receiver;

    private ServiceBusSender sender;

    public AzureServiceBusScheduler(string serviceBusConnectionString, string queueName)
    {
        client = new(serviceBusConnectionString);

        receiver = client.CreateReceiver(queueName, new ServiceBusReceiverOptions()
        {
            PrefetchCount = 10
        });

        sender = client.CreateSender(queueName);
    }

    public async ValueTask<Job> Get()
    {
        var msg = await receiver.ReceiveMessageAsync();
        await receiver.CompleteMessageAsync(msg);
        var stringBody = msg.Body.ToString();
        var job = JsonConvert.DeserializeObject<Job>(stringBody, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto
            });

        return job;
    }

    public async IAsyncEnumerable<Job> GetAll()
    {
        await foreach (var msg in receiver.ReceiveMessagesAsync())
        {
            await receiver.CompleteMessageAsync(msg);
            var stringBody = msg.Body.ToString();
            var job = JsonConvert.DeserializeObject<Job>(stringBody, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto
            });

            yield return job;
        }
    }

    public async ValueTask Schedule(Job job)
    {
        var msg = new ServiceBusMessage(SerializeToJson(job));
        await sender.SendMessageAsync(msg);
    }

    public async ValueTask Schedule(IEnumerable<Job> jobs)
    {
        var messages = jobs.Select(job => new ServiceBusMessage(SerializeToJson(job)));
        await sender.SendMessagesAsync(messages);
    }

    public async ValueTask DisposeAsync()
    {
        await sender.DisposeAsync();
        await client.DisposeAsync();
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