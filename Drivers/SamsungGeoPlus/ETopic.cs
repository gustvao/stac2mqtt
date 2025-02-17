using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace stac2mqtt.Drivers.SamsungGeoPlus
{
    public enum ETopic
    {
        HAClimateConfig,
        HATempSensorConfig,
        HAHumiditySensorConfig,
        HAEnergySensorConfig,
        HALEDDisplaySwitchConfig,
        SetState,
        GetState,
        GetHumidity,
        GetAction,
        GetTemperature,
        GetTargetTemperature,
        SetTargetTemperature,
        SetFanMode,
        GetFanMode,
        SetPresetMode,
        GetPresetMode,
        SetSwingMode,
        GetSwingMode,
        GetCurrentAction,
        SetAutoCleaning,
        GetAutoCleaning,
        GetTotalEnergyUsed,
        SetLEDDisplayMode,
        GetLEDDisplayMode
    }
}
