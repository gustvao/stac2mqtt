using Newtonsoft.Json.Linq;
using System;
using System.Threading;
using System.Threading.Tasks;
using stac2mqtt.Services.Consumed.Mqtt;
using Serilog;
using CaseExtensions;
using stac2mqtt.Services.Consumed.SmartThings;
using System.Linq;
using System.Collections.Concurrent;

namespace stac2mqtt.Drivers.SamsungGeoPlus
{
    public class Driver : IDriver
    {
        private MqttConnection mqttConnection;
        private SmartThingsConnection smartThingsConnection;
        private Configuration.Configuration configuration;
        
        // Semaphore per device to prevent concurrent commands
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> DeviceSemaphores = new ConcurrentDictionary<string, SemaphoreSlim>();

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
            PublishNewLEDDisplaySwitchDeviceForHA((Device)device);

            // Wait before doing any device operations to let the device settle
            Task.Delay(configuration.Intervals.DeviceSetupDelay).Wait();
            
            UpdateState(device.DeviceID);

            // Wait longer before starting the refresh loop
            Task.Delay(configuration.Intervals.DeviceRefreshStartDelay).Wait();
            
            RunRefreshLoop(device.DeviceID, CancellationToken.None);

            mqttConnection.SubscribeToTopicChanges(GetTopic(device.DeviceID, ETopic.SetFanMode));
            mqttConnection.SubscribeToTopicChanges(GetTopic(device.DeviceID, ETopic.SetState));
            mqttConnection.SubscribeToTopicChanges(GetTopic(device.DeviceID, ETopic.SetPresetMode));
            mqttConnection.SubscribeToTopicChanges(GetTopic(device.DeviceID, ETopic.SetTargetTemperature));
            mqttConnection.SubscribeToTopicChanges(GetTopic(device.DeviceID, ETopic.SetLEDDisplayMode));
        }

        public void ClearHADeviceRegistrations(IDevice device)
        {
            PublishClearDeviceForHA(ETopic.HAHumiditySensorConfig, device.DeviceID);
            PublishClearDeviceForHA(ETopic.HATempSensorConfig, device.DeviceID);
            PublishClearDeviceForHA(ETopic.HAEnergySensorConfig, device.DeviceID);
            PublishClearDeviceForHA(ETopic.HAClimateConfig, device.DeviceID);
            PublishClearDeviceForHA(ETopic.HALEDDisplaySwitchConfig, device.DeviceID);
        }

        public void DispatchMqttMessage(IDevice device, string topic, string newValue)
        {
            var deviceId = device.DeviceID;

            if (topic == GetTopic(deviceId, ETopic.SetFanMode))
                SetFanMode(deviceId, newValue);
            else if (topic == GetTopic(deviceId, ETopic.SetState))
                SetState(deviceId, newValue);
            else if (topic == GetTopic(deviceId, ETopic.SetLEDDisplayMode))
                SetLEDDisplay(deviceId, newValue);
            else if (topic == GetTopic(deviceId, ETopic.SetPresetMode))
                SetOptionalMode(deviceId, newValue);
            else if (topic == GetTopic(deviceId, ETopic.SetTargetTemperature))
                SetTargetTemperature(deviceId, ((int)double.Parse(newValue)).ToString());

            // Update state with longer delays to allow device to process commands
            Task.Run(async () =>
            {
                try
                {
                    // Wait for device to process the command
                    await Task.Delay(2000);
                    UpdateState(deviceId);
                    
                    // Wait longer before second update
                    await Task.Delay(3000);
                    UpdateState(deviceId);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error updating state after command dispatch for device {DeviceId}: {Message}", deviceId, ex.Message);
                }
            });
        }

        public bool SupportsDevice(JObject stDeviceStatus)
        {
            dynamic status = stDeviceStatus;

            var statusSignatureRecognized = (status["ocf"].n.value == "Samsung-Room-Air-Conditioner") &&
                                             (((string)(status["ocf"].mnfv.value)).Contains("ARA-WW-TP1-22-COMMON"));

            return statusSignatureRecognized;
        }

