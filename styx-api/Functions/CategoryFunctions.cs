using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Styx.Api.Data;
using Styx.Api.Models;

namespace Styx.Api.Functions
{
    public class CategoryFunctions
    {
        private readonly DatabaseContext _dbContext;

        public CategoryFunctions(DatabaseContext dbContext)
        {
            _dbContext = dbContext;
        }

        [Function("GetCategories")]
        public async Task<HttpResponseData> GetCategories(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "categories")]
                HttpRequestData req,
            FunctionContext executionContext
        )
        {
            var logger = executionContext.GetLogger("GetCategories");
            logger.LogInformation("Fetching all categories.");

            var response = req.CreateResponse();

            try
            {
                // Query to fetch all items
                var query = new QueryDefinition("SELECT * FROM c");
                var queryResultSetIterator =
                    _dbContext.CategoriesContainer.GetItemQueryIterator<Category>(query);

                var categories = new List<Category>();

                while (queryResultSetIterator.HasMoreResults)
                {
                    var categoriesResponse = await queryResultSetIterator.ReadNextAsync();
                    categories.AddRange(categoriesResponse);
                }

                if (categories.Any())
                {
                    response.StatusCode = HttpStatusCode.OK;
                    response.Headers.Add("Content-Type", "application/json; charset=utf-8");
                    await response.WriteStringAsync(JsonSerializer.Serialize(categories));
                    return response;
                }

                response.StatusCode = HttpStatusCode.NotFound;
                await response.WriteStringAsync("No categories found.");
            }
            catch (CosmosException ex)
            {
                logger.LogError($"Cosmos DB error: {ex.Message}");
                response.StatusCode = HttpStatusCode.InternalServerError;
                await response.WriteStringAsync("An error occurred while fetching categories.");
            }

            return response;
        }
    }
}
