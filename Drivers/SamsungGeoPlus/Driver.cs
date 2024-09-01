using Newtonsoft.Json.Linq;
using System;
using System.Threading;
using System.Threading.Tasks;
using stac2mqtt.Services.Consumed.Mqtt;
using Serilog;
using CaseExtensions;
using stac2mqtt.Services.Consumed.SmartThings;

namespace stac2mqtt.Drivers.SamsungGeoPlus
{
    public class Driver : IDriver
    {
        private MqttConnection mqttConnection;
        private SmartThingsConnection smartThingsConnection;
        private Configuration.Configuration configuration;

        public void UseSmartThingsConnection(SmartThingsConnection smartThingsConnection)
        {
            this.smartThingsConnection = smartThingsConnection;
        }

        public void UseMqttClient(MqttConnection mqttConnection)
        {
            this.mqttConnection = mqttConnection;
        }

        public void UseConfiguration(Configuration.Configuration configuration)
        {
            this.configuration = configuration;
        }

        public void PublishNewHADevices(IDevice device)
        {
            PublishNewHVACDeviceForHA((Device)device);
            PublishNewTempSensorDeviceForHA((Device)device);
            PublishNewHumiditySensorDeviceForHA((Device)device);
            PublishNewEnergySensorDeviceForHA((Device)device);

            UpdateState(device.DeviceID);

            RunRefreshLoop(device.DeviceID, CancellationToken.None);

            mqttConnection.SubscribeToTopicChanges(GetTopic(device.DeviceID, ETopic.SetFanMode));
            mqttConnection.SubscribeToTopicChanges(GetTopic(device.DeviceID, ETopic.SetState));
            mqttConnection.SubscribeToTopicChanges(GetTopic(device.DeviceID, ETopic.SetPresetMode));
            mqttConnection.SubscribeToTopicChanges(GetTopic(device.DeviceID, ETopic.SetTargetTemperature));
        }

        public void ClearHADeviceRegistrations(IDevice device)
        {
            PublishClearDeviceForHA(ETopic.HAHumiditySensorConfig, device.DeviceID);
            PublishClearDeviceForHA(ETopic.HATempSensorConfig, device.DeviceID);
            PublishClearDeviceForHA(ETopic.HAEnergySensorConfig, device.DeviceID);
            PublishClearDeviceForHA(ETopic.HAClimateConfig, device.DeviceID);
        }

        public void DispatchMqttMessage(IDevice device, string topic, string newValue)
        {
            var deviceId = device.DeviceID;

            if (topic == GetTopic(deviceId, ETopic.SetFanMode))
                SetFanMode(deviceId, newValue);
            else if (topic == GetTopic(deviceId, ETopic.SetState))
                SetState(deviceId, newValue);
            else if (topic == GetTopic(deviceId, ETopic.SetPresetMode))
                SetOptionalMode(deviceId, newValue);
            else if (topic == GetTopic(deviceId, ETopic.SetTargetTemperature))
                SetTargetTemperature(deviceId, ((int)double.Parse(newValue)).ToString());

            Task.Run(() =>
            {
                UpdateState(deviceId);
                Thread.Sleep(500);
                UpdateState(deviceId);
                Thread.Sleep(1500);
                UpdateState(deviceId);
            });
        }

        public bool SupportsDevice(JObject stDeviceStatus)
        {
            dynamic status = stDeviceStatus;

            var statusSignatureRecognized = (status["ocf"].n.value == "Samsung-Room-Air-Conditioner") &&
                                             (status["execute"].data.value.payload["x.com.samsung.da.description"] == "ARA-WW-TP1-22-COMMON");

            return statusSignatureRecognized;
        }

        public IDevice RegisterDevice(string stDeviceId, JObject stDeviceStatus)
        {
            dynamic status = stDeviceStatus;

            var newDevice = new Device();
            newDevice.DeviceID = stDeviceId;
            newDevice.SerialNumber = status["execute"].data.value.payload["x.com.samsung.da.serialNum"];
            newDevice.TemperatureUOM = status["temperatureMeasurement"].temperature.unit;
            newDevice.MinTemperature = status["custom.thermostatSetpointControl"].minimumSetpoint.value;
            newDevice.MaxTemperature = status["custom.thermostatSetpointControl"].maximumSetpoint.value;
            newDevice.Driver = this;

            return newDevice;
        }

