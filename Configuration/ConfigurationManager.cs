using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
// using Microsoft.Extensions.Configuration;
// using Newtonsoft.Json;
using Serilog;
using System.Threading.Tasks;

namespace stac2mqtt.Configuration
{
    /// <summary>
    /// Manages configuration persistence for the application.
    /// This class handles saving configuration changes back to the settings file.
    /// Initial loading is handled by ASP.NET Core's configuration system.
    /// </summary>
    public class ConfigurationManager
    {
        private readonly Configuration _configuration;
        private readonly string _configFilePath;
        
        /// <summary>
        /// Initializes a new instance of the ConfigurationManager class.
        /// </summary>
        /// <param name="configuration">Configuration object populated by ASP.NET Core's configuration system</param>
        /// <param name="configFilePath">Path to the settings file</param>
        public ConfigurationManager(Configuration configuration, string configFilePath = "data/settings.json")
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _configFilePath = configFilePath;
        }


        /// <summary>
        /// Saves the current configuration to the settings file.
        /// </summary>
        public void SaveConfiguration()
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            string jsonString = JsonSerializer.Serialize(_configuration, options);
            File.WriteAllText(_configFilePath, jsonString);
        }

        /// <summary>
        /// Saves all SmartThings authentication information to the configuration file.
        /// </summary>
        public async Task SaveTokensAsync(string accessToken, string refreshToken)
        {
            _configuration.SmartThings.ApiToken = accessToken;
            _configuration.SmartThings.RefreshToken = refreshToken;
            _configuration.TokenLastUpdated = DateTime.UtcNow;
            
            await Task.Run(() => SaveConfiguration());
        }

    }
}