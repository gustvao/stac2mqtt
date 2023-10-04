using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System;
using Flurl.Http;

namespace stac2mqtt.Services.Consumed.SmartThings
{
    public class SmartThingsConnection
    {
        private Configuration.Configuration configuration { get; }

        public SmartThingsConnection(Configuration.Configuration configuration)
        {
            this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public dynamic GetDeviceStatus(string deviceId)
        {
            var url = $@"{configuration.SmartThings.APIBaseURL}/{deviceId}/components/main/status";
            var json = url.WithHeader("Authorization", $"Bearer {configuration.SmartThings.ApiToken}")
                          .GetStringAsync()
                          .Result;

            dynamic response = JsonConvert.DeserializeObject<JObject>(json);

            return response;
        }

        public void SendCommands(string deviceId, string commandsJson)
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
    }
}
