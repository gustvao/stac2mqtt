using Newtonsoft.Json;
using System;
using System.IO;
using System.Threading.Tasks;
using Serilog;

namespace stac2mqtt.Services.Consumed.SmartThings
{
    public class TokenPersistenceService
    {
        private readonly string _tokenFilePath;

        public TokenPersistenceService(string tokenFilePath = "/data/smartthings_tokens.json")
        {
            _tokenFilePath = tokenFilePath;
            
            // Ensure directory exists
            var directory = Path.GetDirectoryName(_tokenFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        public async Task SaveTokensAsync(string accessToken, string refreshToken, string clientId = null, string clientSecret = null)
        {
            try
            {
                // Load existing tokens first to preserve clientId/secret if not provided
                var existingTokens = await LoadTokensInternalAsync();
                
                var tokens = new
                {
                    AccessToken = accessToken,
                    RefreshToken = refreshToken,
                    ClientId = clientId ?? existingTokens?.ClientId,
                    ClientSecret = clientSecret ?? existingTokens?.ClientSecret,
                    LastUpdated = DateTime.UtcNow
                };

                var json = JsonConvert.SerializeObject(tokens, Formatting.Indented);
                await File.WriteAllTextAsync(_tokenFilePath, json);
                Log.Information("Tokens successfully saved to persistent storage");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save tokens to persistent storage");
                // Don't throw - this is a non-critical operation
            }
        }
        
        public async Task SaveClientCredentialsAsync(string clientId, string clientSecret)
        {
            try
            {
                // Load existing tokens first
                var existingTokens = await LoadTokensInternalAsync();
                
                var tokens = new
                {
                    AccessToken = existingTokens?.AccessToken,
                    RefreshToken = existingTokens?.RefreshToken,
                    ClientId = clientId,
                    ClientSecret = clientSecret,
                    LastUpdated = DateTime.UtcNow
                };

                var json = JsonConvert.SerializeObject(tokens, Formatting.Indented);
                await File.WriteAllTextAsync(_tokenFilePath, json);
                Log.Information("Client credentials successfully saved to persistent storage");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save client credentials to persistent storage");
            }
        }

        private async Task<dynamic> LoadTokensInternalAsync()
        {
            try
            {
                if (File.Exists(_tokenFilePath))
                {
                    var json = await File.ReadAllTextAsync(_tokenFilePath);
                    return JsonConvert.DeserializeObject<dynamic>(json);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load tokens from persistent storage");
            }
            
            return null;
        }

        public async Task<(string accessToken, string refreshToken, string clientId, string clientSecret)> LoadTokensAsync()
        {
            try
            {
                var tokens = await LoadTokensInternalAsync();
                
                if (tokens != null)
                {
                    Log.Information("Loaded tokens from persistent storage (last updated: {LastUpdated})", 
                        tokens.LastUpdated);
                    
                    return (
                        tokens.AccessToken?.ToString(),
                        tokens.RefreshToken?.ToString(),
                        tokens.ClientId?.ToString(), 
                        tokens.ClientSecret?.ToString()
                    );
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load tokens from persistent storage");
                // Don't throw - we'll fall back to configuration values
            }
            
            return (null, null, null, null);
        }
    }
} 