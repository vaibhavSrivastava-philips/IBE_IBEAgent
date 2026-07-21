using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Philips.IBE.Service.WebAgent.Server.Configuration;
using Philips.IBE.Service.WebAgent.Server.Models;
using Philips.IBE.Service.WebAgent.Server.Utilities;
using System.Configuration;
using System.Runtime.Versioning;

namespace Philips.IBE.Service.WebAgent.Server.Services
{
    public class NodeService : INodeService
    {
        private readonly ServiceConfigurations _serviceConfigurations;
        private readonly string _serviceConfigurationsPath;
        private readonly ILogger<NodeService> _logger;
        private readonly string _certificatePath;

        private readonly DataProtectionUtility _protectionUtility;

        [SupportedOSPlatform("windows")]
        public NodeService(AppConfiguration configuration, ILogger<NodeService> logger, DataProtectionUtility protectionUtility)
        {
            if (configuration.CommonConfiguration == null || string.IsNullOrWhiteSpace(configuration.CommonConfiguration.FolderPath))
            {
                throw new ArgumentNullException("CommonConfiguration cannot be null or empty.");
            }
            _serviceConfigurationsPath = Path.Combine(configuration.CommonConfiguration.ServiceConfigPath, "appsettings.json");
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _certificatePath = Path.Combine(configuration.CommonConfiguration.FolderPath, configuration.CommonConfiguration.CertificateFolderName);
            _serviceConfigurations = LoadServiceConfigurationRootFromFile() ?? new ServiceConfigurations();
            initialize(configuration);
            _protectionUtility = protectionUtility;
        }

        [SupportedOSPlatform("windows")]
        private void initialize(AppConfiguration configuration)
        {
            if (configuration.CommonConfiguration == null || string.IsNullOrWhiteSpace(configuration.CommonConfiguration.FolderPath))
            {
                throw new ArgumentNullException("CommonConfiguration cannot be null or empty.");
            }

            if (_serviceConfigurations.WorkflowConfiguration == null)
            {
                _serviceConfigurations.WorkflowConfiguration = new WorkflowConfiguration();
            }
            _serviceConfigurations.WorkflowConfiguration.CommunicationPoints = Path.Combine(configuration.CommonConfiguration.FolderPath, "communicationData.json");
            _serviceConfigurations.WorkflowConfiguration.Contracts = Path.Combine(configuration.CommonConfiguration.FolderPath, "contractData.json");

            if (_serviceConfigurations.DatabaseConfiguration == null)
            {
                _serviceConfigurations.DatabaseConfiguration = new DatabaseConfiguration();
            }
            if (_serviceConfigurations.DatabaseConfiguration.Postgres == null)
            {
                _serviceConfigurations.DatabaseConfiguration.Postgres = new PostgresConfiguration();
            }

        }

        private ServiceConfigurations? LoadServiceConfigurationRootFromFile()
        {
            try
            {
                _logger.LogInformation("Loading communication data from file: {FilePath}", _serviceConfigurationsPath);
                if (File.Exists(_serviceConfigurationsPath))
                {
                    var jsonString = File.ReadAllText(_serviceConfigurationsPath);
                    if (!string.IsNullOrEmpty(jsonString))
                    {
                        _logger.LogInformation("Successfully loaded communication data from file.");
                        var result = JsonConvert.DeserializeObject<ServiceConfigurationRoot>(jsonString);
                        
                        return result?.ServiceConfigurations;
                    }
                }
                _logger.LogWarning("Communication data file does not exist or is empty.");
                return new ServiceConfigurations();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException || ex is JsonException)
            {
                _logger.LogError(ex, "Error occurred while loading communication data from file.");
                throw new Exception("Error occurred while loading communication data from file.", ex);
            }
        }

        private bool SaveServiceConfigurationToFile()
        {
            try
            {
                _logger.LogInformation("Saving communication data to file: {FilePath}", _serviceConfigurationsPath);

                JObject appSettings;
                if (File.Exists(_serviceConfigurationsPath))
                {
                    var existingJson = File.ReadAllText(_serviceConfigurationsPath);
                    appSettings = !string.IsNullOrWhiteSpace(existingJson) ? JObject.Parse(existingJson) : new JObject();
                }
                else
                {
                    appSettings = new JObject();
                }

                var serviceConfigRoot = new ServiceConfigurationRoot
                {
                    ServiceConfigurations = _serviceConfigurations
                };
                var serviceConfigJson = JObject.Parse(JsonConvert.SerializeObject(serviceConfigRoot.ServiceConfigurations, Formatting.Indented, new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore
                }));

                appSettings["ServiceConfigurations"] = serviceConfigJson;
                File.WriteAllText(_serviceConfigurationsPath, appSettings.ToString(Formatting.Indented));
                _logger.LogInformation("Successfully saved communication data to file.");
                return true;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException || ex is JsonException)
            {
                _logger.LogError(ex, "Error occurred while saving communication data to file.");
                return false;
            }
        }
        public ServiceNode GetServiceNode()
        {
            _logger.LogInformation("Retrieving service node configuration.");
            if (_serviceConfigurations == null || _serviceConfigurations.Nodes == null)
            {
                throw new InvalidOperationException("Service node configuration is not initialized.");
            }
            return _serviceConfigurations.Nodes;
        }


