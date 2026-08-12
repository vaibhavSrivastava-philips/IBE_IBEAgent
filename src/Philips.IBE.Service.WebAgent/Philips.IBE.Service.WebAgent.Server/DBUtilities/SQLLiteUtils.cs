using Microsoft.Data.Sqlite;
using Npgsql;
using Philips.IBE.Service.WebAgent.Server.Configuration;
using Philips.IBE.Service.WebAgent.Server.Models;
using Philips.IBE.Service.WebAgent.Server.Utilities;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Philips.IBE.Service.WebAgent.Server.DBUtilities
{
    public class SQLLiteUtils : IDBUtils
    {
        private readonly string _connectionString;
        private readonly ILogger<SQLLiteUtils> _logger;
        private readonly NpgsqlDataSource dataSource;
        private readonly DataProtectionUtility dataDecipher;
        

        public SQLLiteUtils(AppConfiguration configuration, ILogger<SQLLiteUtils> logger)
        {
            _logger = logger;

            if (configuration?.CommonConfiguration == null
                || string.IsNullOrWhiteSpace(configuration.CommonConfiguration.FolderPath)
                || string.IsNullOrWhiteSpace(configuration.CommonConfiguration.DatabaseFileName))
            {
                throw new ArgumentNullException(nameof(configuration), "CommonConfiguration, FolderPath and DatabaseFileName must be provided.");
            }

            var serviceConfigurationsPath = configuration.CommonConfiguration.ServiceConfigPath+"\\appSettings.json";
            ServiceConfigurations serviceConfigurations;

                if (string.IsNullOrEmpty(serviceConfigurationsPath) || !File.Exists(serviceConfigurationsPath))
                {
                    throw new FileNotFoundException($"Service configuration file not found at path: {serviceConfigurationsPath}");
                }

                var jsonContent = File.ReadAllText(serviceConfigurationsPath);
                
                // Log the JSON content for debugging
                _logger.LogDebug("JSON Content: {JsonContent}", jsonContent);
                
                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = null, // Use exact property names from JSON
                    WriteIndented = true
                };
                var root = JsonSerializer.Deserialize<ServiceConfigurationRoot>(jsonContent, jsonOptions) 
                    ?? throw new InvalidOperationException("Failed to deserialize service configuration root");

                serviceConfigurations = root.ServiceConfigurations ?? throw new InvalidOperationException("ServiceConfigurations section is null");

                _logger.LogInformation("Successfully loaded service configurations from: {Path}", serviceConfigurationsPath);
           
            var dbConfig = serviceConfigurations.DatabaseConfiguration?.Postgres;
            
            // Add detailed logging for debugging
            _logger.LogInformation("DatabaseConfiguration is null: {IsNull}", serviceConfigurations.DatabaseConfiguration == null);
            if (serviceConfigurations.DatabaseConfiguration != null)
            {
                _logger.LogInformation("DatabaseType: {Type}", serviceConfigurations.DatabaseConfiguration.DataBaseType ?? "NULL");
                _logger.LogInformation("Postgres config is null: {IsNull}", serviceConfigurations.DatabaseConfiguration.Postgres == null);
                if (dbConfig != null)
                {
                    _logger.LogInformation("DB Config - Host: {Host}, Username: {Username}, Database: {Database}, SslMode: {SslMode}", 
                        dbConfig.Host ?? "NULL", 
                        dbConfig.Username ?? "NULL", 
                        dbConfig.Database ?? "NULL", 
                        dbConfig.SslMode ?? "NULL");
                }
                else
                {
                    _logger.LogWarning("Postgres configuration object is null - deserialization may have failed");
                }
            }
            
            if (dbConfig == null || string.IsNullOrEmpty(dbConfig.Host) || string.IsNullOrEmpty(dbConfig.Username) || string.IsNullOrEmpty(dbConfig.Password) || string.IsNullOrEmpty(dbConfig.Database) || string.IsNullOrEmpty(dbConfig.SslMode))
            {
                throw new ArgumentNullException(nameof(serviceConfigurations), "Database configuration is missing");
            }
            _connectionString = $"Host={dbConfig.Host};Username={dbConfig.Username};Database={dbConfig.Database};Ssl Mode={dbConfig.SslMode};Trust Server Certificate={dbConfig.TrustServerCertificate};";
            dataDecipher = new DataProtectionUtility();
            var password = dataDecipher.ReadProtectedValue(dbConfig.Password);
            IntPtr passwordPtr = IntPtr.Zero;
            byte[] passBytes;
            try
            {
                passwordPtr = Marshal.SecureStringToGlobalAllocUnicode(password);
                string passwordString = Marshal.PtrToStringUni(passwordPtr);
                passBytes = Encoding.UTF8.GetBytes(passwordString);
            }
            finally
            {
                if (passwordPtr != IntPtr.Zero)
                {
                    Marshal.ZeroFreeGlobalAllocUnicode(passwordPtr);
                }
            }
            var dataSourceBuilder = new NpgsqlDataSourceBuilder(_connectionString);
            dataSourceBuilder.UsePasswordProvider(
                (connectionStringBuilder) => Encoding.UTF8.GetString(passBytes),
                async (connectionStringBuilder, cancellationToken) => await GetPasswordAsync(passBytes)
            );
            dataSource = dataSourceBuilder.Build();
        }

        private static Task<string> GetPasswordAsync(byte[] decryptedBytes)
        {
            try
            {
                string password = Encoding.UTF8.GetString(decryptedBytes);
                return Task.FromResult(password);
            }
            finally
            {
                Array.Clear(decryptedBytes, 0, decryptedBytes.Length); // wipe memory
            }
        }

        public List<ErrorQueue> FetchErrorQueue()
        {
            var queue = new List<ErrorQueue>();

            using (var connection = dataSource.OpenConnection())
            {

                string selectQuery = @"
                 SELECT ID, Message, SenderId, timestamp 
                 FROM upload_failed_data 
                 ORDER BY ID ASC 
                 LIMIT 100";

                using (var command = new NpgsqlCommand(selectQuery, connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var messageBytes = new byte[reader.GetBytes(1, 0, null, 0, 0)];
                        reader.GetBytes(1, 0, messageBytes, 0, messageBytes.Length);

                        queue.Add(new ErrorQueue
                        {
                            ID = reader.GetInt32(0),
                            Message = (byte[])reader["Message"],
                            SenderId = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                            timeStamp = reader.GetDateTime(3)
                        });
                    }
                }

            }
            return queue;
        }

        public bool UpdateStatus(string status, string messageId)
        {
            bool result = false;
            using var connection = dataSource.OpenConnection();
            string insertQuery = @"
                            INSERT INTO upload_failed_data (Message, SenderId, timestamp)
                            VALUES (@Message, @SenderId, @timestamp)";

            using var command = new NpgsqlCommand(insertQuery, connection);
            // Convert string status to byte array for bytea column
            byte[] statusBytes = Encoding.UTF8.GetBytes(status);
            command.Parameters.AddWithValue("Message", NpgsqlTypes.NpgsqlDbType.Bytea, statusBytes);
            command.Parameters.AddWithValue("SenderId", messageId);
            command.Parameters.AddWithValue("timestamp", DateTime.Now);
            command.ExecuteNonQuery();
           
            return result;
        }
    }
}
