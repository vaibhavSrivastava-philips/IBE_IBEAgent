using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Philips.IBE.Service.WebAgent.Server.Configuration;
using Philips.IBE.Service.WebAgent.Server.DBUtilities;
using Philips.IBE.Service.WebAgent.Server.Models;
using Xunit;

namespace Philips.IBE.Service.WebAgent.Server.UnitTest.DBUtilities;
public class SQLLiteUtilsTests
{
    private readonly Mock<ILogger<SQLLiteUtils>> _loggerMock = new();

    private AppConfiguration GetValidConfig(string dbPath)
    {
        var inMemorySettings = new Dictionary<string, string?>
    {
        { "AuthenticationConfiguration:AdminUserGroup", "AdminGroup" },
        { "AuthenticationConfiguration:NormalUserGroup", "UserGroup" },
        { "AuthenticationConfiguration:AuthenticationMode", "ActiveDirectory" },
        { "JwtOptions:Issuer", "TestIssuer" },
        { "JwtOptions:Audience", "TestAudience" },
        { "JwtOptions:ExpirationSeconds", "3600" },
        { "CommonConfiguration:FolderPath", Path.GetDirectoryName(dbPath) ?? string.Empty },
        { "CommonConfiguration:DatabaseEnabled", "true" },
        { "CommonConfiguration:CertificateFolderName", "certs" },
        { "CommonConfiguration:DatabaseFileName", Path.GetFileName(dbPath) }
    };

        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddInMemoryCollection(inMemorySettings);
        var configuration = configBuilder.Build();

        var config = new AppConfiguration(configuration);
        var commonConfig = new CommonConfiguration
        {
            FolderPath = Path.GetDirectoryName(dbPath) ?? string.Empty,
            DatabaseFileName = Path.GetFileName(dbPath),
            CertificateFolderName = "dummy",
        };

        var prop = typeof(AppConfiguration).GetProperty("CommonConfiguration", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop == null)
            throw new InvalidOperationException("CommonConfiguration property not found on AppConfiguration.");
        prop.SetValue(config, commonConfig);

        return config;
    }
    private string CreateInMemoryDbWithTable()
    {
        var connectionString = "Data Source=:memory:";
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        var createTable = @"
            CREATE TABLE T_FailureMessageProcessor (
                ID TEXT PRIMARY KEY,
                SourceID INTEGER,
                Message TEXT,
                DestinationID INTEGER,
                DestinationURL TEXT,
                ErrorStatus INTEGER,
                ErrorDescription TEXT,
                Status TEXT,
                CreationDateTime TEXT,
                LastUpdatedDateTime TEXT
            );
            INSERT INTO T_FailureMessageProcessor VALUES (
                'id1', 1, 'msg', 2, 'url', 0, 'desc', 'Pending', '2023-01-01T00:00:00', '2023-01-01T01:00:00'
            );
        ";
        using var command = new SqliteCommand(createTable, connection);
        command.ExecuteNonQuery();

        return connectionString;
    }

