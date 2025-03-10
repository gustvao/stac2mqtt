using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System;
using Flurl.Http;
using System.Net.Http;
using System.Threading.Tasks;
using Serilog;
using System.Linq;
using System.Collections.Generic;

namespace stac2mqtt.Services.Consumed.SmartThings
{
    public class SmartThingsConnection
    {
        private Configuration.Configuration configuration { get; }
        private const string TOKEN_ENDPOINT = "https://auth-global.api.smartthings.com/oauth/token";
        private readonly HttpClient httpClient;
        private readonly TokenPersistenceService tokenPersistenceService;
        private string clientId;
        private string clientSecret;

        public SmartThingsConnection(Configuration.Configuration configuration)
        {
            this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            this.httpClient = new HttpClient();
            this.tokenPersistenceService = new TokenPersistenceService();
            
            // Load tokens from persistent storage on startup
            LoadPersistedTokensAsync().Wait();
        }

        private async Task LoadPersistedTokensAsync()
        {
            var (accessToken, refreshToken, clientId, clientSecret) = await tokenPersistenceService.LoadTokensAsync();
            
            if (!string.IsNullOrEmpty(accessToken) && !string.IsNullOrEmpty(refreshToken))
            {
                configuration.SmartThings.ApiToken = accessToken;
                configuration.SmartThings.RefreshToken = refreshToken;
                this.clientId = clientId;
                this.clientSecret = clientSecret;
                Log.Information("Loaded tokens from persistent storage");
            }
            else
            {
                Log.Information("No persisted tokens found, checking environment variables...");
                
                // Try to get values from environment variables 
                var envClientId = Environment.GetEnvironmentVariable("SmartThings__ClientId");
                var envClientSecret = Environment.GetEnvironmentVariable("SmartThings__ClientSecret");
                
                if (!string.IsNullOrEmpty(envClientId) && !string.IsNullOrEmpty(envClientSecret))
                {
                    this.clientId = envClientId;
                    this.clientSecret = envClientSecret;
                    Log.Information("Loaded client credentials from environment variables");
                    
                    // Save to persistent storage for future use
                    await tokenPersistenceService.SaveClientCredentialsAsync(envClientId, envClientSecret);
                }
                
                if (!string.IsNullOrEmpty(configuration.SmartThings.ApiToken) && 
                    !string.IsNullOrEmpty(configuration.SmartThings.RefreshToken))
                {
                    // Save the initial tokens from configuration to persistent storage
                    await tokenPersistenceService.SaveTokensAsync(
                        configuration.SmartThings.ApiToken,
                        configuration.SmartThings.RefreshToken,
                        this.clientId,
                        this.clientSecret);
                }
                else
                {
                    Log.Warning("No tokens found in persistent storage or environment variables!");
                }
            }
        }

        public dynamic GetDeviceStatus(string deviceId)
        {
            try
            {
                string result = $"{configuration.SmartThings.APIBaseURL}/{deviceId}/components/main/status"
                    .WithHeader("Authorization", $"Bearer {configuration.SmartThings.ApiToken}")
                    .GetStringAsync()
                    .Result;
                
                return JsonConvert.DeserializeObject<dynamic>(result);
            }
            catch (Exception ex)
            {
                // Check if it's a 401 error (look for inner FlurlHttpException)
                var flurlEx = ex as FlurlHttpException;
                if (flurlEx == null && ex is AggregateException aggEx)
                {
                    flurlEx = aggEx.InnerExceptions.FirstOrDefault() as FlurlHttpException;
                }
                
                if (flurlEx != null && flurlEx.StatusCode == 401)
                {
                    // Token expired, attempt refresh
                    Log.Information("API token expired, attempting to refresh...");
                    try
                    {
                        RefreshAccessTokenAsync().Wait();
                        
                        // Retry with new token
                        Log.Information("Retrying request with new token...");
                        string result = $"{configuration.SmartThings.APIBaseURL}/{deviceId}/components/main/status"
                            .WithHeader("Authorization", $"Bearer {configuration.SmartThings.ApiToken}")
                            .GetStringAsync()
                            .Result;
                        
                        return JsonConvert.DeserializeObject<dynamic>(result);
                    }
                    catch (Exception refreshEx)
                    {
                        Log.Error(refreshEx, "Failed to refresh token and retry request");
                        throw;
                    }
                }
                
                // Not a 401 or refresh failed
                Log.Error(ex, $"Failed to get device status for {deviceId}");
                throw;
            }
        }

