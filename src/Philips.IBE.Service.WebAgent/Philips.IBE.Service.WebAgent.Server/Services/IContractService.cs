using Philips.IBE.Service.WebAgent.Server.Models;

namespace Philips.IBE.Service.WebAgent.Server.Services
{
    public interface IContractService
    {
        List<Contract> GetAllContracts();
        //Contract? GetContractById(int id);
        void AddContract(Contract contract);
        void UpdateContract(string oldName,Contract updatedContract);
        void DeleteContract(string name);
    }
}