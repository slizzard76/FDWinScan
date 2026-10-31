namespace FDWinScan.Configuration
{
    public class BackendSettings
    {
        public string BaseUrl { get; set; } = "https://localhost:7142/";
        public int TimeoutSeconds { get; set; } = 300;
    }
}
