using Philips.IBE.Service.WebAgent.Server.Constants;

namespace Philips.IBE.Service.WebAgent.Server.Models
{
    public class ResponseModel
    {
        public Status Status { get; set; }
        public string DisplayMessage { get; set; } = string.Empty;
        public object? Value { get; set; } 
    } 
}
