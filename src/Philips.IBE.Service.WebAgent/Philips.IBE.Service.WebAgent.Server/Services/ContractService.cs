using System.Text.Json;
using Philips.IBE.Service.WebAgent.Server.Configuration;
using Philips.IBE.Service.WebAgent.Server.Models;
using Philips.IBE.Service.WebAgent.Server.Exceptions;
using Philips.IBE.Service.WebAgent.Server.Controllers;

namespace Philips.IBE.Service.WebAgent.Server.Services
{
    public class ContractService : IContractService
    {
        private readonly string _filePath;
        private List<Contract> _contracts;
        private readonly ILogger<ContractService> _logger;

        public ContractService(AppConfiguration configuration, ILogger<ContractService> logger)
        {
            _logger = logger;
            if(configuration == null || configuration.CommonConfiguration == null || string.IsNullOrEmpty(configuration.CommonConfiguration.FolderPath)){
                throw new ArgumentNullException("Configuration or CommonConfiguration cannot be null or empty.");
            }
            _filePath = Path.Combine(configuration.CommonConfiguration.FolderPath, "contractData.json");
            if (!File.Exists(_filePath))
            {
                File.Create(_filePath).Close();
                _logger.LogInformation("Created new contract data file at {FilePath}", _filePath);
            }
            _contracts = LoadContractsFromFile();
        }

        private List<Contract> LoadContractsFromFile()
        {
            try
            {
                _logger.LogInformation("Loading contracts from file.");
                if (File.Exists(_filePath))
                {
                    string jsonString = File.ReadAllText(_filePath);
                    if (!string.IsNullOrEmpty(jsonString))
                    {
                        var data = JsonSerializer.Deserialize<ContractModel>(jsonString) ?? new ContractModel();
                        return data.Contracts;
                    }
                }
                return new List<Contract>();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException || ex is JsonException)
            {
                _logger.LogError($"Error loading contracts: {ex.Message}");
                return new List<Contract>();
            }
        }

        private ContractModel ConvertToContractModel(List<Contract> contracts)
        {
            return new ContractModel
            {
                Contracts = contracts
            };
        }

        private List<Contract> ConvertToContract(ContractModel contractModel)
        {
            return contractModel.Contracts;
        }

        private void SaveContractsToFile()
        {
            try
            {
                _logger.LogInformation("Saving contracts to file.");
                var data = ConvertToContractModel(_contracts);
                string jsonString = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, jsonString);
                _logger.LogInformation("Contracts saved to file.");
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
            {
                _logger.LogError($"Error saving contracts: {ex.Message}");
            }
        }

        public List<Contract> GetAllContracts()
        {
            _logger.LogInformation("Get All Contracts request initiated.");
            return _contracts;
        }

        //public Contract? GetContractById(int id)
        //{
        //    _logger.LogInformation("Get Contract by ID request initiated.");
        //    return _contracts.FirstOrDefault(c => c.Id == id);
        //}

        public void AddContract(Contract contract)
        {
            if (contract == null)
                throw new ArgumentNullException(nameof(contract));
            if (string.IsNullOrWhiteSpace(contract.Name))
                throw new ArgumentException("Contract name cannot be null or empty.", nameof(contract.Name));
            if (_contracts.Any(c => c.Name == contract.Name))
                throw new InvalidOperationException("A contract with the same name already exists.");

            _logger.LogInformation("Add Contract request initiated.");
            //contract.Id = _contracts.Count > 0 ? _contracts.Max(c => c.Id) + 1 : 1;
            _contracts.Add(contract);
            _logger.LogInformation("Contract added successfully.");
            SaveContractsToFile();
        }

        public void UpdateContract(string oldName, Contract updatedContract)
        {
            if (updatedContract == null)
                throw new ArgumentNullException(nameof(updatedContract));
            if (string.IsNullOrWhiteSpace(updatedContract.Name))
                throw new ArgumentException("Contract name cannot be null or empty.", nameof(updatedContract.Name));

            _logger.LogInformation("Update Contract request initiated.");
            var index = _contracts.FindIndex(c => c.Name == oldName);
            if (index != -1)
            {
                _contracts[index] = updatedContract;
                SaveContractsToFile();
            }
            else
            {
                _logger.LogError("Contract with the specified name not found.");
                throw new DataNotFoundException("Contract with the specified name not found.");
            }
        }

        public void DeleteContract(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentNullException(nameof(name));
            }
            _logger.LogInformation("Delete Contract request initiated.");
            var contract = _contracts.FirstOrDefault(c => c.Name == name);
            if (contract != null)
            {
                _contracts.Remove(contract);
                SaveContractsToFile();
                _logger.LogInformation("Contract deleted successfully.");
            }
            else
            {
                throw new DataNotFoundException("Contract with the specified name not found.");
            }
        }
    }
}