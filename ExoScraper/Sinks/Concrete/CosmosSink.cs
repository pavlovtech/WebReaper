using ExoScraper.Sinks.Abstract;
using ExoScraper.Sinks.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace ExoScraper.Sinks.Concrete;

public class CosmosSink : IScraperSink
{
    private string EndpointUrl { get; init; }
    private string AuthorizationKey { get; init; }
    private string DatabaseId { get; init; }
    private string ContainerId { get; init; }
    private ILogger Logger { get; }
    private Container? Container { get; set; }

    public Task Initialization { get; private set; }
    
    private async Task InitializeAsync()
    {
        var cosmosClient = new CosmosClient(EndpointUrl, AuthorizationKey);
        var databaseResponse = await cosmosClient.CreateDatabaseIfNotExistsAsync(DatabaseId);
        var database = databaseResponse.Database;

        // create container
        var containerResp = await database.CreateContainerIfNotExistsAsync(ContainerId, "/id");
        Container = containerResp.Container;
    }

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

        Initialization = InitializeAsync();
    }
    
    public async Task EmitAsync(ParsedData entity, CancellationToken cancellationToken = default)
    {
        await Initialization; // make sure that initialization finished

        var id = Guid.NewGuid().ToString();
        entity.Data["id"] = id;
        entity.Data["url"] = entity.Url;

        try
        {
            await Container!.CreateItemAsync(entity.Data, new PartitionKey(id), null, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error writing to CosmosDB");
            throw;
        }
    }
}