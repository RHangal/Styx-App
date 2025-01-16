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
using PostmarkDotNet;
using Styx.Api.Data;
using Styx.Api.Models;
using Styx.Api.Utils; // <-- Where your Auth0TokenHelper resides

namespace Styx.Api.Functions
{
    public class CommentFunctions
    {
        private readonly DatabaseContext _dbContext;

        public CommentFunctions(DatabaseContext dbContext)
        {
            _dbContext = dbContext;
        }

        // --------------------------------------------------
        // Add a comment or reply
        // --------------------------------------------------
        [Function("AddComment")]
        public async Task<HttpResponseData> AddComment(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "posts/comments")]
                HttpRequestData req,
            FunctionContext executionContext
        )
        {
            var logger = executionContext.GetLogger("AddComment");
            logger.LogInformation("Processing comment/reply addition via token-based user ID.");

            // 1) Extract + validate JWT
            string token;
            try
            {
                token = Auth0TokenHelper.GetBearerToken(req);
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogError($"Token extraction error: {ex.Message}");
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
                return null;
            }
            catch (Exception ex)
            {
                logger.LogError($"Token validation error: {ex.Message}");
                var tokenError = req.CreateResponse(HttpStatusCode.InternalServerError);
                await tokenError.WriteStringAsync("Token validation error.");
                return null;
            }

            // 2) Parse request body
            var requestBody = await req.ReadAsStringAsync();
            var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(requestBody);

            // Validate required fields (except auth0UserId, which we have from the token)
            if (
                payload == null
                || !payload.TryGetValue("postId", out var postId)
                || string.IsNullOrEmpty(postId)
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
                    "Missing required fields: postId, text, commenterEmail, postEmail, or name."
                );
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync(
                    "Invalid payload. postId, text, commenterEmail, postEmail, and name are required."
                );
                return badRequestResponse;
            }

            payload.TryGetValue("commentId", out var commentId); // optional, determines if top-level or reply

            // 3) Retrieve the post
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

            // 4) Add top-level comment or reply
            if (string.IsNullOrEmpty(commentId))
            {
                // top-level comment
                var newComment = new Comment
                {
                    CommentId = Guid.NewGuid().ToString(),
                    auth0UserId = auth0UserId,
                    Text = text,
                    Name = name,
                    Email = commenterEmail, // you can store or omit
                    Replies = new List<Comment>(),
                    CreatedAt = DateTime.UtcNow,
                };
                post.Comments.Add(newComment);
            }
            else
            {
                // reply to an existing comment
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
                    Replies = new List<Comment>(), // no nested replies
                    CreatedAt = DateTime.UtcNow,
                };
                parentComment.Replies.Add(newReply);
            }

            // 5) Update the post in the DB
            try
            {
                await _dbContext.PostsContainer.ReplaceItemAsync(post, post.id);
            }
            catch (Exception ex)
            {
                logger.LogError($"Error updating post (with comment): {ex.Message}");
                return req.CreateResponse(HttpStatusCode.InternalServerError);
            }

            // 6) Return success
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(
                new
                {
                    success = true,
                    message = "Comment/reply added successfully.",
                    postId = post.id,
                }
            );

            // Optionally send an email
            await SendEmailAsync(postEmail, text, name);

            return response;
        }

        // For demonstration, you’re emailing the post owner, so no sub-based check here.
        public static async Task SendEmailAsync(
            string recipientEmail,
            string messageBody,
            string recipientName
        )
        {
            try
            {
                var message = new PostmarkMessage()
                {
                    To = recipientEmail,
                    From = "rhangal@willamette.edu",
                    TrackOpens = true,
                    Subject = $"{recipientName} Replied to You!",
                    TextBody = messageBody,
                    HtmlBody = $"<p>{messageBody} - {recipientName}</p>",
                    MessageStream = "outbound",
                    Tag = "General Notification",
                };

                var client = new PostmarkClient("YOUR-POSTMARK-SERVER-TOKEN");
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

        // --------------------------------------------------
        // Delete comment or reply
        // --------------------------------------------------
        [Function("DeleteCommentOrReply")]
        public async Task<HttpResponseData> DeleteCommentOrReply(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "posts/comments")]
                HttpRequestData req,
            FunctionContext executionContext
        )
        {
            var logger = executionContext.GetLogger("DeleteCommentOrReply");
            logger.LogInformation("Deleting comment/reply using token-based user ID...");

            // 1) Extract + validate token
            string token;
            try
            {
                token = Auth0TokenHelper.GetBearerToken(req);
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogError($"Token extraction error: {ex.Message}");
                var badAuth = req.CreateResponse(HttpStatusCode.Unauthorized);
                await badAuth.WriteStringAsync("Missing or invalid Authorization header.");
                return badAuth;
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
                return null;
            }
            catch (Exception ex)
            {
                logger.LogError($"Token validation error: {ex.Message}");
                var tokenError = req.CreateResponse(HttpStatusCode.InternalServerError);
                await tokenError.WriteStringAsync("Token validation error.");
                return null;
            }

            // 2) Parse request for postId, commentId, optional replyId
            string requestBody;
            try
            {
                requestBody = await req.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                logger.LogError($"Error reading request body: {ex.Message}");
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteStringAsync("Invalid request body.");
                return badRequest;
            }

            var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(requestBody);
            if (
                payload == null
                || !payload.TryGetValue("postId", out var postId)
                || string.IsNullOrEmpty(postId)
                || !payload.TryGetValue("commentId", out var commentId)
                || string.IsNullOrEmpty(commentId)
            )
            {
                var missingFields = req.CreateResponse(HttpStatusCode.BadRequest);
                await missingFields.WriteStringAsync(
                    "Missing required fields: postId or commentId."
                );
                return missingFields;
            }
            payload.TryGetValue("replyId", out var replyId); // optional

            // 3) Retrieve the post
            Post post;
            try
            {
                var query = new QueryDefinition(
                    "SELECT * FROM c WHERE c.id = @postId"
                ).WithParameter("@postId", postId);

                var iterator = _dbContext.PostsContainer.GetItemQueryIterator<Post>(query);
                if (iterator.HasMoreResults)
                {
                    var postResponse = await iterator.ReadNextAsync();
                    post = postResponse.FirstOrDefault();

                    if (post == null)
                    {
                        logger.LogWarning("Post not found.");
                        return req.CreateResponse(HttpStatusCode.NotFound);
                    }
                }
                else
                {
                    return req.CreateResponse(HttpStatusCode.NotFound);
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"Error retrieving post: {ex.Message}");
                return req.CreateResponse(HttpStatusCode.InternalServerError);
            }

            // 4) Locate the comment or reply
            var comment = post.Comments.FirstOrDefault(c => c.CommentId == commentId);
            if (comment == null)
            {
                logger.LogWarning("Comment not found.");
                return req.CreateResponse(HttpStatusCode.NotFound);
            }

            // If replyId is provided
            if (!string.IsNullOrEmpty(replyId))
            {
                var reply = comment.Replies?.FirstOrDefault(r => r.CommentId == replyId);
                if (reply == null)
                {
                    logger.LogWarning("Reply not found.");
                    return req.CreateResponse(HttpStatusCode.NotFound);
                }

                // Check ownership
                if (reply.auth0UserId != auth0UserId)
                {
                    return req.CreateResponse(HttpStatusCode.Unauthorized);
                }

                // "Delete" the reply by scrubbing data
                reply.Text = "*this reply was deleted by the user*";
                reply.auth0UserId = null;
                reply.Email = null;
                reply.Name = "User";
            }
            else
            {
                // top-level comment
                if (comment.auth0UserId != auth0UserId)
                {
                    return req.CreateResponse(HttpStatusCode.Unauthorized);
                }

                // "Delete" the comment
                comment.Text = "*this comment was deleted by the user*";
                comment.auth0UserId = null;
                comment.Email = null;
                comment.Name = "User";
            }

            // 5) Replace item in Cosmos DB
            try
            {
                await _dbContext.PostsContainer.ReplaceItemAsync(post, post.id);
            }
            catch (Exception ex)
            {
                logger.LogError($"Error updating post: {ex.Message}");
                return req.CreateResponse(HttpStatusCode.InternalServerError);
            }

            // 6) Return success
            var successResponse = req.CreateResponse(HttpStatusCode.OK);
            await successResponse.WriteAsJsonAsync(
                new { success = true, message = "Comment/reply deleted successfully." }
            );
            return successResponse;
        }

        // --------------------------------------------------
        // Edit comment or reply
        // --------------------------------------------------
        [Function("EditCommentOrReply")]
        public async Task<HttpResponseData> EditCommentOrReply(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "posts/comments")]
                HttpRequestData req,
            FunctionContext executionContext
        )
        {
            var logger = executionContext.GetLogger("EditCommentOrReply");
            logger.LogInformation("Editing comment/reply using token-based user ID...");

            // 1) Extract + validate token
            string token;
            try
            {
                token = Auth0TokenHelper.GetBearerToken(req);
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogError($"Token extraction error: {ex.Message}");
                var noAuth = req.CreateResponse(HttpStatusCode.Unauthorized);
                await noAuth.WriteStringAsync("Missing or invalid Authorization header.");
                return noAuth;
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
                return null;
            }
            catch (Exception ex)
            {
                logger.LogError($"Token validation error: {ex.Message}");
                var tokenError = req.CreateResponse(HttpStatusCode.InternalServerError);
                await tokenError.WriteStringAsync("Token validation error.");
                return null;
            }

            // 2) Parse the request body
            var requestBody = await req.ReadAsStringAsync();
            var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(requestBody);

            // Required fields
            if (
                payload == null
                || !payload.TryGetValue("text", out var newText)
                || string.IsNullOrEmpty(newText)
                || !payload.TryGetValue("postId", out var postId)
                || string.IsNullOrEmpty(postId)
                || !payload.TryGetValue("commentId", out var commentId)
                || string.IsNullOrEmpty(commentId)
            )
            {
                var missingFields = req.CreateResponse(HttpStatusCode.BadRequest);
                await missingFields.WriteStringAsync(
                    "Missing required fields: text, postId, commentId."
                );
                return missingFields;
            }
            payload.TryGetValue("replyId", out var replyId);

            // 3) Retrieve the post
            Post post;
            try
            {
                var query = new QueryDefinition(
                    "SELECT * FROM c WHERE c.id = @postId"
                ).WithParameter("@postId", postId);

                var iterator = _dbContext.PostsContainer.GetItemQueryIterator<Post>(query);
                if (iterator.HasMoreResults)
                {
                    var postResponse = await iterator.ReadNextAsync();
                    post = postResponse.FirstOrDefault();

                    if (post == null)
                    {
                        logger.LogWarning("Post not found.");
                        return req.CreateResponse(HttpStatusCode.NotFound);
                    }
                }
                else
                {
                    return req.CreateResponse(HttpStatusCode.NotFound);
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"Error retrieving post: {ex.Message}");
                return req.CreateResponse(HttpStatusCode.InternalServerError);
            }

            // 4) Locate the comment
            var comment = post.Comments.FirstOrDefault(c => c.CommentId == commentId);
            if (comment == null)
            {
                logger.LogWarning("Comment not found.");
                return req.CreateResponse(HttpStatusCode.NotFound);
            }

            if (!string.IsNullOrEmpty(replyId))
            {
                // edit a reply
                var reply = comment.Replies?.FirstOrDefault(r => r.CommentId == replyId);
                if (reply == null)
                {
                    logger.LogWarning("Reply not found.");
                    return req.CreateResponse(HttpStatusCode.NotFound);
                }

                if (reply.auth0UserId != auth0UserId)
                {
                    return req.CreateResponse(HttpStatusCode.Unauthorized);
                }

                reply.Text = $"{newText} (edited)";
            }
            else
            {
                // edit the top-level comment
                if (comment.auth0UserId != auth0UserId)
                {
                    return req.CreateResponse(HttpStatusCode.Unauthorized);
                }

                comment.Text = $"{newText} (edited)";
            }

            // 5) Update the post
            try
            {
                await _dbContext.PostsContainer.ReplaceItemAsync(post, post.id);
            }
            catch (Exception ex)
            {
                logger.LogError($"Error updating post: {ex.Message}");
                return req.CreateResponse(HttpStatusCode.InternalServerError);
            }

            // 6) Return success
            var success = req.CreateResponse(HttpStatusCode.OK);
            await success.WriteAsJsonAsync(
                new { success = true, message = "Comment/reply edited successfully." }
            );
            return success;
        }
    }
}
