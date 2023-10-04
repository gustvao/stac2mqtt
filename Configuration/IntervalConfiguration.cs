public class IntervalConfiguration
{
    public int UpdateDelay { get; set; } = 1000 * 5;            // ms to wait before checking device status after refresh command was issued
    public int UpdateInterval { get; set; } = 1000 * 60 * 10;   // ms between requests for device status updates from API
}