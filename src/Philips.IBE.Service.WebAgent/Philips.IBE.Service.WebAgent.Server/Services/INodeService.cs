using Philips.IBE.Service.WebAgent.Server.Models;

namespace Philips.IBE.Service.WebAgent.Server.Services
{
    public interface INodeService
    {
        public ServiceNode GetServiceNode();
        public bool UpdateHTTPServiceNode(ServiceNodeConfiguration serviceNode);
        public bool UpdateTCPServiceNode(ServiceNodeConfiguration serviceNode);
        public bool UpdateWebSocketClientServiceNode(ServiceNodeConfiguration serviceNode);
        public bool UpdateADTServiceNode(ServiceNodeConfiguration serviceNode);
    }
}
