public class IntervalConfiguration
{
    public int UpdateDelay { get; set; } = 1000 * 5;            // ms to wait before checking device status after refresh command was issued
    public int UpdateInterval { get; set; } = 1000 * 60 * 10;   // ms between requests for device status updates from API
    public int CommandRetryDelay { get; set; } = 1000 * 2;      // ms to wait between command retries for 409 conflicts
    
    // Device initialization delays
    public int DeviceSetupDelay { get; set; } = 1000 * 10;      // ms to wait before first device operations
    public int DeviceRefreshStartDelay { get; set; } = 1000 * 5; // ms additional delay before starting refresh loop
    public int RefreshLoopStartDelay { get; set; } = 1000 * 30;  // ms to wait before starting the refresh loop
    public int LongUpdateInterval { get; set; } = 1000 * 60 * 10; // ms between updates in refresh loop (10 minutes)
    public int ErrorRetryDelay { get; set; } = 1000 * 10;       // ms to wait after errors before retrying
}