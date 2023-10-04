using MQTTnet.Client;
using MQTTnet.Server;
using MQTTnet;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace stac2mqtt.Services.Consumed.Mqtt
{
    public class MqttConnection
    {
        private Configuration.Configuration configuration;
        private IMqttClient mqttClient;

        public MqttConnection(Configuration.Configuration configuration)
        {
            this.configuration = configuration;
        }

        public void Setup()
        {
            mqttClient = ConnectMqttClient().Result;
        }

        public void SubscribeToTopicChanges(string topic)
        {
            mqttClient.SubscribeAsync(topic).Wait();
        }

        public void SubscribeToMessageReceived(Action<MqttApplicationMessageReceivedEventArgs> action)
        {
            mqttClient.ApplicationMessageReceivedAsync += (a) =>
            {
                try
                {
                    action?.Invoke(a);
                }
                catch (Exception ex)
                {
                    Thread.Sleep(100);
                    Log.Error(ex, "Unexpected error processing received MQTT message.");
                }

                return Task.CompletedTask;
            };
        }

        private async Task<IMqttClient> ConnectMqttClient()
        {
            var mqttFactory = new MqttFactory();
            var mqttClient = mqttFactory.CreateMqttClient();
            var mqttClientOptions = new MqttClientOptionsBuilder().WithTcpServer(configuration.MqttServer).Build();
            var response = await mqttClient.ConnectAsync(mqttClientOptions, CancellationToken.None);

            Log.Information("The MQTT client is connected.");
            Log.Debug(JsonConvert.SerializeObject(response));

            return mqttClient;
        }

        public void SendMessage(string topic, string payload, bool retain = false)
        {
            var message = new MqttApplicationMessage();

            message.Topic = topic;
            message.PayloadSegment = new ArraySegment<byte>(System.Text.Encoding.UTF8.GetBytes(payload));
            message.Retain = retain;

            mqttClient.PublishAsync(message).Wait();
        }
    }
}
