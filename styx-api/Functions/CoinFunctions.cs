using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Styx.Api.Data;
using Styx.Api.Models;
using Styx.Api.Utils; // <-- Contains Auth0TokenHelper, if that's your chosen namespace

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
            logger.LogInformation("Processing reward coins request via token-based user ID...");

            // 1) Extract and validate the JWT to get auth0UserId
            string token;
            try
            {
                token = Auth0TokenHelper.GetBearerToken(req);
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogError($"Token extraction error: {ex.Message}");
                var missingAuthResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                await missingAuthResponse.WriteStringAsync(
                    "Missing or invalid Authorization header."
                );
                return missingAuthResponse;
            }

            string auth0UserId;
            try
            {
                auth0UserId = await Auth0TokenHelper.ValidateTokenAndGetSub(token);
            }
            catch (SecurityTokenException ex)
            {
                logger.LogError($"Token validation failed: {ex.Message}");
                var invalidToken = req.CreateResponse(HttpStatusCode.Unauthorized);
                await invalidToken.WriteStringAsync("Invalid token.");
                return invalidToken;
            }
            catch (Exception ex)
            {
                logger.LogError($"Token validation error: {ex.Message}");
                var tokenError = req.CreateResponse(HttpStatusCode.InternalServerError);
                await tokenError.WriteStringAsync("Token validation error.");
                return tokenError;
            }

            // 2) Check whether the user has already created a post today
            try
            {
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
                    // If the user either has 0 or more than 1 posts, we assume they've posted today.
                    logger.LogInformation(
                        $"User {auth0UserId} has already created a post today or none were found."
                    );
                    var alreadyPostedResponse = req.CreateResponse(HttpStatusCode.OK);
                    await alreadyPostedResponse.WriteAsJsonAsync(
                        new { message = "No reward given. Post already exists for today." }
                    );
                    return alreadyPostedResponse;
                }

                // 3) Fetch user profile to update coins
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

                // 4) Update the user's coin balance
                userProfile.Coins += 500;

                // Replace the updated user profile in the database
                await _dbContext.UsersContainer.ReplaceItemAsync(userProfile, userProfile.id);

                logger.LogInformation(
                    $"User {auth0UserId} rewarded with 500 coins. Total coins: {userProfile.Coins}"
                );

                // 5) Return success
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
            catch (CosmosException cex)
            {
                logger.LogError($"Cosmos DB error: {cex.Message}");
                var cosmosError = req.CreateResponse(HttpStatusCode.InternalServerError);
                await cosmosError.WriteStringAsync(
                    "An error occurred while accessing the database."
                );
                return cosmosError;
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
