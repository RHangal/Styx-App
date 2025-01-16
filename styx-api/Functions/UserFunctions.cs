using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Styx.Api.Data;
using Styx.Api.Models;
using Styx.Api.Utils;

namespace Styx.Api.Functions
{
    public class UserFunctions
    {
        private readonly DatabaseContext _dbContext;

        public UserFunctions(DatabaseContext dbContext)
        {
            _dbContext = dbContext;
        }

        [Function("RegisterUser")]
        [CosmosDBOutput("my-database", "users", Connection = "CosmosDbConnectionSetting")]
        public async Task<StyxUser> RegisterUser(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "register")]
                HttpRequestData req,
            FunctionContext executionContext
        )
        {
            var logger = executionContext.GetLogger("RegisterUser");
            logger.LogInformation("Processing user registration.");

            var requestBody = await req.ReadAsStringAsync();
            var payload = JsonSerializer.Deserialize<StyxUserPayload>(requestBody);

            if (
                payload == null
                || string.IsNullOrEmpty(payload.UserID)
                || string.IsNullOrEmpty(payload.Email)
                || string.IsNullOrEmpty(payload.Name)
            )
            {
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await unauthorizedResponse.WriteStringAsync(
                    "Invalid payload. UserID, Email, and Name are required."
                );
                return null;
            }

            var newUser = new StyxUser
            {
                id = System.Guid.NewGuid().ToString(),
                auth0UserId = payload.UserID,
                email = payload.Email,
                Name = payload.Name,
            };

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await response.WriteStringAsync(
                JsonSerializer.Serialize(new { message = "Registration successful." })
            );

            return newUser;
        }

        [Function("GetUserProfile")]
        public async Task<HttpResponseData> GetUserProfile(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "profile")]
                HttpRequestData req,
            FunctionContext executionContext
        )
        {
            var logger = executionContext.GetLogger("GetUserProfile");
            logger.LogInformation("Fetching user profile via validated token.");

            var response = req.CreateResponse();

            // Extract bearer token
            string token;
            try
            {
                token = Auth0TokenHelper.GetBearerToken(req);
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogError(ex.Message);
                response.StatusCode = HttpStatusCode.Unauthorized;
                await response.WriteStringAsync("Missing or invalid Authorization header.");
                return response;
            }

            // Validate token and get sub
            string auth0UserId;
            try
            {
                auth0UserId = await Auth0TokenHelper.ValidateTokenAndGetSub(token);
            }
            catch (Exception ex)
            {
                logger.LogError($"Token validation error: {ex.Message}");
                response.StatusCode =
                    ex is SecurityTokenException
                        ? HttpStatusCode.Unauthorized
                        : HttpStatusCode.InternalServerError;
                await response.WriteStringAsync("Token validation failed.");
                return response;
            }

            // Query DB
            try
            {
                var query = new QueryDefinition(
                    "SELECT * FROM c WHERE c.auth0UserId = @auth0UserId"
                ).WithParameter("@auth0UserId", auth0UserId);

                var iterator = _dbContext.UsersContainer.GetItemQueryIterator<StyxUser>(query);

                if (iterator.HasMoreResults)
                {
                    var userResponse = await iterator.ReadNextAsync();
                    var user = userResponse.FirstOrDefault();

                    if (user != null)
                    {
                        response.StatusCode = HttpStatusCode.OK;
                        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
                        await response.WriteStringAsync(JsonSerializer.Serialize(user));
                        return response;
                    }
                }

                response.StatusCode = HttpStatusCode.NotFound;
                await response.WriteStringAsync("User profile not found.");
            }
            catch (CosmosException cex)
            {
                logger.LogError($"Cosmos DB error: {cex.Message}");
                response.StatusCode = HttpStatusCode.InternalServerError;
                await response.WriteStringAsync("Error fetching user profile.");
            }

            return response;
        }

        [Function("UpdateUserProfile")]
        public async Task<HttpResponseData> UpdateUserProfile(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "profile")]
                HttpRequestData req,
            FunctionContext executionContext
        )
        {
            var logger = executionContext.GetLogger("UpdateUserProfile");
            logger.LogInformation("Updating user profile using token-based Auth0UserId.");

            // 1. Extract the token from the Authorization header
            string token;
            try
            {
                token = Auth0TokenHelper.GetBearerToken(req);
                // or your local method if not using a helper class
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogError(ex.Message);
                var authError = req.CreateResponse(HttpStatusCode.Unauthorized);
                await authError.WriteStringAsync("Missing or invalid Authorization header.");
                return authError;
            }

            // 2. Validate token and get sub (Auth0UserId)
            string auth0UserId;
            try
            {
                auth0UserId = await Auth0TokenHelper.ValidateTokenAndGetSub(token);
                // or your local validation method
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

            // 3. Query Cosmos DB for the existing user using auth0UserId
            StyxUser existingUser = null;
            try
            {
                var query = new QueryDefinition(
                    "SELECT * FROM c WHERE c.auth0UserId = @auth0UserId"
                ).WithParameter("@auth0UserId", auth0UserId);

                var queryResultSetIterator =
                    _dbContext.UsersContainer.GetItemQueryIterator<StyxUser>(query);

                if (queryResultSetIterator.HasMoreResults)
                {
                    var userResponse = await queryResultSetIterator.ReadNextAsync();
                    existingUser = userResponse.FirstOrDefault();

                    if (existingUser == null)
                    {
                        logger.LogWarning("User profile not found.");
                        return req.CreateResponse(HttpStatusCode.NotFound);
                    }
                }
            }
            catch (CosmosException ex)
            {
                logger.LogError($"Cosmos DB error: {ex.Message}");
                return req.CreateResponse(HttpStatusCode.InternalServerError);
            }
            catch (Exception ex)
            {
                logger.LogError($"Unexpected error: {ex.Message}");
                return req.CreateResponse(HttpStatusCode.InternalServerError);
            }

            // 4. Deserialize the updated profile data from the request body
            var requestBody = await req.ReadAsStringAsync();
            var updatedProfile = JsonSerializer.Deserialize<StyxUser>(requestBody);

            // Log the current and new user data for debugging
            logger.LogInformation(
                $"Existing user ID: {existingUser.id}, Auth0 User ID: {existingUser.auth0UserId}"
            );
            logger.LogInformation($"Raw request body: {requestBody}");
            logger.LogInformation($"Existing user data: {JsonSerializer.Serialize(existingUser)}");
            logger.LogInformation(
                $"Updated profile data: {JsonSerializer.Serialize(updatedProfile)}"
            );

            // 5. Update the existing user with non-null fields from updatedProfile
            existingUser.Name = updatedProfile.Name ?? existingUser.Name;
            existingUser.AboutMe = updatedProfile.AboutMe ?? existingUser.AboutMe;
            existingUser.Habits = updatedProfile.Habits ?? existingUser.Habits;

            // Ensure the original id and auth0UserId are retained
            // (they're already set, so no changes needed unless you want to enforce them explicitly)
            existingUser.id = existingUser.id;
            existingUser.auth0UserId = existingUser.auth0UserId;

            logger.LogInformation(
                $"Final user object for update: {JsonSerializer.Serialize(existingUser)}"
            );

            // 6. Save changes to Cosmos DB
            try
            {
                await _dbContext.UsersContainer.ReplaceItemAsync(existingUser, existingUser.id);
            }
            catch (CosmosException ex)
            {
                logger.LogError(
                    $"Error updating user profile: {ex.Message}, Status Code: {ex.StatusCode}, ActivityId: {ex.ActivityId}"
                );
                return req.CreateResponse(HttpStatusCode.InternalServerError);
            }

            // 7. Return success response
            var response = req.CreateResponse(HttpStatusCode.OK);
            var responseBody = new { success = true, message = "Profile updated successfully." };
            await response.WriteAsJsonAsync(responseBody);

            return response;
        }

        [Function("UpdateProfileMedia")]
        public async Task<HttpResponseData> UpdateProfileMedia(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "profile/media")]
                HttpRequestData req,
            FunctionContext executionContext
        )
        {
            var logger = executionContext.GetLogger("UpdateProfileMedia");
            logger.LogInformation("Updating media for user via token-based Auth0UserId.");

            // 1. Extract bearer token from Authorization header
            string token;
            try
            {
                token = Auth0TokenHelper.GetBearerToken(req);
                // or replace with your local method if you're not using a Helper class
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogError(ex.Message);
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteStringAsync("Missing or invalid Authorization header.");
                return unauthorized;
            }

            // 2. Validate token and get 'sub' (the Auth0UserId)
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

            // 3. Query Cosmos DB for this user by auth0UserId
            StyxUser existingUser = null;
            try
            {
                var query = new QueryDefinition(
                    "SELECT * FROM c WHERE c.auth0UserId = @auth0UserId"
                ).WithParameter("@auth0UserId", auth0UserId);

                var queryResultSetIterator =
                    _dbContext.UsersContainer.GetItemQueryIterator<StyxUser>(query);

                if (queryResultSetIterator.HasMoreResults)
                {
                    var userResponse = await queryResultSetIterator.ReadNextAsync();
                    existingUser = userResponse.FirstOrDefault();

                    if (existingUser == null)
                    {
                        logger.LogWarning("User not found.");
                        return req.CreateResponse(HttpStatusCode.NotFound);
                    }
                }
            }
            catch (CosmosException ex)
            {
                logger.LogError($"Cosmos DB error: {ex.Message}");
                return req.CreateResponse(HttpStatusCode.InternalServerError);
            }
            catch (Exception ex)
            {
                logger.LogError($"Unexpected error: {ex.Message}");
                return req.CreateResponse(HttpStatusCode.InternalServerError);
            }

            // 4. Parse request body to extract the photoUrl
            var requestBody = await req.ReadAsStringAsync();
            var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(requestBody);
            if (
                payload == null
                || !payload.TryGetValue("photoUrl", out var photoUrl)
                || string.IsNullOrEmpty(photoUrl)
            )
            {
                logger.LogWarning("photoUrl is missing in the request body.");
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("photoUrl is required.");
                return badRequestResponse;
            }

            // 5. Update the user's PhotoUrl field
            existingUser.PhotoUrl = photoUrl;

            // 6. Replace user in Cosmos DB
            try
            {
                await _dbContext.UsersContainer.ReplaceItemAsync(existingUser, existingUser.id);
            }
            catch (CosmosException ex)
            {
                logger.LogError(
                    $"Error updating profile media: {ex.Message}, Status Code: {ex.StatusCode}, ActivityId: {ex.ActivityId}"
                );
                return req.CreateResponse(HttpStatusCode.InternalServerError);
            }

            // 7. Return success response
            var response = req.CreateResponse(HttpStatusCode.OK);
            var responseBody = new
            {
                success = true,
                message = "Profile photo updated successfully.",
                PhotoUrl = photoUrl,
            };

            await response.WriteAsJsonAsync(responseBody);
            return response;
        }
    }
}
