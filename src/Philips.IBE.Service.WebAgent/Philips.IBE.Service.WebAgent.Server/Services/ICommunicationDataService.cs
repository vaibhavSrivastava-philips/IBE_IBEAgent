
using Philips.IBE.Service.WebAgent.Server.Models;

namespace Philips.IBE.Service.WebAgent.Server.Services
{
    public interface ICommunicationDataService
    {
        void AddCommunicationData(CommunicationPoint data);
        CommunicationPoint? GetCommunicationDataById(int id);
        void UpdateCommunicationData(int id, CommunicationPoint updatedData);
        void DeleteCommunicationData(int id);
        List<CommunicationPoint> GetAllCommunicationData();
    }
}
