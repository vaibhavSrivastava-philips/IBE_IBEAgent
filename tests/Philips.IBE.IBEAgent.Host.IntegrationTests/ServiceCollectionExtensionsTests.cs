using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Configuration;
using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.Persistence;
using Philips.IBE.IBEAgent.Security;
using Philips.IBE.IBEAgent.Service;

namespace Philips.IBE.IBEAgent.Host.IntegrationTests;

// §14 Phase 7 — proves the composition root: config (Catalog/Contracts/Endpoints) compiles
// into a runnable IContractRuntime registered for DI, without needing a live Tcp/Http listener.
public sealed class ServiceCollectionExtensionsTests
{
    private static IConfiguration BuildConfiguration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Catalog:Codecs:hl7v2:Type"] = "hl7v2",
            ["Catalog:Formats:hl7-standard:Codec"] = "hl7v2",
            ["Catalog:Templates:adt:Format"] = "hl7-standard",

            ["Contracts:0:Name"] = "Adt",
            ["Contracts:0:Template"] = "adt",
            ["Contracts:0:Inputs:0:InputId"] = "1",
            ["Contracts:0:Outputs:0:OutputId"] = "100",

            ["Endpoints:TcpOutbound:0:OutputId"] = "100",
            ["Endpoints:TcpOutbound:0:Host"] = "localhost",
            ["Endpoints:TcpOutbound:0:Port"] = "9999",
        })
        .Build();

    // Non-empty pipeline: the adt template names the "main" pipeline, which lists the "passthrough"
    // stage. Compiling this requires the real ComponentRegistryBuilder to have registered that stage
    // (via AddCoreStages) — otherwise CreateStage throws "No stage registered with name 'passthrough'".
    private static IConfiguration BuildPipelineConfiguration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Catalog:Codecs:hl7v2:Type"] = "hl7v2",
            ["Catalog:Formats:hl7-standard:Codec"] = "hl7v2",
            ["Catalog:Pipelines:main:0"] = "passthrough",
            ["Catalog:Templates:adt:Format"] = "hl7-standard",
            ["Catalog:Templates:adt:Pipeline"] = "main",

            ["Contracts:0:Name"] = "Adt",
            ["Contracts:0:Template"] = "adt",
            ["Contracts:0:Inputs:0:InputId"] = "1",
            ["Contracts:0:Outputs:0:OutputId"] = "100",

            ["Endpoints:TcpOutbound:0:OutputId"] = "100",
            ["Endpoints:TcpOutbound:0:Host"] = "localhost",
            ["Endpoints:TcpOutbound:0:Port"] = "9999",
        })
        .Build();

    // Legacy/manual path: no Template - developer concerns wired inline on the contract.
    private static IConfiguration BuildInlineConfiguration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Catalog:Codecs:hl7v2:Type"] = "hl7v2",

            ["Contracts:0:Name"] = "Adt",
            ["Contracts:0:Inputs:0:InputId"] = "1",
            ["Contracts:0:Outputs:0:OutputId"] = "100",
            ["Contracts:0:Outputs:0:Encoding"] = "hl7v2",

            ["Endpoints:TcpOutbound:0:OutputId"] = "100",
            ["Endpoints:TcpOutbound:0:Host"] = "localhost",
            ["Endpoints:TcpOutbound:0:Port"] = "9999",
        })
        .Build();

    [Fact]
    public async Task AddIbeAgentEngine_compiles_configured_contracts_and_registers_runtimes()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddIbeAgentEngine(BuildConfiguration());

        await using var provider = services.BuildServiceProvider();
        var runtimes = provider.GetRequiredService<IReadOnlyList<IContractRuntime>>();

        Assert.Single(runtimes);
    }

    [Fact]
    public async Task AddIbeAgentEngine_registers_the_runtime_host_as_a_hosted_service()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddIbeAgentEngine(BuildConfiguration());

        await using var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>();

        Assert.Contains(hosted, h => h is AgentRuntimeHost);
    }

    [Fact]
    public void AddIbeAgentEngine_registers_agent_runtime_health_reporter()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddIbeAgentEngine(BuildConfiguration());

        using var provider = services.BuildServiceProvider();
        var reporter = provider.GetRequiredService<AgentRuntimeHealthReporter>();
        var reporters = provider.GetServices<IHealthReporter>().ToArray();

        var snapshot = reporter.GetSnapshot();
        Assert.Equal("agent-runtime", snapshot.Component);
        Assert.Equal(HealthStatus.Degraded, snapshot.Status);
        Assert.Contains(reporters, r => ReferenceEquals(r, reporter));
    }

    [Fact]
    public void AddIbeAgentEngine_registers_health_snapshot_provider_with_sanitized_component_statuses()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddIbeAgentEngine(BuildConfiguration());

        using var provider = services.BuildServiceProvider();
        var health = provider.GetRequiredService<IHealthSnapshotProvider>();

        var overall = health.GetOverallSnapshot();
        var snapshots = health.GetSnapshots();

        Assert.Equal("ibe-agent", overall.Component);
        Assert.Equal(HealthStatus.Degraded, overall.Status);
        Assert.Contains("agent-runtime: Degraded", overall.Detail);
        Assert.Contains(snapshots, s => s.Component == "agent-runtime");
    }

    [Fact]
    public async Task AddIbeAgentEngine_supports_legacy_inline_contracts_without_a_template()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddIbeAgentEngine(BuildInlineConfiguration());

        await using var provider = services.BuildServiceProvider();
        var runtimes = provider.GetRequiredService<IReadOnlyList<IContractRuntime>>();

        Assert.Single(runtimes);
    }

    [Fact]
    public async Task AddIbeAgentEngine_compiles_a_contract_with_a_non_empty_pipeline()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddIbeAgentEngine(BuildPipelineConfiguration());

        await using var provider = services.BuildServiceProvider();
        var runtimes = provider.GetRequiredService<IReadOnlyList<IContractRuntime>>();

        Assert.Single(runtimes);
    }

    // Proves the File comm point wires end-to-end through the composition root: a File input source
    // compiles into an IInboundEndpoint, and a File output leg resolves to a File outbound endpoint.
    private static IConfiguration BuildFileConfiguration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Catalog:Codecs:hl7v2:Type"] = "hl7v2",
            ["Catalog:Formats:hl7-standard:Codec"] = "hl7v2",
            ["Catalog:Templates:adt:Format"] = "hl7-standard",

            ["Contracts:0:Name"] = "FileFlow",
            ["Contracts:0:Template"] = "adt",
            ["Contracts:0:Inputs:0:InputId"] = "1",
            ["Contracts:0:Outputs:0:OutputId"] = "100",

            ["Endpoints:FileInbound:0:SourceEndpointId"] = "1",
            ["Endpoints:FileInbound:0:Directory"] = Path.Combine(Path.GetTempPath(), "ibe-host-filein"),
            ["Endpoints:FileOutbound:0:OutputId"] = "100",
            ["Endpoints:FileOutbound:0:Directory"] = Path.Combine(Path.GetTempPath(), "ibe-host-fileout"),
        })
        .Build();

    [Fact]
    public async Task AddIbeAgentEngine_compiles_a_file_input_and_output_contract()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddIbeAgentEngine(BuildFileConfiguration());

        await using var provider = services.BuildServiceProvider();
        var inbound = provider.GetRequiredService<IReadOnlyList<IInboundEndpoint>>();
        var runtimes = provider.GetRequiredService<IReadOnlyList<IContractRuntime>>();

        Assert.Single(inbound);    // the File inbound endpoint was constructed
        Assert.Single(runtimes);   // the File output leg (OutputId 100) resolved to a File endpoint
    }

    [Fact]
    public async Task ReloadableEngineManager_replaces_valid_snapshots_atomically_and_rolls_back_invalid_changes()
    {
        var protector = DataProtectorFactory.Create();
        var directory = Path.Combine(Path.GetTempPath(), "ibe-host-forward-store-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            await using var manager = new ReloadableEngineManager(
                new InMemoryForwardStore(protector),
                protector,
                NullLoggerFactory.Instance,
                NullLogger<ReloadableEngineManager>.Instance);

            await manager.LoadInitialAsync(BuildCatalog(), BuildContracts("Initial"), BuildEndpoints(100), CancellationToken.None);

            Assert.Equal(1, manager.Version);
            Assert.Single(manager.Current!.Runtimes);

            var invalidReload = await manager.TryReloadAsync(BuildCatalog(), BuildContracts("Invalid"), new AgentEndpointsOptions
            {
                TcpOutbound =
                [
                    new TcpOutboundEndpointConfig
                    {
                        OutputId = 100,
                        Host = "localhost",
                        Port = 9999,
                        Mode = CommunicationMode.DuplexOutbound,
                    },
                ],
            }, CancellationToken.None);

            Assert.False(invalidReload);
            Assert.Equal(1, manager.Version);

            var validReload = await manager.TryReloadAsync(BuildCatalog(), BuildContracts("Replacement"), BuildEndpoints(100), CancellationToken.None);

            Assert.True(validReload);
            Assert.Equal(2, manager.Version);
            Assert.Single(manager.Current!.Runtimes);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static CatalogOptions BuildCatalog() => new()
    {
        Codecs = new Dictionary<string, CodecOptions>
        {
            ["hl7v2"] = new() { Type = "hl7v2" },
        },
    };

    private static List<ContractOptions> BuildContracts(string name) =>
    [
        new ContractOptions
        {
            Name = name,
            Inputs = [new InputOptions { InputId = 1 }],
            Outputs = [new OutputOptions { OutputId = 100, Encoding = "hl7v2" }],
        },
    ];

    private static AgentEndpointsOptions BuildEndpoints(int outputId) => new()
    {
        TcpOutbound =
        [
            new TcpOutboundEndpointConfig
            {
                OutputId = outputId,
                Host = "localhost",
                Port = 9999,
            },
        ],
    };
}
