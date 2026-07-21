namespace Philips.IBE.Service.WebAgent.Server.Configuration
{
    public class CommonConfiguration
    {
        public required string FolderPath { get; init; }
        public required string CertificateFolderName { get; init; }
        public bool DatabaseEnabled { get; init; } = false;
        public string DatabaseFileName { get;init; } = string.Empty;
        public string ServiceConfigPath { get; init; } = string.Empty;
        public string License { get; init; } = string.Empty;
    }
}