        public bool UpdateHTTPServiceNode(ServiceNodeConfiguration serviceNode)
        {
            _logger.LogInformation("Updating HTTP service node configuration.");
            if (_serviceConfigurations == null || _serviceConfigurations.Nodes == null)
            {
                throw new InvalidOperationException("Service node configuration is not initialized.");
            }

            if (serviceNode.EnableSSL) {
                if (serviceNode.SSLConfiguration != null)
                {
                    if (!string.IsNullOrEmpty(serviceNode.SSLConfiguration.RootCertificatePath) &&
                        !serviceNode.SSLConfiguration.RootCertificatePath.Contains(_certificatePath))
                    {
                        var rootCertFileName = Path.GetFileName(serviceNode.SSLConfiguration.RootCertificatePath);
                        serviceNode.SSLConfiguration.RootCertificatePath = Path.Combine(_certificatePath, "http-service", rootCertFileName);
                    }

                    if (!string.IsNullOrEmpty(serviceNode.SSLConfiguration.ClientCertificatePath) &&
                       !serviceNode.SSLConfiguration.ClientCertificatePath.Contains(_certificatePath))
                    {
                        var certFileName = Path.GetFileName(serviceNode.SSLConfiguration.ClientCertificatePath);
                        serviceNode.SSLConfiguration.ClientCertificatePath = Path.Combine(_certificatePath, "http-service", certFileName);
                    }

                    if (!string.IsNullOrEmpty(serviceNode.SSLConfiguration.ServerCertificatePath) &&
                        !serviceNode.SSLConfiguration.ServerCertificatePath.Contains(_certificatePath))
                    {
                        var serverCertFileName = Path.GetFileName(serviceNode.SSLConfiguration.ServerCertificatePath);
                        serviceNode.SSLConfiguration.ServerCertificatePath = Path.Combine(_certificatePath, "http-service", serverCertFileName);
                    }


                    if (string.IsNullOrEmpty(_serviceConfigurations.Nodes.HTTP?.SSLConfiguration?.ServerCertificatePassword) ||
                        serviceNode.SSLConfiguration.ServerCertificatePassword != _serviceConfigurations.Nodes.HTTP.SSLConfiguration.ServerCertificatePassword)
                    {
                        if (!string.IsNullOrEmpty(serviceNode.SSLConfiguration.ServerCertificatePassword))
                        {
                            serviceNode.SSLConfiguration.ServerCertificatePassword =
                                _protectionUtility.ProtectValue(serviceNode.SSLConfiguration.ServerCertificatePassword);
                        }
                        else
                        {
                            _logger.LogWarning("ServerCertificatePassword is null or empty and cannot be protected.");
                        }
                    }


                    if (string.IsNullOrEmpty(_serviceConfigurations.Nodes.HTTP?.SSLConfiguration?.ClientCertificatePassword) ||
                        serviceNode.SSLConfiguration.ClientCertificatePassword != _serviceConfigurations.Nodes.HTTP.SSLConfiguration.ClientCertificatePassword)
                    {
                        if (!string.IsNullOrEmpty(serviceNode.SSLConfiguration.ClientCertificatePassword))
                        {
                            serviceNode.SSLConfiguration.ClientCertificatePassword =
                                _protectionUtility.ProtectValue(serviceNode.SSLConfiguration.ClientCertificatePassword);
                        }
                        else
                        {
                            _logger.LogWarning("ClientCertificatePassword is null or empty and cannot be protected.");
                        }
                    }
                }
            }

            _serviceConfigurations.Nodes.HTTP = serviceNode;
            return SaveServiceConfigurationToFile();
        }


