using Philips.IBE.Service.WebAgent.Server.Models;

namespace Philips.IBE.Service.WebAgent.Server.Services
{
    public interface IAuthenticationService
    {
        public ResponseModel LoginUser(string username, string password);
    }
}
