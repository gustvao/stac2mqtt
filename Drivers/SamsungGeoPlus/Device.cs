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
