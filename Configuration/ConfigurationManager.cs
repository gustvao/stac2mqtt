using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Serilog;
using System;
using System.IO;
using System.Threading.Tasks;

namespace stac2mqtt.Configuration
{
    public class ConfigurationManager
    {
        private readonly string _configFilePath;
        private readonly Configuration _configuration;
        
        public ConfigurationManager(Configuration configuration, string configFilePath = "settings.json")
        {
            _configuration = configuration;
            _configFilePath = configFilePath;
        }
        
        public async Task SaveConfigurationAsync()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_configuration, Formatting.Indented);
                await File.WriteAllTextAsync(_configFilePath, json);
                Log.Information("Configuration saved to {ConfigFilePath}", _configFilePath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save configuration to {ConfigFilePath}", _configFilePath);
            }
        }
        
        public async Task SaveTokensAsync(string accessToken, string refreshToken, string clientId = null, string clientSecret = null, string code = null)
        {
            try
            {
                if (!string.IsNullOrEmpty(accessToken))
                    _configuration.SmartThings.ApiToken = accessToken;
                    
                if (!string.IsNullOrEmpty(refreshToken))
                    _configuration.SmartThings.RefreshToken = refreshToken;
                    
                if (!string.IsNullOrEmpty(clientId))
                    _configuration.SmartThings.ClientId = clientId;
                    
                if (!string.IsNullOrEmpty(clientSecret))
                    _configuration.SmartThings.ClientSecret = clientSecret;
                    
                if (!string.IsNullOrEmpty(code))
                    _configuration.SmartThings.Code = code;
                
                _configuration.TokenLastUpdated = DateTime.UtcNow;
                
                await SaveConfigurationAsync();
                Log.Information("Tokens successfully saved to settings file");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save tokens to settings file");
            }
        }
    }
} 