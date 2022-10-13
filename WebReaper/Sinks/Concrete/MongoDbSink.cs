using Newtonsoft.Json.Linq;
using WebReaper.Sinks.Abstract;
using MongoDB.Bson;
using MongoDB.Driver;
using Microsoft.Extensions.Logging;

namespace WebReaper.Sinks.Concrete
{
    public class MongoDbSink : IScraperSink
    {
        public string ConnectionString { get; }
        public string CollectionName { get; }
        public string DatabaseName { get; }
        protected MongoClient Client { get; }
        protected ILogger Logger { get; }

        public MongoDbSink(string connectionString, string databaseName, string collectionName, ILogger logger)
        {
            ConnectionString = connectionString;
            CollectionName = collectionName;
            DatabaseName = databaseName;
            Client = new MongoClient(ConnectionString);
            Logger = logger;
        }

        public async Task EmitAsync(JObject scrapedData)
        {
            var database = Client.GetDatabase(DatabaseName);

            var collection = database.GetCollection<BsonDocument>(CollectionName);

            var document = BsonDocument.Parse(scrapedData.ToString());

            await collection.InsertOneAsync(document);
        }
    }
}
