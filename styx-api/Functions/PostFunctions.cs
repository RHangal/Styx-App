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

        [Function("CreatePost")]
        [CosmosDBOutput("my-database", "posts", Connection = "CosmosDbConnectionSetting")]
        public static async Task<Post> CreatePost(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "posts")]
                HttpRequestData req,
            FunctionContext executionContext
        )
        {
            var logger = executionContext.GetLogger("CreatePost");
            logger.LogInformation("Processing post creation.");

            // Read and deserialize the request body
            var requestBody = await req.ReadAsStringAsync();
            var payload = JsonSerializer.Deserialize<PostPayload>(requestBody);

            // Validate required fields
            if (
                payload == null
                || string.IsNullOrEmpty(payload.PostType)
                || string.IsNullOrEmpty(payload.Auth0UserId)
                || string.IsNullOrEmpty(payload.Caption)
                || string.IsNullOrEmpty(payload.Name)
                || string.IsNullOrEmpty(payload.Email)
            )
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync(
                    "Invalid payload. PostType, Auth0UserId, Caption, and Name are required."
                );
                return null;
            }

            // Create a new post instance with required and optional fields
            var newPost = new Post
            {
                id = Guid.NewGuid().ToString(),
                PostType = payload.PostType,
                auth0UserId = payload.Auth0UserId,
                Name = payload.Name,
                Email = payload.Email,
                Caption = payload.Caption,
                MediaUrl = payload.MediaUrl ?? string.Empty,
                CreatedAt = DateTime.UtcNow,
            };

            // Create a success response
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await response.WriteStringAsync(
                JsonSerializer.Serialize(
                    new { message = "Post created successfully.", postId = newPost.id }
                )
            );

            return newPost;
        }

        [Function("DeletePost")]
        public async Task<HttpResponseData> DeletePost(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "posts/{postId}")]
                HttpRequestData req,
            string postId,
            FunctionContext executionContext
        )
        {
            var logger = executionContext.GetLogger("DeletePost");
            logger.LogInformation($"Deleting Post ID: {postId}");

            Post existingPost = null;

            // Parse and validate auth0UserId from the request body
            string requestBody;
            try
            {
                requestBody = await req.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                logger.LogError($"Error reading request body: {ex.Message}");
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Invalid request body.");
                return badRequestResponse;
            }

            var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(requestBody);

            if (
                !payload.TryGetValue("auth0UserId", out var auth0UserId)
                || string.IsNullOrEmpty(auth0UserId)
            )
            {
                logger.LogWarning("auth0UserId is missing in the request body.");
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("auth0UserId is required.");
                return badRequestResponse;
            }

            try
            {
                // Query to find the post by postId
                var query = new QueryDefinition("SELECT * FROM c WHERE c.id = @id").WithParameter(
                    "@id",
                    postId
                );

                var queryResultSetIterator = _dbContext.PostsContainer.GetItemQueryIterator<Post>(
                    query
                );

                if (queryResultSetIterator.HasMoreResults)
                {
                    var postResponse = await queryResultSetIterator.ReadNextAsync();
                    existingPost = postResponse.FirstOrDefault();

                    if (existingPost == null)
                    {
                        logger.LogWarning("Post not found.");
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

            // Authorization check
            if (existingPost.auth0UserId != auth0UserId)
            {
                logger.LogWarning($"Unauthorized delete attempt by user: {auth0UserId}");
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorizedResponse.WriteStringAsync(
                    "You are not authorized to delete this post."
                );
                return unauthorizedResponse;
            }

            // Delete the post
            try
            {
                await _dbContext.PostsContainer.DeleteItemAsync<Post>(
                    postId,
                    new PartitionKey(postId)
                );
            }
            catch (CosmosException ex)
            {
                logger.LogError(
                    $"Error deleting post: {ex.Message}, Status Code: {ex.StatusCode}, ActivityId: {ex.ActivityId}"
                );
                return req.CreateResponse(HttpStatusCode.InternalServerError);
            }

            // Create a success response
            var response = req.CreateResponse(HttpStatusCode.OK);
            var responseBody = new { success = true, message = "Post deleted successfully." };

            await response.WriteAsJsonAsync(responseBody);
            return response;
        }

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

            Post existingPost = null;

            try
            {
                // Query to find the post by postId
                var query = new QueryDefinition("SELECT * FROM c WHERE c.id = @id").WithParameter(
                    "@id",
                    postId
                );

                var queryResultSetIterator = _dbContext.PostsContainer.GetItemQueryIterator<Post>(
                    query
                );

                if (queryResultSetIterator.HasMoreResults)
                {
                    var postResponse = await queryResultSetIterator.ReadNextAsync();
                    existingPost = postResponse.FirstOrDefault();

                    if (existingPost == null)
                    {
                        logger.LogWarning("Post not found.");
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
                !payload.TryGetValue("mediaUrl", out var mediaUrl) || string.IsNullOrEmpty(mediaUrl)
            )
            {
                logger.LogWarning("Media URL is missing in the request body.");
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Media URL is required.");
                return badRequestResponse;
            }

            // Update the post's media URL
            existingPost.MediaUrl = mediaUrl;

            try
            {
                // Replace the item in the Cosmos DB container using the original existingPost.id
                await _dbContext.PostsContainer.ReplaceItemAsync(existingPost, existingPost.id);
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
                MediaUrl = mediaUrl,
            };

            await response.WriteAsJsonAsync(responseBody);
            return response;
        }

        [Function("LikeItem")]
        public async Task<HttpResponseData> LikeItem(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "posts/like")]
                HttpRequestData req,
            FunctionContext executionContext
        )
        {
            var logger = executionContext.GetLogger("LikeItem");
            logger.LogInformation("Processing like/unlike request...");

            // Parse request body
            var requestBody = await req.ReadAsStringAsync();
            var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(requestBody);

            if (
                !payload.TryGetValue("postId", out var postId)
                || string.IsNullOrEmpty(postId)
                || !payload.TryGetValue("auth0UserId", out var auth0UserId)
                || string.IsNullOrEmpty(auth0UserId)
            )
            {
                logger.LogWarning("Missing required fields: postId or auth0UserId.");
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("postId and auth0UserId are required.");
                return badRequestResponse;
            }

            payload.TryGetValue("commentId", out var commentId);
            payload.TryGetValue("replyId", out var replyId);

            // Retrieve the post
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
            catch (Exception ex)
            {
                logger.LogError($"Error retrieving post: {ex.Message}");
                return req.CreateResponse(HttpStatusCode.InternalServerError);
            }

            dynamic targetItem = post; // Default to the post
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

            // Handle like/unlike logic
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

            // Update the post in the database
            try
            {
                await _dbContext.PostsContainer.ReplaceItemAsync(post, post.id);
            }
            catch (Exception ex)
            {
                logger.LogError($"Error updating likes: {ex.Message}");
                return req.CreateResponse(HttpStatusCode.InternalServerError);
            }

            // Response
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(
                new
                {
                    success = true,
                    message = "Like/unlike operation successful.",
                    targetItem.LikesCount,
                    Likes = targetItem.Likes,
                }
            );

            return response;
        }
    }
}
