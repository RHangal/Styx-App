// using System; // For basic types like Guid, DateTime, etc.
// using System.IO; // For Stream and reading/writing file streams
// using System.Linq; // For LINQ operations, such as `FirstOrDefault()`
// using System.Net; // For HttpStatusCode
// using System.Net.Http.Headers;
// using System.Text.Json; // For JSON serialization/deserialization
// using System.Threading.Tasks; // For async/await
// using Azure.Storage.Blobs; // For working with Azure Blob Storage
// using Azure.Storage.Blobs.Models;
// using HttpMultipartParser; // Add this for multipart parsing
// using Microsoft.AspNetCore.WebUtilities; // For `MultipartReader` and related utilities
// using Microsoft.Azure.Cosmos; // For interacting with Cosmos DB
// using Microsoft.Azure.Functions.Worker; // For Azure Functions worker
// using Microsoft.Azure.Functions.Worker.Http; // For working with HTTP requests and responses in Functions
// using Microsoft.Extensions.Logging; // For logging in Azure Functions
// using Microsoft.Extensions.Options;
// using Microsoft.Extensions.WebEncoders.Testing;
// using MimeDetective;
// using PostmarkDotNet;

// namespace Styx.Api
// {
//     public class HttpExample
//     {

//         private static readonly CosmosClient _cosmosClient = new CosmosClient(
//             Environment.GetEnvironmentVariable("CosmosDbConnectionSetting")
//         );
//         private static readonly Container _usersContainer = _cosmosClient.GetContainer(
//             "my-database",
//             "users"
//         );
//         private static readonly Container _postsContainer = _cosmosClient.GetContainer(
//             "my-database",
//             "posts"
//         );
//         private static readonly Container _categoriesContainer = _cosmosClient.GetContainer(
//             "my-database",
//             "categories"
//         );
//         private static readonly Container _badgesContainer = _cosmosClient.GetContainer(
//             "my-database",
//             "badges"
//         );
//         private static readonly BlobServiceClient _blobServiceClient = new BlobServiceClient(
//             Environment.GetEnvironmentVariable("BlobConnectionString")
//         );

//         private const string ContainerName = "media-files"; // Replace with your blob container name












//     }
// }
