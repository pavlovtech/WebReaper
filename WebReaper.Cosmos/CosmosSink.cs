using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using WebReaper.Sinks.Abstract;
using WebReaper.Sinks.Models;

namespace WebReaper.Cosmos;

public class CosmosSink : IScraperSink
{
    public CosmosSink(
        string endpointUrl,
        string authorizationKey,
        string databaseId,
        string containerId,
        bool dataCleanupOnStart,
        ILogger logger)
    {
        EndpointUrl = endpointUrl;
        AuthorizationKey = authorizationKey;
        DatabaseId = databaseId;
        ContainerId = containerId;
        DataCleanupOnStart = dataCleanupOnStart;
        Logger = logger;

        Initialization = InitializeAsync();
    }

    private string EndpointUrl { get; }
    private string AuthorizationKey { get; }
    private string DatabaseId { get; }
    private string ContainerId { get; }
    private ILogger Logger { get; }
    private Container? Container { get; set; }

    public Task Initialization { get; }

    public bool DataCleanupOnStart { get; set; }

    public async Task EmitAsync(ParsedData entity, CancellationToken cancellationToken = default)
    {
        await Initialization; // make sure that initialization finished

        // ADR-0031: the page URL is already folded into entity.Data by
        // ParsedData's construction. The "id" write is Cosmos-specific (the
        // /id partition key) and lands on this sink's own clone of Data.
        var id = Guid.NewGuid().ToString();
        entity.Data["id"] = id;

        // ADR 0008: the Cosmos SDK's default serializer is Newtonsoft and
        // serialises a Newtonsoft JObject natively but not a System.Text.Json
        // JsonObject. Bridge here, locally — CosmosSink is the documented
        // out-of-scope / not-AOT-guaranteed optional sink (ADR 0008 Bounded
        // scope) and, per ADR 0009, lives in the WebReaper.Cosmos satellite so
        // the Newtonsoft + Cosmos dependency stays off the core graph.
        var item = JObject.Parse(entity.Data.ToJsonString());

        try
        {
            await Container!.CreateItemAsync(item, new PartitionKey(id), null, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error writing to CosmosDB");
            throw;
        }
    }

    private async Task InitializeAsync()
    {
        var cosmosClient = new CosmosClient(EndpointUrl, AuthorizationKey);
        var databaseResponse = await cosmosClient.CreateDatabaseIfNotExistsAsync(DatabaseId);
        var database = databaseResponse.Database;

        if (DataCleanupOnStart)
        {
            var container = database.GetContainer(ContainerId);
            container?.DeleteContainerAsync();
        }

        // create container
        var containerResp = await database.CreateContainerIfNotExistsAsync(ContainerId, "/id");
        Container = containerResp.Container;
    }
}
