using Microsoft.Azure.Cosmos;

namespace Styx.Api.Data
{
    public class DatabaseContext
    {
        public CosmosClient Client { get; }
        public Container UsersContainer { get; }
        public Container PostsContainer { get; }
        public Container CategoriesContainer { get; }
        public Container BadgesContainer { get; }

        public DatabaseContext()
        {
            var cosmosConnection = Environment.GetEnvironmentVariable("CosmosDbConnectionSetting");
            Client = new CosmosClient(cosmosConnection);

            // Initialize containers once
            UsersContainer = Client.GetContainer("my-database", "users");
            PostsContainer = Client.GetContainer("my-database", "posts");
            CategoriesContainer = Client.GetContainer("my-database", "categories");
            BadgesContainer = Client.GetContainer("my-database", "badges");
        }
    }
}
