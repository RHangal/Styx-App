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
    public class CoinFunctions
    {
        private readonly DatabaseContext _dbContext;

        public CoinFunctions(DatabaseContext dbContext)
        {
            _dbContext = dbContext;
        }

        [Function("RewardDailyCoins")]
        public async Task<HttpResponseData> RewardDailyCoins(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "users/reward-coins")]
                HttpRequestData req,
            FunctionContext executionContext
        )
        {
            var logger = executionContext.GetLogger("RewardDailyCoins");
            logger.LogInformation("Processing reward coins request...");

            try
            {
                // Parse request body
                var requestBody = await req.ReadAsStringAsync();
                var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(requestBody);

                if (
                    !payload.TryGetValue("auth0UserId", out var auth0UserId)
                    || string.IsNullOrEmpty(auth0UserId)
                )
                {
                    logger.LogWarning("auth0UserId is required.");
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteStringAsync("auth0UserId is required.");
                    return badRequestResponse;
                }

                // Query posts for the given auth0UserId created today
                var query = new QueryDefinition(
                    "SELECT VALUE COUNT(1) FROM c WHERE c.auth0UserId = @auth0UserId AND c.CreatedAt >= @today"
                )
                    .WithParameter("@auth0UserId", auth0UserId)
                    .WithParameter("@today", DateTime.UtcNow.Date);

                var postIterator = _dbContext.PostsContainer.GetItemQueryIterator<int>(query);
                var postResponse = await postIterator.ReadNextAsync();
                var postCount = postResponse.FirstOrDefault();

                if (postCount != 1)
                {
                    logger.LogInformation($"User {auth0UserId} has already created a post today.");
                    var response = req.CreateResponse(HttpStatusCode.OK);
                    await response.WriteAsJsonAsync(
                        new { message = "No reward given. Post already exists for today." }
                    );
                    return response;
                }

                // Query user profile
                var userQuery = new QueryDefinition(
                    "SELECT * FROM c WHERE c.auth0UserId = @auth0UserId"
                ).WithParameter("@auth0UserId", auth0UserId);

                var userIterator = _dbContext.UsersContainer.GetItemQueryIterator<StyxUser>(
                    userQuery
                );
                var userResponse = await userIterator.ReadNextAsync();
                var userProfile = userResponse.FirstOrDefault();

                if (userProfile == null)
                {
                    logger.LogWarning($"User profile not found for auth0UserId: {auth0UserId}");
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteStringAsync("User profile not found.");
                    return notFoundResponse;
                }

                // Update user's coin balance
                userProfile.Coins += 500;

                // Replace the updated user profile in the database
                await _dbContext.UsersContainer.ReplaceItemAsync(userProfile, userProfile.id);

                logger.LogInformation(
                    $"User {auth0UserId} rewarded with 500 coins. Total coins: {userProfile.Coins}"
                );

                // Response
                var successResponse = req.CreateResponse(HttpStatusCode.OK);
                await successResponse.WriteAsJsonAsync(
                    new
                    {
                        message = "500 coins rewarded successfully.",
                        userId = auth0UserId,
                        totalCoins = userProfile.Coins,
                    }
                );

                return successResponse;
            }
            catch (Exception ex)
            {
                logger.LogError($"Error processing request: {ex.Message}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync(
                    "An error occurred while processing the request."
                );
                return errorResponse;
            }
        }
    }
}
