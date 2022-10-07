using Azure.Messaging.ServiceBus;
using Newtonsoft.Json;
using WebReaper.Domain;
using WebReaper.Queue.Abstract;

namespace WebReaper.Queue.Concrete.AzureServiceBus;

public class AzureJobQueueWriter : IJobQueueWriter, IAsyncDisposable
{
    private ServiceBusClient client;
    private ServiceBusSender sender;

    public AzureJobQueueWriter(string serviceBusConnectionString, string queueName)
    {
        // create a Service Bus client
        client = new(serviceBusConnectionString);
        // create a sender for the queue 
        sender = client.CreateSender(queueName);
    }

    public async Task WriteAsync(params Job[] jobs)
    {
        var messages = jobs.Select(job => new ServiceBusMessage(SerializeToJson(job)));
        await sender.SendMessagesAsync(messages);
    }

    public async Task CompleteAddingAsync()
    {
        await sender.CloseAsync();
    }

    public async ValueTask DisposeAsync()
    {
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