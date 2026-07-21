namespace Philips.IBE.Service.WebAgent.Server.Models
{
    public class ServiceNodeConfiguration
    {
        public string? EndPoint { get; init; }
        public string? IPAddress { get; init; }
        public int? Port { get; init; }
        public bool EnableSSL { get; init; }
        public bool IsEnabled { get; init; }
        public string? ContextPath { get; init; }
        public CertificateConfigurations? SSLConfiguration { get; init; }
        public ProxyConfigurations? ProxyConfigurations { get; init; }
        public RetryConfigurations? ConnectionRetry { get; init; }
    }
}