        [SupportedOSPlatform("windows")]
        public bool UpdateTCPServiceNode(ServiceNodeConfiguration serviceNode)
        {
            _logger.LogInformation("Updating TCP service node configuration.");
            if (_serviceConfigurations == null || _serviceConfigurations.Nodes == null)
            {
                throw new InvalidOperationException("Service node configuration is not initialized.");
            }
            if (serviceNode.EnableSSL)
            {
                if (serviceNode.SSLConfiguration != null)
                {
                    if (!string.IsNullOrEmpty(serviceNode.SSLConfiguration.ClientCertificatePath) &&
                        !serviceNode.SSLConfiguration.ClientCertificatePath.Contains(_certificatePath))
                    {
                        var certFileName = Path.GetFileName(serviceNode.SSLConfiguration.ClientCertificatePath);
                        serviceNode.SSLConfiguration.ClientCertificatePath = Path.Combine(_certificatePath, "tcp-service", certFileName);
                    }

                    if (!string.IsNullOrEmpty(serviceNode.SSLConfiguration.ServerCertificatePath) &&
                        !serviceNode.SSLConfiguration.ServerCertificatePath.Contains(_certificatePath))
                    {
                        var serverCertFileName = Path.GetFileName(serviceNode.SSLConfiguration.ServerCertificatePath);
                        serviceNode.SSLConfiguration.ServerCertificatePath = Path.Combine(_certificatePath, "tcp-service", serverCertFileName);
                    }


                    if (string.IsNullOrEmpty(_serviceConfigurations.Nodes.TCP?.SSLConfiguration?.ServerCertificatePassword) ||
                        serviceNode.SSLConfiguration.ServerCertificatePassword != _serviceConfigurations.Nodes.TCP.SSLConfiguration.ServerCertificatePassword)
                    {
                        if (!string.IsNullOrEmpty(serviceNode.SSLConfiguration.ServerCertificatePassword))
                        {
                            serviceNode.SSLConfiguration.ServerCertificatePassword =
                                _protectionUtility.ProtectValue(serviceNode.SSLConfiguration.ServerCertificatePassword);
                        }
                        else
                        {
                            _logger.LogWarning("ServerCertificatePassword is null or empty and cannot be protected.");
                        }
                    }


                    if (string.IsNullOrEmpty(_serviceConfigurations.Nodes.TCP?.SSLConfiguration?.ClientCertificatePassword) ||
                        serviceNode.SSLConfiguration.ClientCertificatePassword != _serviceConfigurations.Nodes.TCP.SSLConfiguration.ClientCertificatePassword)
                    {
                        if (!string.IsNullOrEmpty(serviceNode.SSLConfiguration.ClientCertificatePassword))
                        {
                            serviceNode.SSLConfiguration.ClientCertificatePassword =
                                _protectionUtility.ProtectValue(serviceNode.SSLConfiguration.ClientCertificatePassword);
                        }
                        else
                        {
                            _logger.LogWarning("ClientCertificatePassword is null or empty and cannot be protected.");
                        }
                    }
                }
            }
            _serviceConfigurations.Nodes.TCP = serviceNode;
            return SaveServiceConfigurationToFile();
        }

        [SupportedOSPlatform("windows")]
        public bool UpdateADTServiceNode(ServiceNodeConfiguration serviceNode)
        {
            _logger.LogInformation("Updating ADT service node configuration.");
            if (_serviceConfigurations == null || _serviceConfigurations.Nodes == null)
            {
                throw new InvalidOperationException("Service node configuration is not initialized.");
            }

            if (serviceNode.EnableSSL)
            {
                if (serviceNode.SSLConfiguration != null)
                {
                    if (!string.IsNullOrEmpty(serviceNode.SSLConfiguration.ClientCertificatePath) &&
                        !serviceNode.SSLConfiguration.ClientCertificatePath.Contains(_certificatePath))
                    {
                        var certFileName = Path.GetFileName(serviceNode.SSLConfiguration.ClientCertificatePath);
                        serviceNode.SSLConfiguration.ClientCertificatePath = Path.Combine(_certificatePath, "adt-service", certFileName);
                    }

                    if (!string.IsNullOrEmpty(serviceNode.SSLConfiguration.ServerCertificatePath) &&
                        !serviceNode.SSLConfiguration.ServerCertificatePath.Contains(_certificatePath))
                    {
                        var serverCertFileName = Path.GetFileName(serviceNode.SSLConfiguration.ServerCertificatePath);
                        serviceNode.SSLConfiguration.ServerCertificatePath = Path.Combine(_certificatePath, "adt-service", serverCertFileName);
                    }

                    if (string.IsNullOrEmpty(_serviceConfigurations.Nodes.ADT?.SSLConfiguration?.ServerCertificatePassword) ||
                        serviceNode.SSLConfiguration.ServerCertificatePassword != _serviceConfigurations.Nodes.ADT.SSLConfiguration.ServerCertificatePassword)
                    {
                        if (!string.IsNullOrEmpty(serviceNode.SSLConfiguration.ServerCertificatePassword))
                        {
                            serviceNode.SSLConfiguration.ServerCertificatePassword =
                                _protectionUtility.ProtectValue(serviceNode.SSLConfiguration.ServerCertificatePassword);
                        }
                        else
                        {
                            _logger.LogWarning("ServerCertificatePassword is null or empty and cannot be protected.");
                        }
                    }

                    if (string.IsNullOrEmpty(_serviceConfigurations.Nodes.ADT?.SSLConfiguration?.ClientCertificatePassword) ||
                        serviceNode.SSLConfiguration.ClientCertificatePassword != _serviceConfigurations.Nodes.ADT.SSLConfiguration.ClientCertificatePassword)
                    {
                        if (!string.IsNullOrEmpty(serviceNode.SSLConfiguration.ClientCertificatePassword))
                        {
                            serviceNode.SSLConfiguration.ClientCertificatePassword =
                                _protectionUtility.ProtectValue(serviceNode.SSLConfiguration.ClientCertificatePassword);
                        }
                        else
                        {
                            _logger.LogWarning("ClientCertificatePassword is null or empty and cannot be protected.");
                        }
                    }
                }
            }

            _serviceConfigurations.Nodes.ADT = serviceNode;
            return SaveServiceConfigurationToFile();
        }

