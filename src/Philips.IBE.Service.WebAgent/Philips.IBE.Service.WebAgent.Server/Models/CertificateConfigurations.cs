namespace Philips.IBE.Service.WebAgent.Server.Models
{
    public class CertificateConfigurations
    {
        public string? RootCertificatePath { get; set; }
        public string? ClientCertificatePath { get; set; }
        public string? ClientCertificatePassword { get; set; }
        public string? ServerCertificatePath { get; set; }
        public string? ServerCertificatePassword { get; set; }
    }
}
