using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace stac2mqtt.Drivers
{
    public interface IDevice
    {
        string DeviceID { get; set; }
        string SerialNumber { get; set; }
        IDriver Driver { get; set; }
    }
}