        public void PublishNewHVACDeviceForHA(Device device)
        {
            var deviceId = device.DeviceID;
            var topic = GetTopic(device.DeviceID, ETopic.HAClimateConfig);
            var configPayload = $@"
    {{ 
        ""name"":""Airconditioner"",
        ""unique_id"" : ""{deviceId}"",
        ""sw_version"" : ""{configuration.ThisVersion}"",
        ""mode_command_topic"" : ""{GetTopic(deviceId, ETopic.SetState)}"",
        ""mode_state_topic"" : ""{GetTopic(deviceId, ETopic.GetState)}"",
        ""action_topic"" : ""{GetTopic(deviceId, ETopic.GetAction)}"",
        ""current_humidity_topic"" : ""{GetTopic(deviceId, ETopic.GetHumidity)}"",
        ""current_temperature_topic"" : ""{GetTopic(deviceId, ETopic.GetTemperature)}"",
        ""fan_mode_command_topic"" : ""{GetTopic(deviceId, ETopic.SetFanMode)}"",
        ""fan_mode_state_topic"" : ""{GetTopic(deviceId, ETopic.GetFanMode)}"",
        ""preset_mode_state_topic"" : ""{GetTopic(deviceId, ETopic.GetPresetMode)}"",
        ""preset_mode_command_topic"" : ""{GetTopic(deviceId, ETopic.SetPresetMode)}"",
        ""swing_mode_state_topic"" : ""{GetTopic(deviceId, ETopic.GetSwingMode)}"",
        ""swing_mode_command_topic"" : ""{GetTopic(deviceId, ETopic.SetSwingMode)}"",
        ""temperature_command_topic"" : ""{GetTopic(deviceId, ETopic.SetTargetTemperature)}"",               
        ""temperature_state_topic"" : ""{GetTopic(deviceId, ETopic.GetTargetTemperature)}"",               
        ""precision "" : 1.0,
        ""temp_step"" : 1.0,
        ""max_temp"" : {device.MaxTemperature},
        ""min_temp"" : {device.MinTemperature},
        ""optimistic"" : false,
        ""temperature_unit"" : ""{device.TemperatureUOM}"",
        ""modes"" : [""auto"",""off"",""cool"",""dry"",""fan_only"",""heat""],
        ""preset_modes"" : [""sleep"", ""Quiet"", ""Smart"", ""boost"", ""Wind Free"", ""Wind Free Sleep""],
        ""fan_modes"" : [""auto"",""low"",""medium"",""high"",""Turbo""],
        ""swing_mode"" : [""on"",""off""],
        ""device"" : 
        {{
            ""model"" : ""Geo Plus"",
            ""name"" : ""Airconditioner"",
            ""manufacturer"" : ""Samsung"",
            ""suggested_area"" : ""Bedroom"",
            ""via_device"" : ""{configuration.ThisAppName}"",
            ""identifiers"" : [""{device.SerialNumber}""]
        }}
    }}";

            mqttConnection.SendMessage(topic, configPayload, true);
        }

