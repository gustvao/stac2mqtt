using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace stac2mqtt.Configuration
{
    public class DeviceConfiguration
    {
        public string DeviceId { get; set; }
        public string Name { get; set; } = "Air Conditioner"; // Default value
        public string Area { get; set; } = "Bedroom";         // Default value
    }
} 