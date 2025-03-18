using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using MQTTnet.Client;
using MQTTnet.Server;
using MQTTnet;
using Newtonsoft.Json;
using stac2mqtt.Drivers;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Serilog;
using Microsoft.Extensions.DependencyInjection;
using stac2mqtt.Services.Consumed.Mqtt;
using stac2mqtt.Configuration;
using stac2mqtt.Services.Consumed.SmartThings;

namespace stac2mqtt
{
    class Program
    {
        static void Main(string[] args)
        {
            var builder = Host.CreateApplicationBuilder(args);

            SetupLogging(builder);
            GetSettings(args, builder);
            SetupDI(builder);

            var host = builder.Build();

            host.Services.GetService<MqttConnection>().Setup();
            host.Services.GetService<DriverManager>().Setup();
            host.Services.GetService<DeviceManager>().Setup();

            // Loop, allowing event handlers to process messages received via mqtt and regular timers
            host.Run();
        }

        private static void SetupDI(HostApplicationBuilder builder)
        {
            builder.Services.AddSingleton<MqttConnection>();
            builder.Services.AddSingleton<stac2mqtt.Configuration.ConfigurationManager>();
            builder.Services.AddSingleton<SmartThingsConnection>();
            builder.Services.AddSingleton<DriverManager>();
            builder.Services.AddSingleton<DeviceManager>();
        }

        private static void SetupLogging(HostApplicationBuilder builder)
        {
            var logger = new LoggerConfiguration()
                                .WriteTo.Console()
                                .CreateLogger();

            Log.Logger = logger;

            builder.Services.AddSingleton(logger);
        }

        private static void GetSettings(string[] args, HostApplicationBuilder builder)
        {
            // Load settings.json first (lowest priority)
            builder.Configuration.AddJsonFile("settings.json", optional: true);
            
            // Then environment variables (will override settings.json)
            builder.Configuration.AddEnvironmentVariables();
            
            // Command line arguments (highest priority)
            builder.Configuration.AddCommandLine(args);
            
            // Bind the configuration to our Configuration class
            var configuration = new Configuration.Configuration();
            builder.Configuration.Bind(configuration);
            
            // Register Configuration and ConfigurationManager as singletons
            builder.Services.AddSingleton(configuration);
            
            // ConfigurationManager is already registered in SetupDI
            
            Log.Information("Configuration loaded from settings.json. MQTT Server: {Server}, Device Count: {Count}", 
                configuration.MqttServer, 
                configuration.Devices?.Count ?? 0);
        }
    }
}
