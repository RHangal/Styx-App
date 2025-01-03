using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using AzureHttpRequestData = Microsoft.Azure.Functions.Worker.Http.HttpRequestData;

namespace Styx.Api.Utils // or your preferred namespace
{
    public static class Auth0TokenHelper
    {
        private static readonly string Auth0Domain;
        private static readonly string Auth0Audience;
        private static readonly ConfigurationManager<OpenIdConnectConfiguration> _configurationManager;

        // Static constructor to initialize domain, audience, and config manager once
        static Auth0TokenHelper()
        {
            Auth0Domain = Environment.GetEnvironmentVariable("AUTH0_DOMAIN");
            Auth0Audience = Environment.GetEnvironmentVariable("AUTH0_AUDIENCE");

            if (string.IsNullOrEmpty(Auth0Domain) || string.IsNullOrEmpty(Auth0Audience))
            {
                throw new InvalidOperationException(
                    "AUTH0_DOMAIN or AUTH0_AUDIENCE is not set in environment variables."
                );
            }

            // Build the well-known OpenID endpoint for your Auth0 tenant
            var wellKnownEndpoint = $"https://{Auth0Domain}/.well-known/openid-configuration";

            // ConfigurationManager automatically fetches and caches OIDC config + JWKS
            _configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                wellKnownEndpoint,
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever()
            );
        }

        /// <summary>
        /// Extracts the bearer token from the Authorization header.
        /// Throws UnauthorizedAccessException if not present or invalid.
        /// </summary>
        public static string GetBearerToken(AzureHttpRequestData req)
        {
            if (!req.Headers.TryGetValues("Authorization", out var authValues))
            {
                throw new UnauthorizedAccessException("Authorization header not found.");
            }

            var authHeader = authValues.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                throw new UnauthorizedAccessException("Invalid Authorization header format.");
            }

            return authHeader.Substring("Bearer ".Length).Trim();
        }

        /// <summary>
        /// Validates the token against Auth0's JWKS and returns the 'sub' claim if valid.
        /// </summary>
        public static async Task<string> ValidateTokenAndGetSub(string token)
        {
            // 1. Fetch OIDC config (cached by ConfigurationManager)
            var openIdConfig = await _configurationManager.GetConfigurationAsync();

            // 2. Build validation parameters
            var validationParameters = new TokenValidationParameters
            {
                ValidIssuer = $"https://{Auth0Domain}/", // e.g., https://my-tenant.us.auth0.com/
                ValidAudience = Auth0Audience, // e.g., https://api.stiyx.org
                IssuerSigningKeys = openIdConfig.SigningKeys, // JWKS from Auth0
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.Zero, // optional: reduce default 5 min skew
            };

            // 3. Validate token
            var handler = new JwtSecurityTokenHandler
            {
                InboundClaimTypeMap = new Dictionary<string, string>(), // don't map standard claims
            };

            var principal = handler.ValidateToken(
                token,
                validationParameters,
                out var validatedToken
            );

            // 4. Extract the 'sub' claim
            var subClaim = principal.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
            if (subClaim is null)
            {
                throw new SecurityTokenException("Token has no 'sub' claim.");
            }

            return subClaim;
        }
    }
}
