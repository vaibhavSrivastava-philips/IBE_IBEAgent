namespace Philips.IBE.Service.WebAgent.Server.Configuration
{
    public class AuthenticationConfiguration
    {

        public required string AuthenticationMode { get; set; }
        public required string AdminUserGroup { get; set; }
        public required string NormalUserGroup { get; set; }
    }
}
