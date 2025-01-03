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
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Styx.Api.Data;
using Styx.Api.Models;
using Styx.Api.Utils;
using AzureHttpRequestData = Microsoft.Azure.Functions.Worker.Http.HttpRequestData;

namespace Styx.Api.Functions
{
    public class UserFunctions
    {
        private readonly DatabaseContext _dbContext;

        public UserFunctions(DatabaseContext dbContext)
        {
            _dbContext = dbContext;
        }

        // ConfigurationManager automatically fetches and caches OIDC config and JWKS
        private static readonly string Auth0Domain = Environment.GetEnvironmentVariable(
            "AUTH0_DOMAIN"
        );
        private static readonly string Auth0Audience = Environment.GetEnvironmentVariable(
            "AUTH0_AUDIENCE"
        );

        // Lazy-initialized OIDC config manager
        private static readonly ConfigurationManager<OpenIdConnectConfiguration> _configurationManager;

        static UserFunctions()
        {
            if (string.IsNullOrEmpty(Auth0Domain) || string.IsNullOrEmpty(Auth0Audience))
            {
                throw new InvalidOperationException("AUTH0_DOMAIN or AUTH0_AUDIENCE not set.");
            }

            // The well-known OpenID configuration endpoint for Auth0:
            // e.g. https://my-tenant.us.auth0.com/.well-known/openid-configuration
            var wellKnownEndpoint = $"https://{Auth0Domain}/.well-known/openid-configuration";

            _configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                wellKnownEndpoint,
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever()
            );
        }

        [Function("RegisterUser")]
        [CosmosDBOutput("my-database", "users", Connection = "CosmosDbConnectionSetting")]
        public async Task<StyxUser> RegisterUser(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "register")]
                AzureHttpRequestData req,
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
                AzureHttpRequestData req,
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
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "profile/{auth0UserId}")]
                AzureHttpRequestData req,
            string auth0UserId,
            FunctionContext executionContext
        )
        {
            var logger = executionContext.GetLogger("UpdateUserProfile");
            logger.LogInformation($"Updating profile for Auth0 User ID: {auth0UserId}");

            StyxUser existingUser = null;

            try
            {
                // Query to find the user by auth0UserId
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

            // Update properties if provided
            existingUser.Name = updatedProfile.Name ?? existingUser.Name;
            existingUser.AboutMe = updatedProfile.AboutMe ?? existingUser.AboutMe;
            existingUser.Habits = updatedProfile.Habits ?? existingUser.Habits;

            // Ensure the original id and auth0UserId are retained
            existingUser.id = existingUser.id; // Ensure ID is retained
            existingUser.auth0UserId = existingUser.auth0UserId; // Ensure Auth0 ID is retained

            logger.LogInformation(
                $"Final user object for update: {JsonSerializer.Serialize(existingUser)}"
            );

            try
            {
                // Replace the item in the Cosmos DB container using the original existingUser.id, without a partition key
                await _dbContext.UsersContainer.ReplaceItemAsync(existingUser, existingUser.id);
            }
            catch (CosmosException ex)
            {
                logger.LogError(
                    $"Error updating user profile: {ex.Message}, Status Code: {ex.StatusCode}, ActivityId: {ex.ActivityId}"
                );
                return req.CreateResponse(HttpStatusCode.InternalServerError);
            }

            // Response creation
            var response = req.CreateResponse(HttpStatusCode.OK);

            // Create a JSON object for the response
            var responseBody = new { success = true, message = "Profile updated successfully." };

            // Write the JSON response (automatically sets content type to application/json)
            await response.WriteAsJsonAsync(responseBody);

            return response;
        }

        [Function("UpdateProfileMedia")]
        public async Task<HttpResponseData> UpdateProfileMedia(
            [HttpTrigger(
                AuthorizationLevel.Anonymous,
                "put",
                Route = "profile/media/{auth0UserId}"
            )]
                AzureHttpRequestData req,
            string auth0UserId,
            FunctionContext executionContext
        )
        {
            var logger = executionContext.GetLogger("UpdatePostMedia");
            logger.LogInformation($"Updating media for Profile ID: {auth0UserId}");

            StyxUser existingUser = null;

            try
            {
                // Query to find the post by postId
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

            // Parse request body to extract media URL
            var requestBody = await req.ReadAsStringAsync();
            var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(requestBody);
            if (
                !payload.TryGetValue("photoUrl", out var photoUrl) || string.IsNullOrEmpty(photoUrl)
            )
            {
                logger.LogWarning("Media URL is missing in the request body.");
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Media URL is required.");
                return badRequestResponse;
            }

            // Update the post's media URL
            existingUser.PhotoUrl = photoUrl;

            try
            {
                // Replace the item in the Cosmos DB container using the original existingPost.id
                await _dbContext.UsersContainer.ReplaceItemAsync(existingUser, existingUser.id);
            }
            catch (CosmosException ex)
            {
                logger.LogError(
                    $"Error updating post media: {ex.Message}, Status Code: {ex.StatusCode}, ActivityId: {ex.ActivityId}"
                );
                return req.CreateResponse(HttpStatusCode.InternalServerError);
            }

            // Response creation
            var response = req.CreateResponse(HttpStatusCode.OK);
            var responseBody = new
            {
                success = true,
                message = "Post media updated successfully.",
                PhotoUrl = photoUrl,
            };

            await response.WriteAsJsonAsync(responseBody);
            return response;
        }

        [Function("UpdateUserProfilePhoto")]
        public async Task<HttpResponseData> UpdateUserProfilePhoto(
            [HttpTrigger(
                AuthorizationLevel.Anonymous,
                "put",
                Route = "profile/photo/{auth0UserId}"
            )]
                AzureHttpRequestData req,
            string auth0UserId,
            FunctionContext executionContext
        )
        {
            var logger = executionContext.GetLogger("UpdateUserProfilePhoto");
            logger.LogInformation($"Updating profile photo for Auth0 User ID: {auth0UserId}");

            StyxUser existingUser = null;

            try
            {
                // Query to find the user by auth0UserId
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

            // Parse request body to extract photo URL
            var requestBody = await req.ReadAsStringAsync();
            var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(requestBody);
            if (
                !payload.TryGetValue("photoUrl", out var photoUrl) || string.IsNullOrEmpty(photoUrl)
            )
            {
                logger.LogWarning("Photo URL is missing in the request body.");
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Photo URL is required.");
                return badRequestResponse;
            }

            // Update the user's photo URL
            existingUser.PhotoUrl = photoUrl;

            try
            {
                // Replace the item in the Cosmos DB container using the original existingUser.id
                await _dbContext.UsersContainer.ReplaceItemAsync(existingUser, existingUser.id);
            }
            catch (CosmosException ex)
            {
                logger.LogError(
                    $"Error updating user profile photo: {ex.Message}, Status Code: {ex.StatusCode}, ActivityId: {ex.ActivityId}"
                );
                return req.CreateResponse(HttpStatusCode.InternalServerError);
            }

            // Response creation
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
