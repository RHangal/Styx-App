using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using HttpMultipartParser;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using MimeDetective;
using PostmarkDotNet;
using Styx.Api.Data;
using Styx.Api.Utils;

namespace Styx.Api.Functions
{
    public class MediaFunctions
    {
        private static readonly BlobServiceClient _blobServiceClient = new BlobServiceClient(
            Environment.GetEnvironmentVariable("BlobConnectionString")
        );

        private const string ContainerName = "media-files"; // Replace with your blob container name

        // Example to detect the MIME type from the file content
        public static string GetMimeTypeFromStream(Stream stream)
        {
            // Initialize the ContentInspector
            var inspector = new ContentInspectorBuilder()
            {
                Definitions = MimeDetective.Definitions.Default.All(),
            }.Build();

            // Use the inspector to inspect the stream and get results
            var results = inspector.Inspect(stream);

            // Check and print if no MIME type matches are found
            var mimeTypeMatch = results.ByMimeType().FirstOrDefault();
            if (mimeTypeMatch == null)
            {
                Console.WriteLine("No MIME type match found.");
            }
            else
            {
                Console.WriteLine($"MIME Type found: {mimeTypeMatch.MimeType}");
            }
            // Return the MIME type directly without any fallback logic
            return results.ByMimeType().FirstOrDefault().MimeType;
        }

        [Function("UploadMedia")]
        public static async Task<HttpResponseData> UploadMedia(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "media/upload")]
                HttpRequestData req
        )
        {
            var logger = req.FunctionContext.GetLogger("UploadMedia");
            logger.LogInformation("Processing media upload.");

            // 1. Extract the bearer token from the Authorization header
            string token;
            try
            {
                token = Auth0TokenHelper.GetBearerToken(req);
                // Or your local helper method if you're not using an Auth0TokenHelper class
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogError(ex.Message);
                var authError = req.CreateResponse(HttpStatusCode.Unauthorized);
                await authError.WriteStringAsync("Missing or invalid Authorization header.");
                return authError;
            }

            // 2. Validate token and retrieve the 'sub' (auth0UserId)
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

            using var stream = req.Body;

            // 3. Parse the multipart form data (file + optional "profile" field)
            MultipartFormDataParser parser;
            try
            {
                parser = await MultipartFormDataParser.ParseAsync(stream);
            }
            catch (Exception ex)
            {
                logger.LogError("Failed to parse multipart form data: {0}", ex.Message);
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Invalid multipart form data.");
                return badRequestResponse;
            }

            // (Optional) "profile" field to determine if there's a subfolder
            var profileField = parser.Parameters.FirstOrDefault(f => f.Name == "profile");
            string profile = profileField?.Data;
            logger.LogInformation($"Received profile: {profile}");

            // 4. Ensure a file exists in the form data
            var file = parser.Files.FirstOrDefault();
            if (file == null)
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("No file found in the request.");
                return badRequestResponse;
            }

            var fileName = file.FileName;
            var fileStream = file.Data;

            // 5. Define blob storage details
            // (assuming _blobServiceClient is a class-level or static field)
            var containerClient = _blobServiceClient.GetBlobContainerClient("media-files");
            await containerClient.CreateIfNotExistsAsync();
            // Optionally set access policy
            // await containerClient.SetAccessPolicyAsync(PublicAccessType.Blob);

            // 6. Construct the blob path using auth0UserId and optional profile
            string blobPath = string.IsNullOrWhiteSpace(profile)
                ? $"{auth0UserId}/{fileName}"
                : $"{auth0UserId}/{profile}/{fileName}";

            // 7. Detect MIME type using your existing GetMimeTypeFromStream(fileStream)
            string contentType = GetMimeTypeFromStream(fileStream);

            // Reset file stream to the beginning before uploading
            fileStream.Position = 0;

            // 8. Upload the file to Blob Storage
            var blobClient = containerClient.GetBlobClient(blobPath);
            await blobClient.UploadAsync(
                fileStream,
                new BlobHttpHeaders { ContentType = contentType }
            );

            // 9. Return success response with file URL
            var response = req.CreateResponse(HttpStatusCode.OK);
            var fileUrl = blobClient.Uri.ToString();

            await response.WriteStringAsync(
                JsonSerializer.Serialize(
                    new { message = "File uploaded successfully.", url = fileUrl }
                )
            );

            return response;
        }
    }
}
