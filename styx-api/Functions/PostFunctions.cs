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
using Styx.Api.Utils; // Contains your Auth0TokenHelper

namespace Styx.Api.Functions
{
    public class PostFunctions
    {
        private readonly DatabaseContext _dbContext;

        public PostFunctions(DatabaseContext dbContext)
        {
            _dbContext = dbContext;
        }

        [Function("GetPostsByType")]
        public async Task<HttpResponseData> GetPostsByType(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "posts/{postType}")]
                HttpRequestData req,
            string PostType,
            FunctionContext executionContext
        )
        {
            var logger = executionContext.GetLogger("GetPostsByType");
            logger.LogInformation($"Fetching posts for PostType: {PostType}");

            var response = req.CreateResponse();

            try
            {
                // Query to find posts by postType
                var query = new QueryDefinition(
                    "SELECT * FROM c WHERE c.PostType = @PostType"
                ).WithParameter("@PostType", PostType);
                var queryResultSetIterator = _dbContext.PostsContainer.GetItemQueryIterator<Post>(
                    query
                );

                var posts = new List<Post>();

                while (queryResultSetIterator.HasMoreResults)
                {
                    var postsResponse = await queryResultSetIterator.ReadNextAsync();
                    posts.AddRange(postsResponse);
                }

                if (posts.Any())
                {
                    response.StatusCode = HttpStatusCode.OK;
                    response.Headers.Add("Content-Type", "application/json; charset=utf-8");
                    await response.WriteStringAsync(JsonSerializer.Serialize(posts));
                    return response;
                }

                response.StatusCode = HttpStatusCode.NotFound;
                await response.WriteStringAsync("No posts found for the specified PostType.");
            }
            catch (CosmosException ex)
            {
                logger.LogError($"Cosmos DB error: {ex.Message}");
                response.StatusCode = HttpStatusCode.InternalServerError;
                await response.WriteStringAsync("An error occurred while fetching posts.");
            }

            return response;
        }

        // ------------------------------------------
        // Create a new post
        // ------------------------------------------
        [Function("CreatePost")]
        [CosmosDBOutput("my-database", "posts", Connection = "CosmosDbConnectionSetting")]
        public static async Task<Post> CreatePost(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "posts")]
                HttpRequestData req,
            FunctionContext executionContext
        )
        {
            var logger = executionContext.GetLogger("CreatePost");
            logger.LogInformation("Processing post creation (token-based).");

            // 1) Extract token
            string token;
            try
            {
                token = Auth0TokenHelper.GetBearerToken(req);
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogError($"Authorization header error: {ex.Message}");
                var badRequest = req.CreateResponse(HttpStatusCode.Unauthorized);
                await badRequest.WriteStringAsync("Missing or invalid Authorization header.");
                return null;
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
                var invalidToken = req.CreateResponse(HttpStatusCode.Unauthorized);
                await invalidToken.WriteStringAsync("Invalid token.");
                return null;
            }
            catch (Exception ex)
            {
                logger.LogError($"Token validation error: {ex.Message}");
                var tokenError = req.CreateResponse(HttpStatusCode.InternalServerError);
                await tokenError.WriteStringAsync("Token validation error.");
                return null;
            }

            // 3) Parse request body for the rest of the post fields
            var requestBody = await req.ReadAsStringAsync();
            var payload = JsonSerializer.Deserialize<PostPayload>(requestBody);

            // Validate required fields except auth0UserId,
            // since we already have that from the token
            if (
                payload == null
                || string.IsNullOrEmpty(payload.PostType)
                || string.IsNullOrEmpty(payload.Caption)
                || string.IsNullOrEmpty(payload.Name)
                || string.IsNullOrEmpty(payload.Email)
            )
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteStringAsync(
                    "Invalid payload. PostType, Caption, Name, and Email are required."
                );
                return null;
            }

            // 4) Create a new post
            var newPost = new Post
            {
                id = Guid.NewGuid().ToString(),
                PostType = payload.PostType,
                auth0UserId = auth0UserId, // from token
                Name = payload.Name,
                Email = payload.Email,
                Caption = payload.Caption,
                MediaUrl = payload.MediaUrl ?? string.Empty,
                CreatedAt = DateTime.UtcNow,
            };

            // 5) Return a success response to the client
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await response.WriteStringAsync(
                JsonSerializer.Serialize(
                    new { message = "Post created successfully.", postId = newPost.id }
                )
            );

            // 6) Return the newPost to Cosmos DB via the [CosmosDBOutput] binding
            return newPost;
        }

        // ------------------------------------------
        // Delete a post
        // ------------------------------------------
        [Function("DeletePost")]
        public async Task<HttpResponseData> DeletePost(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "posts/{postId}")]
                HttpRequestData req,
            string postId,
            FunctionContext executionContext
        )
        {
            var logger = executionContext.GetLogger("DeletePost");
            logger.LogInformation($"Attempting to delete post ID: {postId}");

            // 1) Extract and validate token
            string token;
            try
            {
                token = Auth0TokenHelper.GetBearerToken(req);
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogError($"Authorization error: {ex.Message}");
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteStringAsync("Missing or invalid Authorization header.");
                return unauthorized;
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

            // 2) Retrieve the existing post
            Post existingPost = null;
            try
            {
                var query = new QueryDefinition("SELECT * FROM c WHERE c.id = @id").WithParameter(
                    "@id",
                    postId
                );

                var iterator = _dbContext.PostsContainer.GetItemQueryIterator<Post>(query);
                var postResponse = await iterator.ReadNextAsync();
                existingPost = postResponse.FirstOrDefault();

                if (existingPost == null)
                {
                    logger.LogWarning("Post not found.");
                    return req.CreateResponse(HttpStatusCode.NotFound);
                }
            }
            catch (CosmosException cex)
            {
                logger.LogError($"Cosmos DB error: {cex.Message}");
                return req.CreateResponse(HttpStatusCode.InternalServerError);
            }
            catch (Exception ex)
            {
                logger.LogError($"Unexpected error: {ex.Message}");
                return req.CreateResponse(HttpStatusCode.InternalServerError);
            }

            // 3) Check if the token user (auth0UserId) matches the post's owner
            if (!string.Equals(existingPost.auth0UserId, auth0UserId))
            {
                logger.LogWarning($"Unauthorized delete attempt by user: {auth0UserId}");
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteStringAsync("You are not authorized to delete this post.");
                return unauthorized;
            }

            // 4) Delete the post
            try
            {
                await _dbContext.PostsContainer.DeleteItemAsync<Post>(
                    postId,
                    new PartitionKey(postId)
                );
            }
            catch (CosmosException cex)
            {
                logger.LogError($"Error deleting post: {cex.Message}");
                return req.CreateResponse(HttpStatusCode.InternalServerError);
            }

            // 5) Return success
            var response = req.CreateResponse(HttpStatusCode.OK);
            var responseBody = new { success = true, message = "Post deleted successfully." };
            await response.WriteAsJsonAsync(responseBody);
            return response;
        }

        // ------------------------------------------
        // Update post media (if you store media links on Post)
        // ------------------------------------------
        [Function("UpdatePostMedia")]
        public async Task<HttpResponseData> UpdatePostMedia(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "posts/media/{postId}")]
                HttpRequestData req,
            string postId,
            FunctionContext executionContext
        )
        {
            var logger = executionContext.GetLogger("UpdatePostMedia");
            logger.LogInformation($"Updating media for Post ID: {postId}");

            // 1) Validate token for user identity
            string token;
            try
            {
                token = Auth0TokenHelper.GetBearerToken(req);
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogError(ex.Message);
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteStringAsync("Missing or invalid Authorization header.");
                return unauthorized;
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

            // 2) Retrieve existing post
            Post existingPost = null;
            try
            {
                var query = new QueryDefinition("SELECT * FROM c WHERE c.id = @id").WithParameter(
                    "@id",
                    postId
                );

                var iterator = _dbContext.PostsContainer.GetItemQueryIterator<Post>(query);
                var postResponse = await iterator.ReadNextAsync();
                existingPost = postResponse.FirstOrDefault();

                if (existingPost == null)
                {
                    logger.LogWarning("Post not found.");
                    return req.CreateResponse(HttpStatusCode.NotFound);
                }
            }
            catch (CosmosException cex)
            {
                logger.LogError($"Cosmos DB error: {cex.Message}");
                return req.CreateResponse(HttpStatusCode.InternalServerError);
            }

            // 3) Check ownership
            if (!string.Equals(existingPost.auth0UserId, auth0UserId))
            {
                logger.LogWarning($"Unauthorized media update by user: {auth0UserId}");
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteStringAsync("You are not authorized to update this post.");
                return unauthorized;
            }

            // 4) Parse request body for 'mediaUrl' (or your field name)
            var requestBody = await req.ReadAsStringAsync();
            var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(requestBody);
            if (
                payload == null
                || !payload.TryGetValue("mediaUrl", out var mediaUrl)
                || string.IsNullOrEmpty(mediaUrl)
            )
            {
                logger.LogWarning("mediaUrl is missing in the request body.");
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("mediaUrl is required.");
                return badRequestResponse;
            }

            // 5) Update the post
            existingPost.MediaUrl = mediaUrl;
            try
            {
                await _dbContext.PostsContainer.ReplaceItemAsync(existingPost, existingPost.id);
            }
            catch (CosmosException cex)
            {
                logger.LogError($"Error updating post media: {cex.Message}");
                return req.CreateResponse(HttpStatusCode.InternalServerError);
            }

            // 6) Return success
            var response = req.CreateResponse(HttpStatusCode.OK);
            var responseBody = new
            {
                success = true,
                message = "Post media updated successfully.",
                MediaUrl = mediaUrl,
            };

            await response.WriteAsJsonAsync(responseBody);
            return response;
        }

        // ------------------------------------------
        // Like/unlike item (post or comment)
        // ------------------------------------------
        [Function("LikeItem")]
        public async Task<HttpResponseData> LikeItem(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "posts/like")]
                HttpRequestData req,
            FunctionContext executionContext
        )
        {
            var logger = executionContext.GetLogger("LikeItem");
            logger.LogInformation("Processing like/unlike request...");

            // 1) Extract token
            string token;
            try
            {
                token = Auth0TokenHelper.GetBearerToken(req);
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogError($"Authorization header error: {ex.Message}");
                var missingAuth = req.CreateResponse(HttpStatusCode.Unauthorized);
                await missingAuth.WriteStringAsync("Missing or invalid Authorization header.");
                return missingAuth;
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
                var invalid = req.CreateResponse(HttpStatusCode.Unauthorized);
                await invalid.WriteStringAsync("Invalid token.");
                return invalid;
            }
            catch (Exception ex)
            {
                logger.LogError($"Token validation error: {ex.Message}");
                var tokenErr = req.CreateResponse(HttpStatusCode.InternalServerError);
                await tokenErr.WriteStringAsync("Token validation error.");
                return tokenErr;
            }

            // 3) Parse body for 'postId', 'commentId', 'replyId'
            var requestBody = await req.ReadAsStringAsync();
            var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(requestBody);

            if (
                payload == null
                || !payload.TryGetValue("postId", out var postId)
                || string.IsNullOrEmpty(postId)
            )
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteStringAsync("postId is required.");
                return badRequest;
            }

            payload.TryGetValue("commentId", out var commentId);
            payload.TryGetValue("replyId", out var replyId);

            // 4) Retrieve post
            Post post;
            try
            {
                var query = new QueryDefinition("SELECT * FROM c WHERE c.id = @id").WithParameter(
                    "@id",
                    postId
                );

                var iterator = _dbContext.PostsContainer.GetItemQueryIterator<Post>(query);
                var postResponse = await iterator.ReadNextAsync();
                post = postResponse.FirstOrDefault();

                if (post == null)
                {
                    logger.LogWarning($"Post with ID {postId} not found.");
                    return req.CreateResponse(HttpStatusCode.NotFound);
                }
            }
            catch (CosmosException cex)
            {
                logger.LogError($"Cosmos DB error: {cex.Message}");
                return req.CreateResponse(HttpStatusCode.InternalServerError);
            }

            // 5) Locate target item (post, comment, or reply)
            dynamic targetItem = post;
            if (!string.IsNullOrEmpty(commentId))
            {
                var comment = post.Comments.FirstOrDefault(c => c.CommentId == commentId);
                if (comment == null)
                {
                    logger.LogWarning($"Comment with ID {commentId} not found.");
                    return req.CreateResponse(HttpStatusCode.NotFound);
                }
                targetItem = comment;

                if (!string.IsNullOrEmpty(replyId))
                {
                    var reply = comment.Replies.FirstOrDefault(r => r.CommentId == replyId);
                    if (reply == null)
                    {
                        logger.LogWarning($"Reply with ID {replyId} not found.");
                        return req.CreateResponse(HttpStatusCode.NotFound);
                    }
                    targetItem = reply;
                }
            }

            // 6) Like/unlike logic
            var likesList = targetItem.Likes as List<string>;
            if (likesList.Contains(auth0UserId))
            {
                likesList.Remove(auth0UserId);
                targetItem.LikesCount--;
            }
            else
            {
                likesList.Add(auth0UserId);
                targetItem.LikesCount++;
            }

            // 7) Save changes
            try
            {
                await _dbContext.PostsContainer.ReplaceItemAsync(post, post.id);
            }
            catch (CosmosException cex)
            {
                logger.LogError($"Error updating likes: {cex.Message}");
                return req.CreateResponse(HttpStatusCode.InternalServerError);
            }

            // 8) Return response
            var success = req.CreateResponse(HttpStatusCode.OK);
            await success.WriteAsJsonAsync(
                new
                {
                    success = true,
                    message = "Like/unlike operation successful.",
                    targetItem.LikesCount,
                    Likes = targetItem.Likes,
                }
            );

            return success;
        }
    }
}
