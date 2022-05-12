using Azure.Cosmos;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebReaper.Absctracts.Sinks;

namespace WebReaper.Sinks;

public class CosmosSink : IScraperSink
{
    protected string EndpointUrl { get; init; }
    protected string AuthorizationKey { get; init; }
    protected string DatabaseId { get; init; }
    protected string ContainerId { get; init; }

    protected CosmosClient CosmosClient { get; set; }
    protected CosmosContainer Container { get; set; }

    public bool IsInitialized { get; set; }

    public CosmosSink(
        string endpointUrl,
        string authorizationKey,
        string databaseId,
        string containerId)
    {
        EndpointUrl = endpointUrl;
        AuthorizationKey = authorizationKey;
        DatabaseId = databaseId;
        ContainerId = containerId;
    }

    public async Task InitAsync()
    {
        CosmosClient = new CosmosClient(EndpointUrl, AuthorizationKey);
        var databaseResponse = await CosmosClient.CreateDatabaseIfNotExistsAsync(DatabaseId);
        var database = databaseResponse.Database;
        var containerResp = await database.CreateContainerIfNotExistsAsync(ContainerId, "/id");
        Container = containerResp.Container;

        IsInitialized = true;
    }

    public async Task EmitAsync(JObject scrapedData)
    {
        if(!IsInitialized)
        {
            await InitAsync();
        }

       await Container.CreateItemAsync(JsonConvert.DeserializeObject(scrapedData.ToString()));
    }
}