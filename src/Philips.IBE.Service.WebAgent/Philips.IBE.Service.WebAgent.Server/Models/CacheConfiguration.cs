namespace Philips.IBE.Service.WebAgent.Server.Models
{
    public class CacheConfiguration
    {
        public required string CacheReconciliationEndPoint { get; set; }
        public required string CacheRelaodEndPoint { get; set; }
        public required string CacheCertificatePath { get; set; }
        public required string CacheCertificatePassword { get; set; }
    }
}
