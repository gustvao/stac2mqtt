namespace stac2mqtt.Configuration
{
    public class SmartThingsConfiguration
    {
        public string APIBaseURL { get; set; } = "https://api.smartthings.com/v1/devices";
        public string ApiToken { get; set; }
        public string RefreshToken { get; set; }
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string Code { get; set; }
    }
}
