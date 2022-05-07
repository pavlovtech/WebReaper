using Azure.Messaging.ServiceBus;
using Newtonsoft.Json;
using WebReaper.Abstractions.JobQueue;
using WebReaper.Domain;

namespace WebReaper.Queue.AzureServiceBus;

public class AzureJobQueueReader : IJobQueueReader
{
    private ServiceBusClient client;
    
    private ServiceBusReceiver receiver;

    public AzureJobQueueReader(string serviceBusConnectionString, string queueName)
    {
        client = new(serviceBusConnectionString);

        receiver = client.CreateReceiver(queueName, new ServiceBusReceiverOptions()
        {
            PrefetchCount = 0
        });
    }

    public async IAsyncEnumerable<Job> ReadAsync()
    {
        // await foreach (var msg in receiver.ReceiveMessagesAsync())
        // {
        //     Console.WriteLine("Reading");
        //     await receiver.CompleteMessageAsync(msg);
        // }

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
}