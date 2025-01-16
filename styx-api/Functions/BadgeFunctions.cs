using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens; // For SecurityTokenException
using Styx.Api.Data;
using Styx.Api.Models;
using Styx.Api.Utils; // <-- Contains your Auth0TokenHelper

namespace Styx.Api.Functions
{
    public class BadgeFunctions
    {
        private readonly DatabaseContext _dbContext;

        public BadgeFunctions(DatabaseContext dbContext)
        {
            _dbContext = dbContext;
        }

        // ------------------------------------------
        // Fetch all badges (no user ID required)
        // ------------------------------------------
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

        // ------------------------------------------
        // Purchase badges: now use token-based user ID
        // ------------------------------------------
        [Function("PurchaseBadges")]
        public async Task<HttpResponseData> UpdateUserBadges(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "profile/purchase-badges")]
                HttpRequestData req,
            FunctionContext executionContext
        )
        {
            var logger = executionContext.GetLogger("UpdateUserBadges");
            logger.LogInformation("Updating user badges (token-based).");

            var response = req.CreateResponse();

            // 1) Extract the token
            string token;
            try
            {
                token = Auth0TokenHelper.GetBearerToken(req);
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogError($"Token extraction error: {ex.Message}");
                response.StatusCode = HttpStatusCode.Unauthorized;
                await response.WriteAsJsonAsync(
                    new { Error = "Missing or invalid Authorization header." }
                );
                return response;
            }

            // 2) Validate token, get sub
            string auth0UserId;
            try
            {
                auth0UserId = await Auth0TokenHelper.ValidateTokenAndGetSub(token);
            }
            catch (SecurityTokenException ex)
            {
                logger.LogError($"Token validation failed: {ex.Message}");
                response.StatusCode = HttpStatusCode.Unauthorized;
                await response.WriteAsJsonAsync(new { Error = "Invalid token." });
                return response;
            }
            catch (Exception ex)
            {
                logger.LogError($"Token validation error: {ex.Message}");
                response.StatusCode = HttpStatusCode.InternalServerError;
                await response.WriteAsJsonAsync(new { Error = "Token validation error." });
                return response;
            }

            // 3) Parse request body for the cost (Value) and the badge image URL
            //    We no longer expect the user ID from the payload
            UpdateBadgeRequest requestData;
            try
            {
                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                requestData = JsonSerializer.Deserialize<UpdateBadgeRequest>(requestBody);
            }
            catch (Exception ex)
            {
                logger.LogError($"Error reading request body: {ex.Message}");
                response.StatusCode = HttpStatusCode.BadRequest;
                await response.WriteAsJsonAsync(new { Error = "Invalid request payload." });
                return response;
            }

            // Validate the request data
            if (
                requestData == null
                || requestData.Value <= 0
                || string.IsNullOrWhiteSpace(requestData.ImageUrl)
            )
            {
                response.StatusCode = HttpStatusCode.BadRequest;
                await response.WriteAsJsonAsync(
                    new { Error = "Invalid request payload. Value > 0 and ImageUrl are required." }
                );
                return response;
            }

            // 4) Fetch the user profile from Cosmos DB using auth0UserId from token
            try
            {
                var query = new QueryDefinition(
                    "SELECT * FROM c WHERE c.auth0UserId = @auth0UserId"
                ).WithParameter("@auth0UserId", auth0UserId);
                var iterator = _dbContext.UsersContainer.GetItemQueryIterator<StyxUser>(query);

                StyxUser user = null;
                if (iterator.HasMoreResults)
                {
                    var userResponse = await iterator.ReadNextAsync();
                    user = userResponse.FirstOrDefault();
                }

                if (user == null)
                {
                    response.StatusCode = HttpStatusCode.NotFound;
                    await response.WriteAsJsonAsync(new { Error = "User profile not found." });
                    return response;
                }

                // Check if the user already has this badge
                if (user.Badges.Contains(requestData.ImageUrl))
                {
                    response.StatusCode = HttpStatusCode.Conflict;
                    await response.WriteAsJsonAsync(new { Error = "Badge already exists." });
                    return response;
                }

                // Check if the user has enough coins
                if (user.Coins < requestData.Value)
                {
                    response.StatusCode = HttpStatusCode.BadRequest;
                    await response.WriteAsJsonAsync(new { Error = "Not enough coins." });
                    return response;
                }

                // Deduct coins, add badge
                user.Coins -= requestData.Value;
                user.Badges.Add(requestData.ImageUrl);

                // 5) Upsert user profile in DB
                await _dbContext.UsersContainer.UpsertItemAsync(user, new PartitionKey(user.id));

                // 6) Return success
                response.StatusCode = HttpStatusCode.OK;
                await response.WriteAsJsonAsync(
                    new { Message = "Badge added successfully.", User = user }
                );
                return response;
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
