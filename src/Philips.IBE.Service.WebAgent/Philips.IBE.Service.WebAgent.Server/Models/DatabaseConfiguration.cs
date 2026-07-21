namespace Philips.IBE.Service.WebAgent.Server.Models
{
    public class PostgresConfiguration
    {
        public string Host { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Database { get; set; } = string.Empty;
        public string SslMode { get; set; } = string.Empty;
        public bool TrustServerCertificate { get; set; }
    }

    public class DatabaseConfiguration
    {
        public string DataBaseType { get; set; } = string.Empty;

        public PostgresConfiguration Postgres { get; set; } = new PostgresConfiguration();
    }
}
