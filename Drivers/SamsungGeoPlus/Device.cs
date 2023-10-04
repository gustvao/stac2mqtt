using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace stac2mqtt.Drivers.SamsungGeoPlus
{
    public class Device : IDevice
    {
        public string DeviceID { get; set; }
        public string SerialNumber { get; set; }
        public IDriver Driver { get; set; }
        public string TemperatureUOM { get; set; }
        public double MinTemperature { get; set; }
        public double MaxTemperature { get; set; }

    }
}