        [SupportedOSPlatform("windows")]
        public bool UpdateWebSocketClientServiceNode(ServiceNodeConfiguration serviceNode)
        {
            _logger.LogInformation("Updating WebSocket client service node configuration.");
            if (_serviceConfigurations == null || _serviceConfigurations.Nodes == null)
            {
                throw new InvalidOperationException("Service node configuration is not initialized.");
            }
            if (serviceNode.EnableSSL)
            {
                if (serviceNode.SSLConfiguration != null)
                {
                    if (!string.IsNullOrEmpty(serviceNode.SSLConfiguration.ServerCertificatePath) &&
                        !serviceNode.SSLConfiguration.ServerCertificatePath.Contains(_certificatePath))
                    {
                        var certFileName = Path.GetFileName(serviceNode.SSLConfiguration.ServerCertificatePath);
                        serviceNode.SSLConfiguration.ServerCertificatePath = Path.Combine(_certificatePath, "webSocket-service", certFileName);
                    }

                    if (!string.IsNullOrEmpty(serviceNode.SSLConfiguration.RootCertificatePath) &&
                        !serviceNode.SSLConfiguration.RootCertificatePath.Contains(_certificatePath))
                    {
                        var rootCertFileName = Path.GetFileName(serviceNode.SSLConfiguration.RootCertificatePath);
                        serviceNode.SSLConfiguration.RootCertificatePath = Path.Combine(_certificatePath, "webSocket-service", rootCertFileName);
                    }

                    if (string.IsNullOrEmpty(_serviceConfigurations.Nodes.WebSocketClient?.SSLConfiguration?.ServerCertificatePassword) ||
                        serviceNode.SSLConfiguration.ServerCertificatePassword != _serviceConfigurations.Nodes.WebSocketClient.SSLConfiguration.ServerCertificatePassword)
                    {
                        if (!string.IsNullOrEmpty(serviceNode.SSLConfiguration.ServerCertificatePassword))
                        {
                            serviceNode.SSLConfiguration.ServerCertificatePassword =
                                _protectionUtility.ProtectValue(serviceNode.SSLConfiguration.ServerCertificatePassword);
                        }
                        else
                        {
                            _logger.LogWarning("ServerCertificatePassword is null or empty and cannot be protected.");
                        }
                    }


                }
            }

            if (serviceNode.ProxyConfigurations != null)
            {
                var currentProxyPassword = _serviceConfigurations.Nodes.WebSocketClient?.ProxyConfigurations?.ProxyPassword;
                if (!string.IsNullOrEmpty(serviceNode.ProxyConfigurations.ProxyPassword) &&
                    (string.IsNullOrEmpty(currentProxyPassword) || serviceNode.ProxyConfigurations.ProxyPassword != currentProxyPassword))
                {
                    serviceNode.ProxyConfigurations.ProxyPassword =
                        _protectionUtility.ProtectValue(serviceNode.ProxyConfigurations.ProxyPassword);
                }
                else if (string.IsNullOrEmpty(serviceNode.ProxyConfigurations.ProxyPassword))
                {
                    _logger.LogWarning("ProxyPassword is null or empty and cannot be protected.");
                }
            }
            _serviceConfigurations.Nodes.WebSocketClient = serviceNode;
            return SaveServiceConfigurationToFile();
        }
    }
}
