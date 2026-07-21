using Newtonsoft.Json.Linq;
using Philips.IBE.Service.WebAgent.Server.Configuration;
using Philips.IBE.Service.WebAgent.Server.Exceptions;
using Philips.IBE.Service.WebAgent.Server.Models;
using Philips.IBE.Service.WebAgent.Server.Utilities;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Xml;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Philips.IBE.Service.WebAgent.Server.Services
{
    public class CommunicationDataService : ICommunicationDataService
    {
        private readonly string _filePath;
        private readonly string _certificatePath;
        private List<CommunicationPoint> _communicationPoints;
        private readonly DataProtectionUtility _protectionUtility;
        private readonly ILogger<CommunicationDataService> _logger;

        public CommunicationDataService(AppConfiguration configuration, DataProtectionUtility protectionUtility, ILogger<CommunicationDataService> logger)
        {
            if (configuration.CommonConfiguration == null)
            {
                throw new ArgumentNullException(nameof(configuration.CommonConfiguration), "CommonConfiguration cannot be null.");
            }
            _protectionUtility = protectionUtility ?? throw new ArgumentNullException(nameof(protectionUtility));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _filePath = Path.Combine(configuration.CommonConfiguration.FolderPath, "communicationData.json");
            _certificatePath = Path.Combine(configuration.CommonConfiguration.FolderPath, configuration.CommonConfiguration.CertificateFolderName);

            if (!File.Exists(_filePath))
            {
                File.Create(_filePath).Close();
                _logger.LogInformation("Created new communication data file at {FilePath}", _filePath);
            }

            _communicationPoints = LoadCommunicationDataFromFile();
        }

        private List<CommunicationPoint> LoadCommunicationDataFromFile()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var jsonString = File.ReadAllText(_filePath);
                    if (!string.IsNullOrEmpty(jsonString))
                    {
                        var data = JsonSerializer.Deserialize<CommunicationPointModel>(jsonString) ?? new CommunicationPointModel();
                        _logger.LogInformation("Loaded communication data from file.");
                        //return ConvertToCommunicationPointList(data);
                        return data.CommunicationPoints;
                    }
                }
                return new List<CommunicationPoint>();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException || ex is JsonException)
            {
                _logger.LogError(ex, "Error occurred while loading communication data from file.");
                return new List<CommunicationPoint>();
            }
        }

        private void CleanCommunicationPointConfigs(List<CommunicationPoint> points)
        {
            foreach (var cp in points)
            {
                if (string.Equals(cp.Type, "websocket", StringComparison.OrdinalIgnoreCase))
                {
                    cp.HttpConfiguration = null;
                    cp.TcpConfiguration = null;
                    cp.S3Configuration = null;
                    cp.CacheConfiguration = null;
                }
                else if (string.Equals(cp.Type, "http", StringComparison.OrdinalIgnoreCase))
                {
                    cp.WebSocketConfiguration = null;
                    cp.TcpConfiguration = null;
                    cp.S3Configuration = null;
                    cp.CacheConfiguration = null;
                }
                else if (string.Equals(cp.Type, "tcp", StringComparison.OrdinalIgnoreCase))
                {
                    cp.HttpConfiguration = null;
                    cp.WebSocketConfiguration = null;
                    cp.S3Configuration = null;
                    cp.CacheConfiguration = null;
                }
                else
                {
                    cp.HttpConfiguration = null;
                    cp.WebSocketConfiguration = null;
                    cp.TcpConfiguration = null;

                }
            }
        }



        private void SanitizeCommunicationPointJsonData(JToken token)
        {
            if (token.Type == JTokenType.Object)
            {
                var obj = (JObject)token;
                var props = obj.Properties().ToList();
                foreach (var prop in props)
                {
                    var v = prop.Value;
                    if (v.Type == JTokenType.Null ||
                        (v.Type == JTokenType.String && v.ToString().Trim() == ""))
                    {
                        prop.Remove();
                    }
                    else
                    {
                        SanitizeCommunicationPointJsonData(v);

                        if ((v.Type == JTokenType.Object && !v.HasValues) ||
                            (v.Type == JTokenType.Array && !v.Any()))
                        {
                            prop.Remove();
                        }
                    }
                }
            }
            else if (token.Type == JTokenType.Array)
            {
                var arr = (JArray)token;
                for (int i = arr.Count - 1; i >= 0; i--)
                {
                    var v = arr[i];
                    if (v.Type == JTokenType.Null ||
                        (v.Type == JTokenType.String && v.ToString().Trim() == ""))
                    {
                        arr.RemoveAt(i);
                    }
                    else
                    {
                        SanitizeCommunicationPointJsonData(v);
                        if ((v.Type == JTokenType.Object && !v.HasValues) ||
                            (v.Type == JTokenType.Array && !v.Any()))
                        {
                            arr.RemoveAt(i);
                        }
                    }
                }
            }
        }

        private string ParseCommunicationPointJsonData(string jsonString)
        {
            if (string.IsNullOrWhiteSpace(jsonString))
                return "{}";
            var parsedJsonData = JToken.Parse(jsonString);
            SanitizeCommunicationPointJsonData(parsedJsonData);
            return parsedJsonData.ToString((Newtonsoft.Json.Formatting)Formatting.Indented);
        }

        private void SaveCommunicationDataToFile()
        {
            try
            {
                _communicationPoints.ForEach(cp =>
                {
                    cp.Mode = cp.Mode.ToLower();
                    cp.Type = cp.Type.ToLower();
                    if (cp.IsSSLEnabled
                        && cp.CertificateDetails != null
                        && !string.IsNullOrEmpty(cp.CertificateDetails.ClientCertificatePath)
                        && !string.IsNullOrEmpty(cp.CertificateDetails.RootCertificatePath)
                        && !cp.CertificateDetails.ClientCertificatePath.Contains(_certificatePath)
                        && !cp.CertificateDetails.RootCertificatePath.Contains(_certificatePath))
                    {
                        var certFileName = Path.GetFileName(cp.CertificateDetails.ClientCertificatePath);
                        var rootCertFileName = Path.GetFileName(cp.CertificateDetails.RootCertificatePath);
                        cp.CertificateDetails.ClientCertificatePath = Path.Combine(_certificatePath, cp.Name, certFileName);
                        cp.CertificateDetails.RootCertificatePath = Path.Combine(_certificatePath, cp.Name, rootCertFileName);
                    }

                });

                CleanCommunicationPointConfigs(_communicationPoints);


                var model = ConvertToCommunicationPointModel(_communicationPoints);
                var jsonString = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
                jsonString = ParseCommunicationPointJsonData(jsonString);
                File.WriteAllText(_filePath, jsonString);
                _logger.LogInformation("Saved communication data to file.");
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
            {
                _logger.LogError(ex, "Error occurred while saving communication data to file.");
            }
        }

        public List<CommunicationPoint> GetAllCommunicationData()
        {
            _communicationPoints = LoadCommunicationDataFromFile();
            _logger.LogInformation("Retrieved all communication data.");
            return _communicationPoints;
        }

        public CommunicationPoint? GetCommunicationDataById(int id)
        {
            _communicationPoints = LoadCommunicationDataFromFile();
            var data = _communicationPoints.FirstOrDefault(d => d.Id == id);
            if (data != null)
            {
                _logger.LogInformation("Retrieved communication data for ID {Id}.", id);
            }
            else
            {
                _logger.LogWarning("No communication data found for ID {Id}.", id);
            }
            return data;
        }



        [SupportedOSPlatform("windows")]
        public void AddCommunicationData(CommunicationPoint data)
        {
            _communicationPoints = LoadCommunicationDataFromFile();
            data.Id = _communicationPoints.Count > 0 ? _communicationPoints.Max(d => d.Id) + 1 : 1;

            if (data.IsSSLEnabled && data.CertificateDetails?.ClientCertificatePassword != null)
            {
                data.CertificateDetails.ClientCertificatePassword = _protectionUtility.ProtectValue(data.CertificateDetails.ClientCertificatePassword);
            }
            if (data.Type.ToLower().Equals("cim_s3"))
            {
                data.S3Configuration.PrivateKeyPassword = _protectionUtility.ProtectValue(data.S3Configuration.PrivateKeyPassword);
                data.CacheConfiguration.CacheCertificatePassword = _protectionUtility.ProtectValue(data.CacheConfiguration.CacheCertificatePassword);
            }
            _communicationPoints.Add(data);
            SaveCommunicationDataToFile();
            _logger.LogInformation("Added new communication data with ID {Id}.", data.Id);
        }

        [SupportedOSPlatform("windows")]
        public void UpdateCommunicationData(int id, CommunicationPoint updatedData)
        {
            _communicationPoints = LoadCommunicationDataFromFile();
            var existingData = _communicationPoints.FirstOrDefault(d => d.Id == id);

            if (existingData != null)
            {
                existingData.Name = updatedData.Name;
                existingData.Type = updatedData.Type;
                //existingData.Port = updatedData.Port;
                existingData.TcpConfiguration = updatedData.TcpConfiguration;
                existingData.HttpConfiguration = updatedData.HttpConfiguration;
                existingData.WebSocketConfiguration = updatedData.WebSocketConfiguration;

                existingData.Mode = updatedData.Mode;
                existingData.IsSSLEnabled = updatedData.IsSSLEnabled;
                existingData.ConnectionRetry = updatedData.ConnectionRetry;
                existingData.MessageRetry = updatedData.MessageRetry;
                existingData.ProxyConfigurations = updatedData.ProxyConfigurations;


                if (updatedData.IsSSLEnabled && updatedData.CertificateDetails == null)
                {
                    throw new ArgumentNullException("Certificate details cannot be null when SSL is enabled.");
                }

                else if (updatedData.CertificateDetails != null
                    && existingData.CertificateDetails != null
                    && updatedData.CertificateDetails.ClientCertificatePassword != null
                    && updatedData.CertificateDetails.ClientCertificatePassword != existingData.CertificateDetails.ClientCertificatePassword)
                {
                    updatedData.CertificateDetails.ClientCertificatePassword = _protectionUtility.ProtectValue(updatedData.CertificateDetails.ClientCertificatePassword);
                }
                if (updatedData.Type.ToLower().Equals("cim_s3"))
                {
                    if (updatedData.S3Configuration.PrivateKeyPassword != existingData.S3Configuration.PrivateKeyPassword)
                        updatedData.S3Configuration.PrivateKeyPassword = _protectionUtility.ProtectValue(updatedData.S3Configuration.PrivateKeyPassword);
                    if (updatedData.CacheConfiguration.CacheCertificatePassword != existingData.CacheConfiguration.CacheCertificatePassword)
                        updatedData.CacheConfiguration.CacheCertificatePassword = _protectionUtility.ProtectValue(updatedData.CacheConfiguration.CacheCertificatePassword);
                    
                }
                existingData.CacheConfiguration = updatedData.CacheConfiguration;
                existingData.S3Configuration = updatedData.S3Configuration;
                existingData.CertificateDetails = updatedData.CertificateDetails;
                SaveCommunicationDataToFile();
                _logger.LogInformation("Updated communication data for ID {Id}.", id);
            }
            else
            {
                _logger.LogWarning("No communication data found for ID {Id}.", id);
                throw new DataNotFoundException("Data with the specified ID not found.");
            }
        }

        public void DeleteCommunicationData(int id)
        {
            _communicationPoints = LoadCommunicationDataFromFile();
            var dataToDelete = _communicationPoints.FirstOrDefault(d => d.Id == id);

            if (dataToDelete != null)
            {
                _communicationPoints.Remove(dataToDelete);
                SaveCommunicationDataToFile();
                _logger.LogInformation("Deleted communication data for ID {Id}.", id);
            }
            else
            {
                _logger.LogWarning("No communication data found for ID {Id}.", id);
                throw new DataNotFoundException("Data with the specified ID not found.");
            }
        }

        private CommunicationPointModel ConvertToCommunicationPointModel(List<CommunicationPoint> communicationPoints)
        {
            var model = new CommunicationPointModel();
            communicationPoints.ForEach(cp => model.CommunicationPoints.Add(cp));
            return model;
        }
    }
}