using Newtonsoft.Json;

namespace Philips.IBE.Service.WebAgent.Server.Models
{
    public class ServiceNode
    {
        public ServiceNodeConfiguration? TCP { get; set; }
        public ServiceNodeConfiguration? HTTP { get; set; }
        [JsonProperty("WebSocket")]
        public ServiceNodeConfiguration? WebSocketClient { get; set; }
        public ServiceNodeConfiguration? ADT { get; set; }
    }
}