        public string GetTopic(string deviceId, ETopic topic)
        {
            switch (topic)
            {
                case ETopic.HAClimateConfig:
                    return $"{configuration.HomeAssistant.HaDiscoveryTopicPrefix}/climate/{deviceId}/config";
                case ETopic.HATempSensorConfig:
                    return $"{configuration.HomeAssistant.HaDiscoveryTopicPrefix}/sensor/{deviceId}_temperature/config";
                case ETopic.HAHumiditySensorConfig:
                    return $"{configuration.HomeAssistant.HaDiscoveryTopicPrefix}/sensor/{deviceId}_humidity/config";
                case ETopic.HAEnergySensorConfig:
                    return $"{configuration.HomeAssistant.HaDiscoveryTopicPrefix}/sensor/{deviceId}_energy/config";
                case ETopic.SetState:
                    return $"{configuration.ThisAppName}/hvac/{deviceId}/mode/set";
                case ETopic.GetState:
                    return $"{configuration.ThisAppName}/hvac/{deviceId}/mode";
                case ETopic.GetAction:
                    return $"{configuration.ThisAppName}/hvac/{deviceId}/action";
                case ETopic.GetHumidity:
                    return $"{configuration.ThisAppName}/hvac/{deviceId}/humidity";
                case ETopic.GetTemperature:
                    return $"{configuration.ThisAppName}/hvac/{deviceId}/temperature";
                case ETopic.SetTargetTemperature:
                    return $"{configuration.ThisAppName}/hvac/{deviceId}/target_temperature/set";
                case ETopic.GetTargetTemperature:
                    return $"{configuration.ThisAppName}/hvac/{deviceId}/target_temperature";
                case ETopic.SetFanMode:
                    return $"{configuration.ThisAppName}/hvac/{deviceId}/fan/set";
                case ETopic.GetFanMode:
                    return $"{configuration.ThisAppName}/hvac/{deviceId}/fan";
                case ETopic.SetPresetMode:
                    return $"{configuration.ThisAppName}/hvac/{deviceId}/preset/set";
                case ETopic.GetPresetMode:
                    return $"{configuration.ThisAppName}/hvac/{deviceId}/preset";
                case ETopic.SetSwingMode:
                    return $"{configuration.ThisAppName}/hvac/{deviceId}/swing_mode/set";
                case ETopic.GetSwingMode:
                    return $"{configuration.ThisAppName}/hvac/{deviceId}/swing_mode";
                case ETopic.SetAutoCleaning:
                    return $"{configuration.ThisAppName}/hvac/{deviceId}/autocleaning/set";
                case ETopic.GetAutoCleaning:
                    return $"{configuration.ThisAppName}/hvac/{deviceId}/autocleaning";
                case ETopic.GetTotalEnergyUsed:
                    return $"{configuration.ThisAppName}/hvac/{deviceId}/energy_used";
                default:
                    throw new NotSupportedException($"Topic not supported: '{topic}'");
            }
        }

        void PublishClearDeviceForHA(ETopic topic, string deviceId)
        {
            var pauloadTopic = GetTopic(deviceId, topic);

            var configPayload = "{}";

            mqttConnection.SendMessage(pauloadTopic, configPayload, true);
        }

        void PublishNewSensorDeviceForHA(Device device, ETopic deviceTopic, ETopic stateTopic, string sensorClass, string deviceClass, string uom, string name)
        {
            var topic = GetTopic(device.DeviceID, deviceTopic);

            var configPayload = $@"
    {{ 
        ""name"":""{name}"",
        ""unique_id"" : ""{device.DeviceID}_{deviceClass}"",
        ""sw_version"" : ""{configuration.ThisVersion}"",
        ""device_class"" : ""{deviceClass}"",
        ""suggested_display_precision"" : 0,
        ""state_class"" : ""{sensorClass}"",
        ""unit_of_measurement"" : ""{uom}"",
        ""state_topic"" : ""{GetTopic(device.DeviceID, stateTopic)}"",
        ""device"" : 
        {{
            ""model"" : ""Geo Plus"",
            ""name"" : ""Airconditioner"",
            ""manufacturer"" : ""Samsung"",
            ""suggested_area"" : ""Bedroom"",
            ""via_device"" : ""{configuration.ThisAppName}"",
            ""identifiers"" : [""{device.SerialNumber}""]
        }}
    }}";

            mqttConnection.SendMessage(topic, configPayload, true);
        }

        public void PublishNewHumiditySensorDeviceForHA(Device device)
        {
            PublishNewSensorDeviceForHA(device, ETopic.HAHumiditySensorConfig, ETopic.GetHumidity, "measurement", "humidity", "%", "Humidity");
        }

        public void PublishNewEnergySensorDeviceForHA(Device device)
        {
            PublishNewSensorDeviceForHA(device, ETopic.HAEnergySensorConfig, ETopic.GetTotalEnergyUsed, "measurement", "energy", "Wh", "Total Energy"); 
        }

