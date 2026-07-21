namespace Philips.IBE.Service.WebAgent.Server.Services
{
    public interface IHeartBeatService
    {
        Task<bool> IsPortOpenAsync(string host, int port);
        public List<string> GetTcpPorts();
    }
}
