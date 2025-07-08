using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System;
using Flurl.Http;
using System.Net.Http;
using System.Threading.Tasks;
using Serilog;
using System.Linq;
using System.Collections.Generic;
using stac2mqtt.Configuration;
using System.Threading;

namespace stac2mqtt.Services.Consumed.SmartThings
{
    public class SmartThingsConnection
    {
        private Configuration.Configuration configuration { get; }
        private readonly ConfigurationManager configurationManager;
        private const string TOKEN_ENDPOINT = "https://auth-global.api.smartthings.com/oauth/token";
        private readonly HttpClient httpClient;
        
        // Semaphore to ensure only one token refresh happens at a time
        private static SemaphoreSlim _tokenRefreshSemaphore = new SemaphoreSlim(1, 1);
        // Flag to track if a token refresh is in progress
        private static bool _tokenRefreshInProgress = false;

        public SmartThingsConnection(Configuration.Configuration configuration, ConfigurationManager configurationManager)
        {
            this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            this.configurationManager = configurationManager ?? throw new ArgumentNullException(nameof(configurationManager));
            this.httpClient = new HttpClient();
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
                Log.Error(ex, "Error retrieving device status for {DeviceId}: {Message}", deviceId, ex.Message);
                
                if (ex.InnerException?.Message?.Contains("401") == true)
                {
                    Log.Information("Token expired, attempting to refresh...");
                    RefreshAccessToken().Wait();
                    return GetDeviceStatus(deviceId); // Retry after token refresh
                }
                
                return null;
            }
        }

        public dynamic SendCommands(string deviceId, string commands)
        {
            return SendCommandsWithRetry(deviceId, commands, 0).Result;
        }

        private async Task<dynamic> SendCommandsWithRetry(string deviceId, string commands, int retryCount)
        {
            const int maxRetries = 3;
            var baseDelayMs = configuration.Intervals.CommandRetryDelay; // Use configurable delay

            try
            {
                string result = await $"{configuration.SmartThings.APIBaseURL}/{deviceId}/commands"
                    .WithHeader("Authorization", $"Bearer {configuration.SmartThings.ApiToken}")
                    .PostStringAsync(commands)
                    .Result
                    .GetStringAsync();
                
                return JsonConvert.DeserializeObject<dynamic>(result);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error sending commands to {DeviceId}: {Message}", deviceId, ex.Message);
                
                // Handle 401 Unauthorized (token expired)
                if (ex.InnerException?.Message?.Contains("401") == true)
                {
                    Log.Information("Token expired, attempting to refresh...");
                    await RefreshAccessTokenAsync();
                    return await SendCommandsWithRetry(deviceId, commands, retryCount); // Retry after token refresh
                }
                
                // Handle 409 Conflict (rate limiting or device busy)
                if (ex.InnerException?.Message?.Contains("409") == true)
                {
                    if (retryCount < maxRetries)
                    {
                        var delayMs = baseDelayMs * (int)Math.Pow(2, retryCount); // Exponential backoff
                        Log.Warning("Received 409 Conflict for device {DeviceId}, retrying in {Delay}ms (attempt {RetryCount}/{MaxRetries})", 
                            deviceId, delayMs, retryCount + 1, maxRetries);
                        
                        await Task.Delay(delayMs);
                        return await SendCommandsWithRetry(deviceId, commands, retryCount + 1);
                    }
                    else
                    {
                        Log.Error("Max retries reached for device {DeviceId} after 409 conflicts. Giving up.", deviceId);
                    }
                }
                
                return null;
            }
        }

        private async Task RefreshAccessTokenAsync()
        {
            // Only proceed if no token refresh is in progress
            if (_tokenRefreshInProgress)
            {
                Log.Information("Token refresh already in progress. Waiting for completion...");
                
                // Wait until the existing refresh operation completes
                while (_tokenRefreshInProgress)
                {
                    await Task.Delay(100);
                }
                
                return; // Token has been refreshed by another request
            }
            
            // Try to enter the semaphore to ensure only one refresh happens at a time
            await _tokenRefreshSemaphore.WaitAsync();
            
            try
            {
                // Double-check if another thread has refreshed the token while we were waiting
                if (_tokenRefreshInProgress)
                {
                    return; // Another thread is already refreshing
                }
                
                _tokenRefreshInProgress = true;
                
                var clientId = configuration.SmartThings.ClientId;
                var clientSecret = configuration.SmartThings.ClientSecret;
                
                if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
                {
                    Log.Error("Client ID or Client Secret missing. Cannot refresh token.");
                    throw new InvalidOperationException("Client credentials required for token refresh");
                }

                var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = configuration.SmartThings.RefreshToken,
                    ["client_id"] = clientId
                });

                var auth = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{clientId}:{clientSecret}"));
                httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);

                var response = await httpClient.PostAsync(TOKEN_ENDPOINT, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var tokenResponse = JsonConvert.DeserializeObject<dynamic>(responseContent);
                    
                    var newAccessToken = tokenResponse.access_token.ToString();
                    var newRefreshToken = tokenResponse.refresh_token.ToString();
                    
                    Log.Information("New access token obtained");
                    
                    // Save updated tokens directly to settings.json using ConfigurationManager
                    await configurationManager.SaveTokensAsync(newAccessToken, newRefreshToken);
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
            finally
            {
                // Reset the flag and release the semaphore
                _tokenRefreshInProgress = false;
                _tokenRefreshSemaphore.Release();
            }
        }

        private Task RefreshAccessToken()
        {
            return RefreshAccessTokenAsync();
        }
    }
}