        public void PublishNewTempSensorDeviceForHA(Device device)
        {
            PublishNewSensorDeviceForHA(device, ETopic.HATempSensorConfig, ETopic.GetTemperature, "measurement", "temperature", $"°{device.TemperatureUOM}", "Temperature");
        }

        void UpdateState(string deviceId)
        {
            var response = smartThingsConnection.GetDeviceStatus(deviceId);

            var humitidy = (double)response["relativeHumidityMeasurement"].humidity.value;
            var temperature = (double)response["temperatureMeasurement"].temperature.value;
            var airConditionerMode = response["airConditionerMode"].airConditionerMode.value;
            var airConditionerOptionalMode = response["custom.airConditionerOptionalMode"].acOptionalMode.value;
            var airConditionerFanMode = response["airConditionerFanMode"].fanMode.value;
            var fanOscillationMode = response["fanOscillationMode"].fanOscillationMode.value;
            var thermostatCoolingSetpoint = response["thermostatCoolingSetpoint"].coolingSetpoint.value;
            var autoCleaningMode = response["custom.autoCleaningMode"].autoCleaningMode.value;
            var switchState = response["switch"].@switch.value;
            var energyUsed = response["powerConsumptionReport"].powerConsumption.value.persistedEnergy;

            if (switchState == "off")
            {
                airConditionerMode = "off";
                airConditionerOptionalMode = "None";
            }

            var action = "off";
            if (airConditionerMode == "cool")
                action = "cooling";
            else if (airConditionerMode == "heat")
                action = "heating";
            else if (airConditionerMode == "dry")
                action = "drying";
            else if (airConditionerMode == "wind")
                action = "fan";

            if (airConditionerMode == "wind")
                airConditionerMode = "fan_only";

            SendPayload(deviceId, ETopic.GetAction, $@"{action}");

            if (fanOscillationMode == "fixed")
                fanOscillationMode = "off";
            else
                fanOscillationMode = "on";

            if (airConditionerFanMode == "turbo")
                airConditionerFanMode = "Turbo";

            if (airConditionerOptionalMode == "windFree")
                airConditionerOptionalMode = "Wind Free";
            else if (airConditionerOptionalMode == "windFreeSleep")
                airConditionerOptionalMode = "Wind Free Sleep";
            else if (airConditionerOptionalMode == "speed")
                airConditionerOptionalMode = "boost";
            else if (airConditionerOptionalMode == "smart")
                airConditionerOptionalMode = "Smart";
            else if (airConditionerOptionalMode == "quiet")
                airConditionerOptionalMode = "Quiet";
            else if (airConditionerOptionalMode == "off")
                airConditionerOptionalMode = "none";

            SendPayload(deviceId, ETopic.GetPresetMode, $@"{airConditionerOptionalMode}");
            SendPayload(deviceId, ETopic.GetState, $@"{airConditionerMode}");
            SendPayload(deviceId, ETopic.GetHumidity, $@"{humitidy}");
            SendPayload(deviceId, ETopic.GetTemperature, $@"{temperature}");
            SendPayload(deviceId, ETopic.GetFanMode, $@"{airConditionerFanMode}");
            SendPayload(deviceId, ETopic.GetSwingMode, $@"{fanOscillationMode}");
            SendPayload(deviceId, ETopic.GetTargetTemperature, $@"{thermostatCoolingSetpoint}");
            SendPayload(deviceId, ETopic.GetAutoCleaning, $@"{autoCleaningMode}");
            SendPayload(deviceId, ETopic.GetTotalEnergyUsed, $@"{energyUsed}");
        }

        private void SendPayload(string deviceId, ETopic topic, string payload)
        {
            var stateTopic = GetTopic(deviceId, topic);

            mqttConnection.SendMessage(stateTopic, payload);
        }

