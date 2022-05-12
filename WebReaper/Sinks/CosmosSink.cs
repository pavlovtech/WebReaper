using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebReaper.Absctracts.Sinks;

namespace WebReaper.Sinks;

public class CosmosSink : IScraperSink
{
    private readonly object lockObject = new();

    protected string EndpointUrl { get; init; }
    protected string AuthorizationKey { get; init; }
    protected string DatabaseId { get; init; }
    protected string ContainerId { get; init; }
    protected ILogger Logger { get; }
    protected CosmosClient CosmosClient { get; set; }
    protected Container Container { get; set; }

    public bool IsInitialized { get; set; }

    public CosmosSink(
        string endpointUrl,
        string authorizationKey,
        string databaseId,
        string containerId,
        ILogger logger)
    {
        EndpointUrl = endpointUrl;
        AuthorizationKey = authorizationKey;
        DatabaseId = databaseId;
        ContainerId = containerId;
        Logger = logger;
    }

    public async Task InitAsync()
    {
        Logger.LogInformation("Initializing CosmosSink...");

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

        var id = Guid.NewGuid().ToString();
        scrapedData["id"] = id;
        
        try
        {
            await Container.CreateItemAsync(scrapedData, new PartitionKey(id));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error writing to CosmosDB");
            throw;
        }
    }
}