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

namespace stac2mqtt.Services.Consumed.SmartThings
{
    public class SmartThingsConnection
    {
        private Configuration.Configuration configuration { get; }
        private readonly ConfigurationManager configurationManager;
        private const string TOKEN_ENDPOINT = "https://auth-global.api.smartthings.com/oauth/token";
        private readonly HttpClient httpClient;

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
            try
            {
                string result = $"{configuration.SmartThings.APIBaseURL}/{deviceId}/commands"
                    .WithHeader("Authorization", $"Bearer {configuration.SmartThings.ApiToken}")
                    .PostStringAsync(commands)
                    .Result
                    .GetStringAsync()
                    .Result;
                
                return JsonConvert.DeserializeObject<dynamic>(result);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error sending commands to {DeviceId}: {Message}", deviceId, ex.Message);
                
                if (ex.InnerException?.Message?.Contains("401") == true)
                {
                    Log.Information("Token expired, attempting to refresh...");
                    RefreshAccessToken().Wait();
                    return SendCommands(deviceId, commands); // Retry after token refresh
                }
                
                return null;
            }
        }

        private async Task RefreshAccessTokenAsync()
        {
            try
            {
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
        }

        private Task RefreshAccessToken()
        {
            return RefreshAccessTokenAsync();
        }

        public async Task TryAuthorizationCodeFlowAsync(string code, string redirectUri)
        {
            try
            {
                var clientId = configuration.SmartThings.ClientId;
                var clientSecret = configuration.SmartThings.ClientSecret;
                
                if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
                {
                    Log.Error("Client ID or Secret missing. Cannot exchange authorization code for token.");
                    return;
                }

                var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["client_id"] = clientId,
                    ["redirect_uri"] = redirectUri
                });

                var auth = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{clientId}:{clientSecret}"));
                httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);

                var response = await httpClient.PostAsync(TOKEN_ENDPOINT, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var tokenResponse = JsonConvert.DeserializeObject<dynamic>(responseContent);
                    
                    var accessToken = tokenResponse.access_token.ToString();
                    var refreshToken = tokenResponse.refresh_token.ToString();
                    
                    // Save tokens and code to settings.json
                    await configurationManager.SaveTokensAsync(accessToken, refreshToken, clientId, clientSecret, code);
                    
                    Log.Information("Authorization code exchanged for tokens");
                }
                else
                {
                    Log.Error("Failed to exchange code for token: {StatusCode}, Response: {Response}", 
                        response.StatusCode, responseContent);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in authorization code flow: {Message}", ex.Message);
            }
        }
    }
}
