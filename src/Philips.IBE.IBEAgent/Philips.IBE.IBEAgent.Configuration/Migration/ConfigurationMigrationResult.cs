namespace Philips.IBE.IBEAgent.Configuration;

public sealed record ConfigurationMigrationResult(
    AgentConfigurationDocument? Document,
    ConfigurationMigrationReport Report);
