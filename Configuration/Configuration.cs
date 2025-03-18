using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace stac2mqtt.Configuration
{
    public class Configuration
    {
        public string MqttServer { get; set; }
        public SmartThingsConfiguration SmartThings { get; set; } = new SmartThingsConfiguration();
        public HomeAssistantConfiguration HomeAssistant { get; set; } = new HomeAssistantConfiguration();
        public IntervalConfiguration Intervals { get; set; } = new IntervalConfiguration();

        public List<string> DeviceIds { get; set; } = new List<string>();

        public string ThisAppName { get; set; } = "stac2mqtt";
        public string ThisVersion { get; set; } = "1.0.0";

        public MqttConfiguration Mqtt { get; set; } = new MqttConfiguration();
        public DateTime TokenLastUpdated { get; set; } = DateTime.UtcNow;
    }
}