        void RunRefreshLoop(string deviceId, CancellationToken cancellationToken)
        {
            Task.Run(() =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        mqttConnection.EnsureConnected();   // Ecosystem just not stable enough, make sure mqtt connection is up before doing the refresh.

                        TriggerUpdateSensors(deviceId);
                        Task.Delay(configuration.Intervals.UpdateDelay, cancellationToken).Wait();
                        UpdateState(deviceId);
                        Task.Delay(configuration.Intervals.UpdateInterval, cancellationToken).Wait();
                    }
                    catch (Exception ex)
                    {
                        Thread.Sleep(100);
                        Log.Error(ex, "Unexpected error while refreshing device state.");
                    }
                }
            });
        }

        void TriggerUpdateSensors(string deviceId)
        {
            var commands = $@"
        {{
          ""commands"": [
            {{
              ""component"": ""main"",
              ""capability"": ""custom.periodicSensing"",
              ""command"": ""triggerManualSensing"",
              ""arguments"": []
            }},
            {{
              ""component"": ""main"",
              ""capability"": ""refresh"",
              ""command"": ""refresh"",
              ""arguments"": []
            }}
          ]
        }}";

            smartThingsConnection.SendCommands(deviceId, commands);
        }

        void SetState(string deviceId, string newValue)
        {
            var acMode = newValue;
            if (acMode == "fan_only")
            {
                acMode = "wind";
            }

            var commands = string.Empty;
            if (acMode == "off")
            {
                commands = $@"
            {{
              ""commands"": [
                {{
                  ""component"": ""main"",
                  ""capability"": ""switch"",
                  ""command"": ""off"",
                  ""arguments"": []
                }},
                {{
                  ""component"": ""main"",
                  ""capability"": ""refresh"",
                  ""command"": ""refresh"",
                  ""arguments"": []
                }}
              ]
            }}";
            }
            else
            {
                commands = $@"
            {{
              ""commands"": [
                {{
                  ""component"": ""main"",
                  ""capability"": ""switch"",
                  ""command"": ""on"",
                  ""arguments"": []
                }},
                {{
                  ""component"": ""main"",
                  ""capability"": ""airConditionerMode"",
                  ""command"": ""setAirConditionerMode"",
                  ""arguments"": [
                    ""{acMode}""
                  ]
                }},
                {{
                  ""component"": ""main"",
                  ""capability"": ""refresh"",
                  ""command"": ""refresh"",
                  ""arguments"": []
                }}
              ]
            }}    
            ";
            }

            smartThingsConnection.SendCommands(deviceId, commands);
        }

        void SetTargetTemperature(string deviceId, string newValue)
        {
            var commands = string.Empty;
            commands = $@"
        {{
          ""commands"": [
            {{
              ""component"": ""main"",
              ""capability"": ""thermostatCoolingSetpoint"",
              ""command"": ""setCoolingSetpoint"",
              ""arguments"": [
                {newValue.ToLower()}
              ]
            }},
            {{
              ""component"": ""main"",
              ""capability"": ""refresh"",
              ""command"": ""refresh"",
              ""arguments"": []
            }}
          ]
        }}    
        ";

            smartThingsConnection.SendCommands(deviceId, commands);
        }

        void SetFanMode(string deviceId, string newValue)
        {
            var commands = string.Empty;
            commands = $@"
        {{
          ""commands"": [
            {{
              ""component"": ""main"",
              ""capability"": ""airConditionerFanMode"",
              ""command"": ""setFanMode"",
              ""arguments"": [
                ""{newValue.ToLower()}""
              ]
            }},
            {{
              ""component"": ""main"",
              ""capability"": ""refresh"",
              ""command"": ""refresh"",
              ""arguments"": []
            }}
          ]
        }}    
        ";

            smartThingsConnection.SendCommands(deviceId, commands);
        }

        void SetOptionalMode(string deviceId, string newValue)
        {
            if (newValue == "boost")
                newValue = "speed";
            else if (newValue == "none")
                newValue = "off";

            var commands = string.Empty;
            commands = $@"
        {{
          ""commands"": [
            {{
              ""component"": ""main"",
              ""capability"": ""custom.airConditionerOptionalMode"",
              ""command"": ""setAcOptionalMode"",
              ""arguments"": [
                ""{newValue.ToCamelCase()}""
              ]
            }},
            {{
              ""component"": ""main"",
              ""capability"": ""refresh"",
              ""command"": ""refresh"",
              ""arguments"": []
            }}
          ]
        }}    
        ";

            smartThingsConnection.SendCommands(deviceId, commands);
        }
    }
}