        public IDevice RegisterDevice(string stDeviceId, JObject stDeviceStatus)
        {
            // Remove dynamic magic and use SelectToken.
            // This will attempt to find execute.data.value.payload safely.
            var payloadToken = stDeviceStatus.SelectToken("execute.data.value.payload");

            string serialNumber = string.Empty;
            if (payloadToken != null)
            {
                if (payloadToken.Type == JTokenType.Object)
                {
                    // If payload is a JObject, get the property "x.com.samsung.da.serialNum"
                    serialNumber = payloadToken.Value<string>("x.com.samsung.da.serialNum") ?? string.Empty;
                }
                else
                {
                    // Otherwise, if it's a simple value, convert it to string.
                    serialNumber = payloadToken.Value<string>() ?? string.Empty;
                }
            }

            var newDevice = new Device();
            newDevice.DeviceID = stDeviceId;

            if (!string.IsNullOrWhiteSpace(serialNumber))
            {
                newDevice.SerialNumber = serialNumber;
                newDevice.TemperatureUOM = stDeviceStatus.SelectToken("temperatureMeasurement.temperature.unit")?.Value<string>();
                newDevice.MinTemperature = stDeviceStatus.SelectToken("custom.thermostatSetpointControl.minimumSetpoint.value")?.Value<double>() ?? 16;
                newDevice.MaxTemperature = stDeviceStatus.SelectToken("custom.thermostatSetpointControl.maximumSetpoint.value")?.Value<double>() ?? 30;
            }
            else
            {
                // Fallback: build serial number from other properties
                string mnmn = stDeviceStatus.SelectToken("ocf.mnmn.value")?.Value<string>();
                string di = stDeviceStatus.SelectToken("ocf.di.value")?.Value<string>();
                newDevice.SerialNumber = $"{mnmn}-{di}";
                newDevice.TemperatureUOM = stDeviceStatus.SelectToken("temperatureMeasurement.temperature.unit")?.Value<string>() ?? "C";
                newDevice.MinTemperature = stDeviceStatus.SelectToken("custom.thermostatSetpointControl.minimumSetpoint.value")?.Value<double>() ?? 16;
                newDevice.MaxTemperature = stDeviceStatus.SelectToken("custom.thermostatSetpointControl.maximumSetpoint.value")?.Value<double>() ?? 30;
            }

            newDevice.Driver = this;
            return newDevice;
        }

