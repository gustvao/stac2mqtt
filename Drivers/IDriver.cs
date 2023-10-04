using MQTTnet.Client;
using MQTTnet.Packets;
using Newtonsoft.Json.Linq;
using stac2mqtt.Services.Consumed.Mqtt;
using stac2mqtt.Services.Consumed.SmartThings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace stac2mqtt.Drivers
{
    public interface IDriver
    {
        void UseMqttClient(MqttConnection mqttConnection);
        void UseSmartThingsConnection(SmartThingsConnection smartThingsConnection);
        void UseConfiguration(Configuration.Configuration configuration);
        bool SupportsDevice(JObject stDeviceStatus);
        IDevice RegisterDevice(string stDeviceId, JObject stDeviceStatus);
        void PublishNewHADevices(IDevice device);
        void ClearHADeviceRegistrations(IDevice device);
        void DispatchMqttMessage(IDevice device, string topic, string newValue);
    }
}
