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
    public class BadgeFunctions
    {
        private readonly DatabaseContext _dbContext;

        public BadgeFunctions(DatabaseContext dbContext)
        {
            _dbContext = dbContext;
        }

        [Function("GetBadges")]
        public async Task<HttpResponseData> GetBadges(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "badges")]
                HttpRequestData req,
            FunctionContext executionContext
        )
        {
            var logger = executionContext.GetLogger("GetBadges");
            logger.LogInformation("Fetching all badges.");
            var response = req.CreateResponse();

            try
            {
                var query = new QueryDefinition("SELECT * FROM c");
                var iterator = _dbContext.BadgesContainer.GetItemQueryIterator<BadgeObject>(query);
                var badges = await iterator.ReadNextAsync();

                if (badges != null && badges.Count > 0)
                {
                    response.StatusCode = HttpStatusCode.OK;
                    response.Headers.Add("Content-Type", "application/json; charset=utf-8");
                    await response.WriteStringAsync(JsonSerializer.Serialize(badges));
                }
                else
                {
                    response.StatusCode = HttpStatusCode.NotFound;
                    await response.WriteStringAsync("No badges found.");
                }
            }
            catch (CosmosException ex)
            {
                logger.LogError($"Cosmos DB error: {ex.Message}");
                response.StatusCode = HttpStatusCode.InternalServerError;
                await response.WriteStringAsync("An error occurred while fetching badges.");
            }

            return response;
        }

        [Function("PurchaseBadges")]
        public async Task<HttpResponseData> UpdateUserBadges(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "profile/purchase-badges")]
                HttpRequestData req,
            FunctionContext executionContext
        )
        {
            var logger = executionContext.GetLogger("UpdateUserBadges");
            logger.LogInformation("Updating user badges");

            var response = req.CreateResponse();
            try
            {
                // Parse request body for auth0UserId, value, and imageUrl
                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var requestData = JsonSerializer.Deserialize<UpdateBadgeRequest>(requestBody);

                if (
                    requestData == null
                    || string.IsNullOrWhiteSpace(requestData.Auth0UserId)
                    || string.IsNullOrWhiteSpace(requestData.ImageUrl)
                )
                {
                    response.StatusCode = HttpStatusCode.BadRequest;
                    await response.WriteAsJsonAsync(new { Error = "Invalid request payload." });
                    return response;
                }

                // Fetch user profile
                var query = new QueryDefinition(
                    "SELECT * FROM c WHERE c.auth0UserId = @auth0UserId"
                ).WithParameter("@auth0UserId", requestData.Auth0UserId);
                var queryResultSetIterator =
                    _dbContext.UsersContainer.GetItemQueryIterator<StyxUser>(query);

                if (queryResultSetIterator.HasMoreResults)
                {
                    var userResponse = await queryResultSetIterator.ReadNextAsync();
                    var user = userResponse.FirstOrDefault();

                    if (user != null)
                    {
                        // Check if imageUrl is already in badges array
                        if (user.Badges.Contains(requestData.ImageUrl))
                        {
                            response.StatusCode = HttpStatusCode.Conflict;
                            await response.WriteAsJsonAsync(
                                new { Error = "Badge already exists." }
                            );
                            return response;
                        }

                        // Check if the user has enough coins
                        if (user.Coins >= requestData.Value)
                        {
                            // Update user profile
                            user.Badges.Add(requestData.ImageUrl);
                            user.Coins = user.Coins - requestData.Value;

                            // Replace the user document in the database
                            await _dbContext.UsersContainer.UpsertItemAsync(
                                user,
                                new PartitionKey(user.id)
                            );

                            response.StatusCode = HttpStatusCode.OK;
                            await response.WriteAsJsonAsync(
                                new { Message = "Badge added successfully.", User = user }
                            );
                            return response;
                        }
                        else
                        {
                            response.StatusCode = HttpStatusCode.BadRequest;
                            await response.WriteAsJsonAsync(new { Error = "Not enough coins." });
                            return response;
                        }
                    }
                }

                response.StatusCode = HttpStatusCode.NotFound;
                await response.WriteAsJsonAsync(new { Error = "User profile not found." });
            }
            catch (CosmosException ex)
            {
                logger.LogError($"Cosmos DB error: {ex.Message}");
                response.StatusCode = HttpStatusCode.InternalServerError;
                await response.WriteAsJsonAsync(
                    new { Error = "An error occurred while updating the user profile." }
                );
            }
            catch (Exception ex)
            {
                logger.LogError($"General error: {ex.Message}");
                response.StatusCode = HttpStatusCode.InternalServerError;
                await response.WriteAsJsonAsync(new { Error = "An unexpected error occurred." });
            }

            return response;
        }
    }
}
