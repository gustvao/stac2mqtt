using Serilog;
using stac2mqtt.Services.Consumed.Mqtt;
using stac2mqtt.Services.Consumed.SmartThings;
using System;
using System.Collections.Generic;
using System.Linq;

namespace stac2mqtt.Drivers
{
    public class DeviceManager
    {
        private Configuration.Configuration configuration { get; }
        private SmartThingsConnection smartThingsConnection { get; }
        private DriverManager driverManager { get; }
        private MqttConnection mqttConnection { get; }
        private List<IDevice> devices { get; set; }

        public DeviceManager(Configuration.Configuration configuration, SmartThingsConnection smartThingsConnection, DriverManager driverManager, MqttConnection mqttConnection)
        {
            this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            this.smartThingsConnection = smartThingsConnection ?? throw new ArgumentNullException(nameof(smartThingsConnection));
            this.driverManager = driverManager ?? throw new ArgumentNullException(nameof(driverManager));
            this.mqttConnection = mqttConnection ?? throw new ArgumentNullException(nameof(mqttConnection));
        }

        public void Setup()
        {
            var deviceIds = configuration.DeviceIds;

            var deviceStatuses = deviceIds.Select(deviceId => new
            {
                DeviceId = deviceId,
                Status = smartThingsConnection.GetDeviceStatus(deviceId)
            })
                                          .ToList();
            var supportedDevices = deviceStatuses.Select(entry => new
            {
                Drivers = (List<IDriver>)driverManager.SupportedDrivers(entry.Status),
                Status = entry
            }).ToList();

            devices = supportedDevices.SelectMany(entry => entry.Drivers.Select(d => (IDevice)d.RegisterDevice(entry.Status.DeviceId, entry.Status.Status))).ToList();

            deviceIds.ForEach(deviceId =>
            {
                var device = devices.Single(x => x.DeviceID == deviceId);
                var deviceConfig = configuration.Devices.FirstOrDefault(d => d.DeviceId == deviceId);

                Log.Information($"Configuring device '{deviceId}' - {deviceConfig?.Name ?? "Unnamed"}.");

                device.Driver.ClearHADeviceRegistrations(device);
                device.Driver.PublishNewHADevices(device);

                mqttConnection.SubscribeToMessageReceived((a) =>
                {
                    var newValue = System.Text.Encoding.UTF8.GetString(a.ApplicationMessage.PayloadSegment.Array);
                    var topic = a.ApplicationMessage.Topic;

                    device.Driver.DispatchMqttMessage(device, topic, newValue);
                });
            });
        }
    }
}
