using Philips.IBE.Service.WebAgent.Server.Models;

namespace Philips.IBE.Service.WebAgent.Server.DBUtilities
{
    public interface IDBUtils
    {
        public List<ErrorQueue> FetchErrorQueue();
        public bool UpdateStatus(string status, string messageId);
    }
}
