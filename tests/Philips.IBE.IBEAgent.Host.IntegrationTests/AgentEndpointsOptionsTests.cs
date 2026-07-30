using Microsoft.Extensions.Configuration;
using Philips.IBE.IBEAgent.Service;

namespace Philips.IBE.IBEAgent.Host.IntegrationTests;

// Parity with TCP PoolSize: the HTTP outbound connection-pool knobs bind from the Endpoints section
// and fall back to the endpoint's own SocketsHttpHandler defaults when omitted.
public sealed class AgentEndpointsOptionsTests
{
    private static HttpOutboundEndpointConfig BindHttpOutbound(params (string Key, string Value)[] extra)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Endpoints:HttpOutbound:0:OutputId"] = "102",
            ["Endpoints:HttpOutbound:0:Endpoint"] = "http://localhost:5202/ibe/",
        };
        foreach (var (key, value) in extra)
            settings[$"Endpoints:HttpOutbound:0:{key}"] = value;

        var endpoints = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build()
            .GetSection("Endpoints")
            .Get<AgentEndpointsOptions>()!;

        return Assert.Single(endpoints.HttpOutbound);
    }

    [Fact]
    public void Pool_knobs_bind_from_config()
    {
        var http = BindHttpOutbound(
            ("MaxConnectionsPerServer", "32"),
            ("PooledConnectionLifetimeSeconds", "600"),
            ("PooledConnectionIdleTimeoutSeconds", "90"));

        Assert.Equal(32, http.MaxConnectionsPerServer);
        Assert.Equal(600, http.PooledConnectionLifetimeSeconds);
        Assert.Equal(90, http.PooledConnectionIdleTimeoutSeconds);
    }

    [Fact]
    public void Pool_knobs_default_when_omitted()
    {
        var http = BindHttpOutbound();

        Assert.Equal(8, http.MaxConnectionsPerServer);
        Assert.Equal(300, http.PooledConnectionLifetimeSeconds);
        Assert.Equal(120, http.PooledConnectionIdleTimeoutSeconds);
    }
}
