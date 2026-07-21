namespace Philips.IBE.Service.WebAgent.Server.Models
{
    public class ProxyConfigurations
    {
        public bool IsEnabled { get; set; }
        public string? ProxyAddress { get; set; }
        public string? ProxyPort { get; set; }
        public string? ProxyUsername { get; set; }
        public string? ProxyPassword { get; set; }
    }
}