        public void SendCommands(string deviceId, string commandsJson)
        {
            try
            {
                var url = $@"{configuration.SmartThings.APIBaseURL}/{deviceId}/commands";
                var json = url.WithHeader("Authorization", $"Bearer {configuration.SmartThings.ApiToken}")
                              .PostStringAsync(commandsJson)
                              .Result
                              .ResponseMessage
                              .Content
                              .ReadAsStringAsync()
                              .Result;
            }
            catch (FlurlHttpException ex)
            {
                if (ex.StatusCode == 401)
                {
                    // Token expired, refresh and retry
                    RefreshAccessToken().Wait();
                    
                    // Retry with new token
                    var url = $@"{configuration.SmartThings.APIBaseURL}/{deviceId}/commands";
                    var json = url.WithHeader("Authorization", $"Bearer {configuration.SmartThings.ApiToken}")
                                  .PostStringAsync(commandsJson)
                                  .Result
                                  .ResponseMessage
                                  .Content
                                  .ReadAsStringAsync()
                                  .Result;
                }
                else
                {
                    throw;
                }
            }
        }

        private async Task RefreshAccessTokenAsync()
        {
            try {
                Log.Information("Starting token refresh process...");
                
                // Check for required values
                if (string.IsNullOrEmpty(configuration.SmartThings.RefreshToken))
                {
                    Log.Error("Cannot refresh token: RefreshToken is missing from configuration");
                    throw new InvalidOperationException("RefreshToken is missing from configuration");
                }
                
                if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
                {
                    Log.Error("Cannot refresh token: ClientId or ClientSecret is missing");
                    throw new InvalidOperationException("ClientId or ClientSecret is missing");
                }
                
                Log.Information("Using client ID: {ClientId}", clientId);
                
                // Construct the full URL with query parameters
                string fullUrl = $"{TOKEN_ENDPOINT}?grant_type=refresh_token&refresh_token={Uri.EscapeDataString(configuration.SmartThings.RefreshToken)}";
                Log.Information("Request URL: {Url}", fullUrl);
                
                // Create the HTTP request with empty body
                var request = new HttpRequestMessage(HttpMethod.Post, fullUrl);
                
                // Add Basic auth header with client ID and secret
                var base64Auth = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", base64Auth);
                
                Log.Information("Request method: POST");
                Log.Information("Request headers: Authorization: Basic {Auth}", base64Auth);
                Log.Information("Request body: <empty>");
                
                // Send the request
                var response = await httpClient.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();
                
                Log.Information("Token refresh response status: {StatusCode}", response.StatusCode);
                Log.Information("Token refresh response: {Response}", responseContent);
                
                if (response.IsSuccessStatusCode)
                {
                    var tokenResponse = JsonConvert.DeserializeObject<dynamic>(responseContent);
                    
                    var newAccessToken = tokenResponse.access_token.ToString();
                    var newRefreshToken = tokenResponse.refresh_token.ToString();
                    
                    Log.Information("New access token: {AccessToken}", newAccessToken);
                    Log.Information("New refresh token: {RefreshToken}", newRefreshToken);
                    
                    configuration.SmartThings.ApiToken = newAccessToken;
                    configuration.SmartThings.RefreshToken = newRefreshToken;
                    
                    await tokenPersistenceService.SaveTokensAsync(newAccessToken, newRefreshToken, clientId, clientSecret);
                    Log.Information("Successfully refreshed access token and saved to persistent storage");
                }
                else
                {
                    Log.Error("Failed to refresh token: {StatusCode}, Response: {Response}", 
                        response.StatusCode, responseContent);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error refreshing access token: {Message}", ex.Message);
                throw;
            }
        }

        private Task RefreshAccessToken()
        {
            return RefreshAccessTokenAsync();
        }
    }
}
