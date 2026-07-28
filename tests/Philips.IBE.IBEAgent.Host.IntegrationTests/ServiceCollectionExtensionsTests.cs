using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.Service;

namespace Philips.IBE.IBEAgent.Host.IntegrationTests;

// §14 Phase 7 — proves the composition root: config (Ibe:Catalog/Contracts/Endpoints) compiles
// into a runnable IContractRuntime registered for DI, without needing a live Tcp/Http listener.
public sealed class ServiceCollectionExtensionsTests
{
    private static IConfiguration BuildConfiguration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ibe:Catalog:Codecs:hl7v2:Type"] = "hl7v2",

            ["Ibe:Contracts:Contracts:0:Name"] = "Adt",
            ["Ibe:Contracts:Contracts:0:Inputs:0:InputId"] = "1",
            ["Ibe:Contracts:Contracts:0:Outputs:0:OutputId"] = "100",
            ["Ibe:Contracts:Contracts:0:Outputs:0:Encoding"] = "hl7v2",

            ["Ibe:Endpoints:TcpOutbound:0:OutputId"] = "100",
            ["Ibe:Endpoints:TcpOutbound:0:Host"] = "localhost",
            ["Ibe:Endpoints:TcpOutbound:0:Port"] = "9999",
        })
        .Build();

    [Fact]
    public void AddIbeAgentEngine_compiles_configured_contracts_and_registers_runtimes()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddIbeAgentEngine(BuildConfiguration());

        using var provider = services.BuildServiceProvider();
        var runtimes = provider.GetRequiredService<IReadOnlyList<IContractRuntime>>();

        Assert.Single(runtimes);
    }

    [Fact]
    public void AddIbeAgentEngine_registers_the_runtime_host_as_a_hosted_service()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddIbeAgentEngine(BuildConfiguration());

        using var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>();

        Assert.Contains(hosted, h => h is AgentRuntimeHost);
    }
}