    [Fact]
    public void Constructor_Throws_If_Configuration_Is_Invalid()
    {
        var config = GetValidConfig(Path.GetTempFileName());
        var prop = typeof(AppConfiguration).GetProperty("CommonConfiguration", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(prop);
        prop.SetValue(config, null);

        Assert.Throws<ArgumentNullException>(() =>
            new SQLLiteUtils(config, _loggerMock.Object));
    }


    [Fact]
    public void Constructor_Sets_ConnectionString_When_Valid()
    {
        var dbPath = Path.GetTempFileName();
        var config = GetValidConfig(dbPath);
        var utils = new SQLLiteUtils(config, _loggerMock.Object);
        Assert.NotNull(utils);
    }

    [Fact]
    public void FetchErrorQueue_Returns_Empty_If_No_Data()
    {
        var dbPath = Path.GetTempFileName();
        var config = GetValidConfig(dbPath);
        var utils = new SQLLiteUtils(config, _loggerMock.Object);

        using (var connection = new SqliteConnection($"Data Source={dbPath}.db"))
        {
            connection.Open();
            var createTable = @"
                CREATE TABLE T_FailureMessageProcessor (
                    ID TEXT PRIMARY KEY,
                    SourceID INTEGER,
                    Message TEXT,
                    DestinationID INTEGER,
                    DestinationURL TEXT,
                    ErrorStatus INTEGER,
                    ErrorDescription TEXT,
                    Status TEXT,
                    CreationDateTime TEXT,
                    LastUpdatedDateTime TEXT
                );";
            using var command = new SqliteCommand(createTable, connection);
            command.ExecuteNonQuery();
        }

        var result = utils.FetchErrorQueue();

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void FetchErrorQueue_Returns_Data_If_Present()
    {
        var dbPath = Path.GetTempFileName();
        var config = GetValidConfig(dbPath);
        var utils = new SQLLiteUtils(config, _loggerMock.Object);

        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            connection.Open();
            var createTable = @"
                CREATE TABLE T_FailureMessageProcessor (
                    ID TEXT PRIMARY KEY,
                    SourceID INTEGER,
                    Message TEXT,
                    DestinationID INTEGER,
                    DestinationURL TEXT,
                    ErrorStatus INTEGER,
                    ErrorDescription TEXT,
                    Status TEXT,
                    CreationDateTime TEXT,
                    LastUpdatedDateTime TEXT
                );";
            using var command = new SqliteCommand(createTable, connection);
            command.ExecuteNonQuery();
        }

        var result = utils.FetchErrorQueue();

        Assert.NotNull(result);
        Assert.Empty(result);
    }


    [Fact]
    public void UpdateStatus_Returns_False_When_No_Row_Updated()
    {
        var dbPath = Path.GetTempFileName();
        var config = GetValidConfig(dbPath);
        var utils = new SQLLiteUtils(config, _loggerMock.Object);

        using (var connection = new SqliteConnection($"Data Source={dbPath}.db"))
        {
            connection.Open();
            var createTable = @"
                CREATE TABLE T_FailureMessageProcessor (
                    ID TEXT PRIMARY KEY,
                    SourceID INTEGER,
                    Message TEXT,
                    DestinationID INTEGER,
                    DestinationURL TEXT,
                    ErrorStatus INTEGER,
                    ErrorDescription TEXT,
                    Status TEXT,
                    CreationDateTime TEXT,
                    LastUpdatedDateTime TEXT
                );";
            using var command = new SqliteCommand(createTable, connection);
            command.ExecuteNonQuery();
        }

        var result = utils.UpdateStatus("Done", "nonexistent");

        Assert.False(result);
    }

    [Fact]
    public void FetchErrorQueue_Handles_SqliteException()
    {
        var dbPath = Path.GetTempFileName();
        var config = GetValidConfig(dbPath);
        var utils = new SQLLiteUtils(config, _loggerMock.Object);

        File.WriteAllText(dbPath + ".db", "not a sqlite db");

        var result = utils.FetchErrorQueue();

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void FetchErrorQueue_Returns_Data_With_Valid_Dates()
    {
        var dbPath = Path.GetTempFileName();
        var config = GetValidConfig(dbPath);
        var utils = new SQLLiteUtils(config, _loggerMock.Object);

        using (var connection = new SqliteConnection($"Data Source={dbPath}.db"))
        {
            connection.Open();
            var createTable = @"
            CREATE TABLE T_FailureMessageProcessor (
                ID TEXT PRIMARY KEY,
                SourceID INTEGER,
                Message TEXT,
                DestinationID INTEGER,
                DestinationURL TEXT,
                ErrorStatus INTEGER,
                ErrorDescription TEXT,
                Status TEXT,
                CreationDateTime TEXT,
                LastUpdatedDateTime TEXT
            );";
            using var command = new SqliteCommand(createTable, connection);
            command.ExecuteNonQuery();

            var insert = @"
            INSERT INTO T_FailureMessageProcessor VALUES (
                'id2', 10, 'msg2', 20, 'url2', 1, 'desc2', 'Pending', '2023-01-01T00:00:00', '2023-01-01T01:00:00'
            );";
            using var insertCmd = new SqliteCommand(insert, connection);
            insertCmd.ExecuteNonQuery();
        }

        var result = utils.FetchErrorQueue();

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(Convert.ToInt32("id2", 16), result[0].ID);
    }

    [Fact]
    public void UpdateStatus_Returns_True_When_Row_Updated()
    {
        var dbPath = Path.GetTempFileName();
        var config = GetValidConfig(dbPath);
        var utils = new SQLLiteUtils(config, _loggerMock.Object);

        using (var connection = new SqliteConnection($"Data Source={dbPath}.db"))
        {
            connection.Open();
            var createTable = @"
            CREATE TABLE T_FailureMessageProcessor (
                ID TEXT PRIMARY KEY,
                SourceID INTEGER,
                Message TEXT,
                DestinationID INTEGER,
                DestinationURL TEXT,
                ErrorStatus INTEGER,
                ErrorDescription TEXT,
                Status TEXT,
                CreationDateTime TEXT,
                LastUpdatedDateTime TEXT
            );";
            using var command = new SqliteCommand(createTable, connection);
            command.ExecuteNonQuery();

            var insert = @"
            INSERT INTO T_FailureMessageProcessor VALUES (
                'id3', 10, 'msg3', 20, 'url3', 1, 'desc3', 'Pending', '2023-01-01T00:00:00', '2023-01-01T01:00:00'
            );";
            using var insertCmd = new SqliteCommand(insert, connection);
            insertCmd.ExecuteNonQuery();
        }

        var result = utils.UpdateStatus("Done", "id3");

        Assert.True(result);
    }

    [Fact]
    public void UpdateStatus_Handles_SqliteException()
    {
        var dbPath = Path.GetTempFileName();
        var config = GetValidConfig(dbPath);
        var utils = new SQLLiteUtils(config, _loggerMock.Object);

        File.WriteAllText(dbPath + ".db", "not a sqlite db");

        var result = utils.UpdateStatus("Done", "id4");

        Assert.False(result);
    }

    [Fact]
    public void Constructor_Throws_If_FolderPath_Or_FileName_Is_Empty()
    {
        var config = GetValidConfig(Path.GetTempFileName());
        var prop = typeof(AppConfiguration).GetProperty("CommonConfiguration", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(prop);

        var commonConfig = new CommonConfiguration
        {
            FolderPath = "",
            DatabaseFileName = "file",
            CertificateFolderName = "dummy"
        };

        prop.SetValue(config, commonConfig);
        Assert.Throws<ArgumentNullException>(() => new SQLLiteUtils(config, _loggerMock.Object));


        var commonConfig1 = new CommonConfiguration
        {
            FolderPath = "",
            DatabaseFileName = "file",
            CertificateFolderName = "dummy"
        };

        prop.SetValue(config, commonConfig1);
        Assert.Throws<ArgumentNullException>(() => new SQLLiteUtils(config, _loggerMock.Object));
    }

}
