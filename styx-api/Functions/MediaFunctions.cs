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
using MimeDetective;
using PostmarkDotNet;
using Styx.Api.Data;

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

            using var stream = req.Body;

            // Parse the form data
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

            // Log the Auth0UserId passed in the body (assuming it's passed as a part of the form data)
            var auth0UserIdField = parser.Parameters.FirstOrDefault(f => f.Name == "auth0UserId");
            if (auth0UserIdField == null)
            {
                logger.LogError("Auth0UserId is missing from the request.");
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Auth0UserId is required.");
                return badRequestResponse;
            }

            // Get the value of the form field
            var auth0UserId = auth0UserIdField.Data;
            logger.LogInformation($"Received Auth0UserId: {auth0UserId}");

            // Get the optional profile field
            var profileField = parser.Parameters.FirstOrDefault(f => f.Name == "profile");
            string profile = profileField?.Data; // Null if not provided
            logger.LogInformation($"Received profile: {profile}");

            // Get the file
            var file = parser.Files.FirstOrDefault();
            if (file == null)
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("No file found in the request.");
                return badRequestResponse;
            }

            var fileName = file.FileName;
            var fileStream = file.Data;

            // Define blob storage details
            var containerClient = _blobServiceClient.GetBlobContainerClient("media-files");

            // Ensure the container exists
            await containerClient.CreateIfNotExistsAsync();
            // await containerClient.SetAccessPolicyAsync(PublicAccessType.Blob);


            // Create the blob path with optional profile subfolder
            string blobPath = string.IsNullOrWhiteSpace(profile)
                ? $"{auth0UserId}/{fileName}" // No profile, use only Auth0UserId
                : $"{auth0UserId}/{profile}/{fileName}"; // Include profile as a subfolder

            // Get MIME type dynamically using Mime-Detective
            string contentType = GetMimeTypeFromStream(fileStream);

            // Ensure the stream position is set to 0 before uploading
            fileStream.Position = 0;
            // Upload the file
            var blobClient = containerClient.GetBlobClient(blobPath);
            await blobClient.UploadAsync(
                fileStream,
                new BlobHttpHeaders { ContentType = contentType }
            );

            // Return the response with the file URL
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
