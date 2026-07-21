using Philips.IBE.Service.WebAgent.Server.Configuration;

namespace Philips.IBE.Service.WebAgent.Server.Models
{
    public class CommunicationPoint
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Mode { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public bool IsSSLEnabled { get; set; }
    
        public TcpServerConfiguration? TcpConfiguration { get; set; }
        public HttpConnectionConfiguration? HttpConfiguration { get; set; }
        public HttpConnectionConfiguration? WebSocketConfiguration { get; set; }

        public CertificateConfigurations? CertificateDetails { get; set; }
        public ProxyConfigurations? ProxyConfigurations { get; set; }
        public RetryConfigurations? ConnectionRetry { get; set; }
        public RetryConfigurations? MessageRetry { get; set; }
        public CacheConfiguration? CacheConfiguration { get; set; }
        public S3Configuration? S3Configuration { get; set; }
    }
}
