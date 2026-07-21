namespace Philips.IBE.Service.WebAgent.Server.Models
{
    public class S3Configuration
    {
        public string ServiceId { get; set; } = string.Empty;
        public string TenantName { get; set; } = string.Empty;
        public string CollectorId { get; set; } = string.Empty;
        public string InstitutionName { get; set; } = string.Empty;
        public string GatewayUrl { get; set; } = string.Empty;
        public string IamHost { get; set; } = string.Empty;
        public string TimeZone { get; set; } = string.Empty;
        public string PrivateKeyPath { get; set; } = string.Empty;
        public string PrivateKeyPassword { get; set; } = string.Empty;

    }
}
