using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using PostmarkDotNet;
using Styx.Api.Data;
using Styx.Api.Models;

namespace Styx.Api.Functions
{
    public class CommentFunctions
    {
        private readonly DatabaseContext _dbContext;

        public CommentFunctions(DatabaseContext dbContext)
        {
            _dbContext = dbContext;
        }

        [Function("AddComment")]
        public async Task<HttpResponseData> AddComment(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "posts/comments")]
                HttpRequestData req,
            FunctionContext executionContext
        )
        {
            var logger = executionContext.GetLogger("AddComment");
            logger.LogInformation("Processing comment/reply addition...");

            // Parse request body
            var requestBody = await req.ReadAsStringAsync();
            var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(requestBody);

            // Validate required fields
            if (
                !payload.TryGetValue("postId", out var postId)
                || string.IsNullOrEmpty(postId)
                || !payload.TryGetValue("auth0UserId", out var auth0UserId)
                || string.IsNullOrEmpty(auth0UserId)
                || !payload.TryGetValue("text", out var text)
                || string.IsNullOrEmpty(text)
                || !payload.TryGetValue("commenterEmail", out var commenterEmail)
                || string.IsNullOrEmpty(commenterEmail)
                || !payload.TryGetValue("postEmail", out var postEmail)
                || string.IsNullOrEmpty(postEmail)
                || !payload.TryGetValue("name", out var name)
                || string.IsNullOrEmpty(name)
            )
            {
                logger.LogWarning(
                    "Missing required fields: postId, auth0UserId, text, email, or name."
                );
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync(
                    "postId, auth0UserId, and text are required."
                );
                return badRequestResponse;
            }

            payload.TryGetValue("commentId", out var commentId); // Optional field

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

            // Handle top-level comment or reply logic
            if (string.IsNullOrEmpty(commentId))
            {
                // Add a new top-level comment
                var newComment = new Comment
                {
                    CommentId = Guid.NewGuid().ToString(),
                    auth0UserId = auth0UserId,
                    Text = text,
                    Name = name,
                    Replies = new List<Comment>(),
                    CreatedAt = DateTime.UtcNow,
                };
                post.Comments.Add(newComment);
            }
            else
            {
                // Add a reply to an existing comment
                var parentComment = post.Comments.FirstOrDefault(c => c.CommentId == commentId);
                if (parentComment == null)
                {
                    logger.LogWarning($"Comment with ID {commentId} not found.");
                    return req.CreateResponse(HttpStatusCode.NotFound);
                }

                var newReply = new Comment
                {
                    CommentId = Guid.NewGuid().ToString(),
                    auth0UserId = auth0UserId,
                    Text = text,
                    Name = name,
                    Email = commenterEmail,
                    Replies = new List<Comment>(), // Replies to replies are not allowed
                    CreatedAt = DateTime.UtcNow,
                };
                parentComment.Replies.Add(newReply);
            }

            // Update the post in the database
            try
            {
                await _dbContext.PostsContainer.ReplaceItemAsync(post, post.id);
            }
            catch (Exception ex)
            {
                logger.LogError($"Error updating post: {ex.Message}");
                return req.CreateResponse(HttpStatusCode.InternalServerError);
            }

            // Create the success response
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(
                new
                {
                    success = true,
                    message = "Comment/reply added successfully.",
                    postId = post.id,
                }
            );

            await SendEmailAsync(postEmail, text, name);

            return response;
        }

        public static async Task SendEmailAsync(
            string recipientEmail,
            string messageBody,
            string recipientName
        )
        {
            try
            {
                // Create a new Postmark message
                var message = new PostmarkMessage()
                {
                    To = recipientEmail,
                    From = "rhangal@willamette.edu",
                    TrackOpens = true,
                    Subject = $"{recipientName} Replied to You!",
                    TextBody = messageBody,
                    HtmlBody = $"<p>{messageBody} - {recipientName}</p>", // Wrap in HTML if needed
                    MessageStream = "outbound",
                    Tag = "General Notification",
                };

                // Initialize Postmark client
                var client = new PostmarkClient("88a7303c-126d-46d2-9f96-fda20cbfb8c4");

                // Send the message asynchronously
                var sendResult = await client.SendMessageAsync(message);

                if (sendResult.Status == PostmarkStatus.Success)
                {
                    Console.WriteLine("Email sent successfully.");
                }
                else
                {
                    Console.WriteLine($"Failed to send email: {sendResult.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while sending the email: {ex.Message}");
            }
        }

        [Function("DeleteCommentOrReply")]
        public async Task<HttpResponseData> DeleteCommentOrReply(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "posts/comments")]
                HttpRequestData req,
            FunctionContext executionContext
        )
        {
            var logger = executionContext.GetLogger("DeleteCommentOrReply");
            logger.LogInformation("Deleting comment/reply metadata...");

            try
            {
                // Read request body and deserialize to get the details (postId, commentId, optional replyId, auth0UserId)
                var requestBody = await req.ReadAsStringAsync();
                var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(requestBody);

                string postId = payload["postId"];
                string commentId = payload["commentId"];
                string? replyId = payload.ContainsKey("replyId") ? payload["replyId"] : null;
                string auth0UserId = payload["auth0UserId"];

                // Query the post by its postId
                var query = new QueryDefinition(
                    "SELECT * FROM c WHERE c.id = @postId"
                ).WithParameter("@postId", postId);

                var queryResultSetIterator = _dbContext.PostsContainer.GetItemQueryIterator<Post>(
                    query
                );
                if (queryResultSetIterator.HasMoreResults)
                {
                    var postResponse = await queryResultSetIterator.ReadNextAsync();
                    var post = postResponse.FirstOrDefault();

                    if (post == null)
                    {
                        logger.LogWarning("Post not found.");
                        return req.CreateResponse(HttpStatusCode.NotFound);
                    }

                    // Locate the comment or reply to delete based on commentId and optionally replyId
                    var comment = post.Comments.FirstOrDefault(c => c.CommentId == commentId);

                    if (comment == null)
                    {
                        logger.LogWarning("Comment not found.");
                        return req.CreateResponse(HttpStatusCode.NotFound);
                    }

                    // Check if the replyId is provided
                    if (replyId != null)
                    {
                        var reply = comment.Replies?.FirstOrDefault(r => r.CommentId == replyId);
                        if (reply != null && reply.auth0UserId == auth0UserId)
                        {
                            // Delete reply metadata and set text to deleted
                            reply.Text = "*this reply was deleted by the user*";
                            reply.auth0UserId = null;
                            reply.Email = null;
                            reply.Name = "User";
                        }
                        else
                        {
                            return req.CreateResponse(HttpStatusCode.Unauthorized);
                        }
                    }
                    else if (comment.auth0UserId == auth0UserId)
                    {
                        // Delete comment metadata and set text to deleted
                        comment.Text = "*this comment was deleted by the user*";
                        comment.auth0UserId = null;
                        comment.Email = null;
                        comment.Name = "User";
                    }
                    else
                    {
                        return req.CreateResponse(HttpStatusCode.Unauthorized);
                    }

                    // Replace the item in the Cosmos DB container
                    await _dbContext.PostsContainer.ReplaceItemAsync(post, post.id);

                    var response = req.CreateResponse(HttpStatusCode.OK);
                    var responseBody = new
                    {
                        success = true,
                        message = "Comment/reply deleted successfully.",
                    };

                    await response.WriteAsJsonAsync(responseBody);
                    return response;
                }

                // Return a not found response if no post is found
                return req.CreateResponse(HttpStatusCode.NotFound);
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
        }

        [Function("EditCommentOrReply")]
        public async Task<HttpResponseData> EditCommentOrReply(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "posts/comments")]
                HttpRequestData req,
            FunctionContext executionContext
        )
        {
            var logger = executionContext.GetLogger("EditCommentOrReply");
            logger.LogInformation("Editing comment/reply ...");

            try
            {
                // Read request body and deserialize to get the details (postId, commentId, optional replyId, auth0UserId)
                var requestBody = await req.ReadAsStringAsync();
                var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(requestBody);

                string text = payload["text"];
                string postId = payload["postId"];
                string commentId = payload["commentId"];
                string? replyId = payload.ContainsKey("replyId") ? payload["replyId"] : null;
                string auth0UserId = payload["auth0UserId"];

                // Query the post by its postId
                var query = new QueryDefinition(
                    "SELECT * FROM c WHERE c.id = @postId"
                ).WithParameter("@postId", postId);

                var queryResultSetIterator = _dbContext.PostsContainer.GetItemQueryIterator<Post>(
                    query
                );
                if (queryResultSetIterator.HasMoreResults)
                {
                    var postResponse = await queryResultSetIterator.ReadNextAsync();
                    var post = postResponse.FirstOrDefault();

                    if (post == null)
                    {
                        logger.LogWarning("Post not found.");
                        return req.CreateResponse(HttpStatusCode.NotFound);
                    }

                    // Locate the comment or reply to delete based on commentId and optionally replyId
                    var comment = post.Comments.FirstOrDefault(c => c.CommentId == commentId);

                    if (comment == null)
                    {
                        logger.LogWarning("Comment not found.");
                        return req.CreateResponse(HttpStatusCode.NotFound);
                    }

                    // Check if the replyId is provided
                    if (replyId != null)
                    {
                        var reply = comment.Replies?.FirstOrDefault(r => r.CommentId == replyId);
                        if (reply != null && reply.auth0UserId == auth0UserId)
                        {
                            // Delete reply metadata and set text to deleted
                            reply.Text = text + " (edited)";
                        }
                        else
                        {
                            return req.CreateResponse(HttpStatusCode.Unauthorized);
                        }
                    }
                    else if (comment.auth0UserId == auth0UserId)
                    {
                        // Delete comment metadata and set text to deleted
                        comment.Text = text + " (edited)";
                    }
                    else
                    {
                        return req.CreateResponse(HttpStatusCode.Unauthorized);
                    }

                    // Replace the item in the Cosmos DB container
                    await _dbContext.PostsContainer.ReplaceItemAsync(post, post.id);

                    var response = req.CreateResponse(HttpStatusCode.OK);
                    var responseBody = new
                    {
                        success = true,
                        message = "Comment/reply edited successfully.",
                    };

                    await response.WriteAsJsonAsync(responseBody);
                    return response;
                }

                // Return a not found response if no post is found
                return req.CreateResponse(HttpStatusCode.NotFound);
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
        }
    }
}