        public void PublishNewHVACDeviceForHA(Device device)
        {
            var deviceId = device.DeviceID;
            var deviceConfig = configuration.Devices.FirstOrDefault(d => d.DeviceId == deviceId);
            var deviceName = deviceConfig?.Name ?? "Airconditioner";
            var deviceArea = deviceConfig?.Area ?? "Bedroom";
            
            var topic = GetTopic(device.DeviceID, ETopic.HAClimateConfig);
            var configPayload = $@"
    {{ 
        ""name"":""{deviceName}"",
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
            ""name"" : ""{deviceName}"",
            ""manufacturer"" : ""Samsung"",
            ""suggested_area"" : ""{deviceArea}"",
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
                case ETopic.HALEDDisplaySwitchConfig:
                    return $"{configuration.HomeAssistant.HaDiscoveryTopicPrefix}/switch/{deviceId}_led_display/config";
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
                case ETopic.SetLEDDisplayMode:
                    return $"{configuration.ThisAppName}/hvac/{deviceId}/led_display/set";
                case ETopic.GetLEDDisplayMode:
                    return $"{configuration.ThisAppName}/hvac/{deviceId}/led_display";
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
            var deviceId = device.DeviceID;
            var deviceConfig = configuration.Devices.FirstOrDefault(d => d.DeviceId == deviceId);
            var deviceName = deviceConfig?.Name ?? "Airconditioner";
            var deviceArea = deviceConfig?.Area ?? "Bedroom";
            
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
            ""name"" : ""{deviceName}"",
            ""manufacturer"" : ""Samsung"",
            ""suggested_area"" : ""{deviceArea}"",
            ""via_device"" : ""{configuration.ThisAppName}"",
            ""identifiers"" : [""{device.SerialNumber}""]
        }}
    }}";

            mqttConnection.SendMessage(topic, configPayload, true);
        }

        void PublishNewSwitchDeviceForHA(Device device, ETopic deviceTopic, ETopic getStateTopic, ETopic setTopic, string name, string idName)
        {
            var deviceId = device.DeviceID;
            var deviceConfig = configuration.Devices.FirstOrDefault(d => d.DeviceId == deviceId);
            var deviceName = deviceConfig?.Name ?? "Airconditioner";
            var deviceArea = deviceConfig?.Area ?? "Bedroom";

            var topic = GetTopic(device.DeviceID, deviceTopic);

            var configPayload = $@"
    {{ 
        ""name"":""{name}"",
        ""unique_id"" : ""{device.DeviceID}_{idName}"",
        ""sw_version"" : ""{configuration.ThisVersion}"",
        ""device_class"" : ""switch"",
        ""state_topic"" : ""{GetTopic(device.DeviceID, getStateTopic)}"",
        ""command_topic"" : ""{GetTopic(device.DeviceID, setTopic)}"",
        ""payload_on"" : ""on"",
        ""payload_off"" : ""off"",
        ""state_on"" : ""on"",
        ""state_off"" : ""off"",
        ""optimistic"" : ""true"",
        ""device"" : 
        {{
            ""model"" : ""Geo Plus"",
            ""name"" : ""{deviceName}"",
            ""manufacturer"" : ""Samsung"",
            ""suggested_area"" : ""{deviceArea}"",
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
        
        public void PublishNewLEDDisplaySwitchDeviceForHA(Device device)
        {
            PublishNewSwitchDeviceForHA(device, ETopic.HALEDDisplaySwitchConfig, ETopic.GetLEDDisplayMode, ETopic.SetLEDDisplayMode, "LED Display", "ledDisplaySwitch");
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
            var ledDisplayStatus = response["samsungce.airConditionerLighting"].lighting.value; 

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
            SendPayload(deviceId, ETopic.GetLEDDisplayMode, $@"{ledDisplayStatus}");
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
                // Wait before starting the loop to let device settle
                Task.Delay(configuration.Intervals.RefreshLoopStartDelay, cancellationToken).Wait();
                
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        mqttConnection.EnsureConnected();   // Ecosystem just not stable enough, make sure mqtt connection is up before doing the refresh.

                        // Only update state (read-only operation) - no commands initially
                        UpdateState(deviceId);
                        
                        // Wait for a much longer interval between updates (10 minutes instead of 3)
                        Task.Delay(configuration.Intervals.LongUpdateInterval, cancellationToken).Wait();
                    }
                    catch (Exception ex)
                    {
                        Thread.Sleep(configuration.Intervals.ErrorRetryDelay); // Configurable wait on error
                        Log.Error(ex, "Unexpected error while refreshing device state.");
                    }
                }
            });
        }

        void TriggerUpdateSensors(string deviceId)
        {
            var semaphore = GetDeviceSemaphore(deviceId);
            
            Task.Run(async () =>
            {
                try
                {
                    // Wait for exclusive access to this device
                    await semaphore.WaitAsync();
                    
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

                    var result = smartThingsConnection.SendCommands(deviceId, commands);
                    if (result == null)
                    {
                        Log.Warning("Failed to trigger sensor update for device {DeviceId}", deviceId);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error triggering sensor update for device {DeviceId}: {Message}", deviceId, ex.Message);
                }
                finally
                {
                    semaphore.Release();
                }
            });
        }

        void SetState(string deviceId, string newValue)
        {
            var semaphore = GetDeviceSemaphore(deviceId);
            
            Task.Run(async () =>
            {
                try
                {
                    // Wait for exclusive access to this device
                    await semaphore.WaitAsync();
                    
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

                    var result = smartThingsConnection.SendCommands(deviceId, commands);
                    if (result == null)
                    {
                        Log.Warning("Failed to set state to '{State}' for device {DeviceId}", newValue, deviceId);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error setting state to '{State}' for device {DeviceId}: {Message}", newValue, deviceId, ex.Message);
                }
                finally
                {
                    semaphore.Release();
                }
            });
        }

        void SetTargetTemperature(string deviceId, string newValue)
        {
            try
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

                var result = smartThingsConnection.SendCommands(deviceId, commands);
                if (result == null)
                {
                    Log.Warning("Failed to set target temperature to '{Temperature}' for device {DeviceId}", newValue, deviceId);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error setting target temperature to '{Temperature}' for device {DeviceId}: {Message}", newValue, deviceId, ex.Message);
            }
        }

        void SetFanMode(string deviceId, string newValue)
        {
            try
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

                var result = smartThingsConnection.SendCommands(deviceId, commands);
                if (result == null)
                {
                    Log.Warning("Failed to set fan mode to '{FanMode}' for device {DeviceId}", newValue, deviceId);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error setting fan mode to '{FanMode}' for device {DeviceId}: {Message}", newValue, deviceId, ex.Message);
            }
        }

        void SetLEDDisplay(string deviceId, string newValue)
        {
            try
            {
                var commands = string.Empty;
                commands = $@"
        {{
          ""commands"": [
            {{
              ""component"": ""main"",
              ""capability"": ""samsungce.airConditionerLighting"",
              ""command"": ""{newValue.ToLower()}""              
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

                var result = smartThingsConnection.SendCommands(deviceId, commands);
                if (result == null)
                {
                    Log.Warning("Failed to set LED display to '{LEDMode}' for device {DeviceId}", newValue, deviceId);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error setting LED display to '{LEDMode}' for device {DeviceId}: {Message}", newValue, deviceId, ex.Message);
            }
        }

        void SetOptionalMode(string deviceId, string newValue)
        {
            try
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

                var result = smartThingsConnection.SendCommands(deviceId, commands);
                if (result == null)
                {
                    Log.Warning("Failed to set optional mode to '{OptionalMode}' for device {DeviceId}", newValue, deviceId);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error setting optional mode to '{OptionalMode}' for device {DeviceId}: {Message}", newValue, deviceId, ex.Message);
            }
        }

        private SemaphoreSlim GetDeviceSemaphore(string deviceId)
        {
            return DeviceSemaphores.GetOrAdd(deviceId, _ => new SemaphoreSlim(1, 1));
        }
    }
}
