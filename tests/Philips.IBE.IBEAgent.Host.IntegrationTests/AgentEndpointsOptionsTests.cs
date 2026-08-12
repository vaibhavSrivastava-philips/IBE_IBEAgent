using Microsoft.Extensions.Configuration;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Security;
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

    [Fact]
    public void Endpoint_modes_default_to_existing_half_duplex_roles()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Endpoints:TcpInbound:0:SourceEndpointId"] = "1",
            ["Endpoints:TcpInbound:0:Port"] = "6000",
            ["Endpoints:TcpOutbound:0:OutputId"] = "2",
            ["Endpoints:TcpOutbound:0:Host"] = "localhost",
            ["Endpoints:TcpOutbound:0:Port"] = "6001",
            ["Endpoints:HttpInbound:0:SourceEndpointId"] = "3",
            ["Endpoints:HttpInbound:0:Prefix"] = "http://localhost:6002/ibe/",
            ["Endpoints:HttpOutbound:0:OutputId"] = "4",
            ["Endpoints:HttpOutbound:0:Endpoint"] = "http://localhost:6003/ibe/",
            ["Endpoints:WebSocketInbound:0:SourceEndpointId"] = "5",
            ["Endpoints:WebSocketInbound:0:Prefix"] = "http://localhost:6004/ws/",
            ["Endpoints:WebSocketOutbound:0:OutputId"] = "6",
            ["Endpoints:WebSocketOutbound:0:Endpoint"] = "ws://localhost:6005/ws/",
            ["Endpoints:FileInbound:0:SourceEndpointId"] = "7",
            ["Endpoints:FileInbound:0:Directory"] = "C:\\in",
            ["Endpoints:FileOutbound:0:OutputId"] = "8",
            ["Endpoints:FileOutbound:0:Directory"] = "C:\\out",
        };

        var endpoints = BindEndpoints(settings);

        Assert.Equal(CommunicationMode.Inbound, Assert.Single(endpoints.TcpInbound).Mode);
        Assert.Equal(CommunicationMode.Outbound, Assert.Single(endpoints.TcpOutbound).Mode);
        Assert.Equal(CommunicationMode.Inbound, Assert.Single(endpoints.HttpInbound).Mode);
        Assert.Equal(CommunicationMode.Outbound, Assert.Single(endpoints.HttpOutbound).Mode);
        Assert.Equal(CommunicationMode.Inbound, Assert.Single(endpoints.WebSocketInbound).Mode);
        Assert.Equal(CommunicationMode.Outbound, Assert.Single(endpoints.WebSocketOutbound).Mode);
        Assert.Equal(CommunicationMode.Inbound, Assert.Single(endpoints.FileInbound).Mode);
        Assert.Equal(CommunicationMode.Outbound, Assert.Single(endpoints.FileOutbound).Mode);
    }

    [Fact]
    public void Endpoint_modes_bind_from_config()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Endpoints:TcpInbound:0:SourceEndpointId"] = "1",
            ["Endpoints:TcpInbound:0:Port"] = "6000",
            ["Endpoints:TcpInbound:0:Mode"] = "DuplexInbound",
            ["Endpoints:TcpOutbound:0:OutputId"] = "2",
            ["Endpoints:TcpOutbound:0:Host"] = "localhost",
            ["Endpoints:TcpOutbound:0:Port"] = "6001",
            ["Endpoints:TcpOutbound:0:Mode"] = "DuplexOutbound",
        };

        var endpoints = BindEndpoints(settings);

        Assert.Equal(CommunicationMode.DuplexInbound, Assert.Single(endpoints.TcpInbound).Mode);
        Assert.Equal(CommunicationMode.DuplexOutbound, Assert.Single(endpoints.TcpOutbound).Mode);
    }

    [Fact]
    public void Http_logical_bidirectional_pair_binds_and_validates()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Endpoints:HttpInbound:0:SourceEndpointId"] = "3",
            ["Endpoints:HttpInbound:0:Prefix"] = "http://localhost:6002/ibe/callback/",
            ["Endpoints:HttpInbound:0:Mode"] = "DuplexInbound",
            ["Endpoints:HttpInbound:0:LogicalEndpointId"] = "partner-a",
            ["Endpoints:HttpOutbound:0:OutputId"] = "4",
            ["Endpoints:HttpOutbound:0:Endpoint"] = "http://localhost:6003/ibe/outbound/",
            ["Endpoints:HttpOutbound:0:Mode"] = "DuplexOutbound",
            ["Endpoints:HttpOutbound:0:LogicalEndpointId"] = "partner-a",
        };

        var endpoints = BindEndpoints(settings);
        var result = AgentEndpointsOptionsValidator.Validate(endpoints);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
        Assert.Equal("partner-a", Assert.Single(endpoints.HttpInbound).LogicalEndpointId);
        Assert.Equal("partner-a", Assert.Single(endpoints.HttpOutbound).LogicalEndpointId);
    }

    [Fact]
    public void Http_logical_bidirectional_pair_requires_matching_inbound_listener()
    {
        var endpoints = new AgentEndpointsOptions
        {
            HttpOutbound =
            [
                new HttpOutboundEndpointConfig
                {
                    OutputId = 4,
                    Endpoint = new Uri("http://localhost:6003/ibe/outbound/"),
                    Mode = CommunicationMode.DuplexOutbound,
                    LogicalEndpointId = "partner-a",
                },
            ],
        };

        var result = AgentEndpointsOptionsValidator.Validate(endpoints);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("no matching HttpInbound endpoint is configured"));
    }

    [Fact]
    public void WebSocket_duplex_outbound_modes_validate_required_source_ids()
    {
        var valid = new AgentEndpointsOptions
        {
            WebSocketOutbound =
            [
                new WebSocketOutboundEndpointConfig
                {
                    OutputId = 6,
                    Endpoint = new Uri("ws://localhost:6005/ws/"),
                    Mode = CommunicationMode.DuplexOutbound,
                    SourceEndpointId = 5,
                },
                new WebSocketOutboundEndpointConfig
                {
                    OutputId = 7,
                    Endpoint = new Uri("ws://localhost:6006/ws/"),
                    Mode = CommunicationMode.DuplexInbound,
                    DuplexInboundSourceEndpointId = 8,
                },
            ],
        };

        Assert.True(AgentEndpointsOptionsValidator.Validate(valid).IsValid);

        var invalid = new AgentEndpointsOptions
        {
            WebSocketOutbound =
            [
                new WebSocketOutboundEndpointConfig
                {
                    OutputId = 6,
                    Endpoint = new Uri("ws://localhost:6005/ws/"),
                    Mode = CommunicationMode.DuplexOutbound,
                },
                new WebSocketOutboundEndpointConfig
                {
                    OutputId = 7,
                    Endpoint = new Uri("ws://localhost:6006/ws/"),
                    Mode = CommunicationMode.DuplexInbound,
                },
            ],
        };

        var result = AgentEndpointsOptionsValidator.Validate(invalid);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("uses DuplexOutbound but has no SourceEndpointId configured"));
        Assert.Contains(result.Errors, e => e.Contains("uses DuplexInbound but has no DuplexInboundSourceEndpointId configured"));
    }

    [Fact]
    public void File_logical_bidirectional_pair_binds_and_validates()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Endpoints:FileInbound:0:SourceEndpointId"] = "7",
            ["Endpoints:FileInbound:0:Directory"] = "C:\\in",
            ["Endpoints:FileInbound:0:Mode"] = "DuplexInbound",
            ["Endpoints:FileInbound:0:LogicalEndpointId"] = "folder-pair-a",
            ["Endpoints:FileOutbound:0:OutputId"] = "8",
            ["Endpoints:FileOutbound:0:Directory"] = "C:\\out",
            ["Endpoints:FileOutbound:0:Mode"] = "DuplexOutbound",
            ["Endpoints:FileOutbound:0:LogicalEndpointId"] = "folder-pair-a",
        };

        var endpoints = BindEndpoints(settings);
        var result = AgentEndpointsOptionsValidator.Validate(endpoints);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
        Assert.Equal("folder-pair-a", Assert.Single(endpoints.FileInbound).LogicalEndpointId);
        Assert.Equal("folder-pair-a", Assert.Single(endpoints.FileOutbound).LogicalEndpointId);
    }

    [Fact]
    public void File_logical_bidirectional_pair_requires_matching_inbound_directory()
    {
        var endpoints = new AgentEndpointsOptions
        {
            FileOutbound =
            [
                new FileOutboundEndpointConfig
                {
                    OutputId = 8,
                    Directory = "C:\\out",
                    Mode = CommunicationMode.DuplexOutbound,
                    LogicalEndpointId = "folder-pair-a",
                },
            ],
        };

        var result = AgentEndpointsOptionsValidator.Validate(endpoints);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("no matching FileInbound endpoint is configured"));
    }

    [Fact]
    public void Endpoint_validator_rejects_directionally_invalid_modes()
    {
        var endpoints = new AgentEndpointsOptions
        {
            TcpInbound =
            [
                new Philips.IBE.IBEAgent.Endpoints.Tcp.TcpInboundOptions
                {
                    SourceEndpointId = 1,
                    Port = 6000,
                    Mode = CommunicationMode.Outbound,
                },
            ],
            TcpOutbound =
            [
                new TcpOutboundEndpointConfig
                {
                    OutputId = 2,
                    Host = "localhost",
                    Port = 6001,
                    Mode = CommunicationMode.Inbound,
                },
            ],
        };

        var result = AgentEndpointsOptionsValidator.Validate(endpoints);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("TcpInbound SourceEndpointId 1 uses invalid Mode 'Outbound'"));
        Assert.Contains(result.Errors, e => e.Contains("TcpOutbound OutputId 2 uses invalid Mode 'Inbound'"));
    }

    [Fact]
    public void Endpoint_validator_rejects_tcp_inbound_tls_without_server_certificate()
    {
        var endpoints = new AgentEndpointsOptions
        {
            TcpInbound =
            [
                new Philips.IBE.IBEAgent.Endpoints.Tcp.TcpInboundOptions
                {
                    SourceEndpointId = 1,
                    Port = 6000,
                    Ssl = new SslOptions { Enabled = true },
                },
            ],
        };

        var result = AgentEndpointsOptionsValidator.Validate(endpoints);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("enables TLS but has no local/server certificate reference"));
    }

    [Fact]
    public void Endpoint_validator_rejects_http_listener_tls_on_plain_prefix()
    {
        var endpoints = new AgentEndpointsOptions
        {
            HttpInbound =
            [
                new Philips.IBE.IBEAgent.Endpoints.Http.HttpInboundOptions
                {
                    SourceEndpointId = 1,
                    Prefix = "http://localhost:6000/ibe/",
                    Ssl = new SslOptions { Enabled = true },
                },
            ],
        };

        var result = AgentEndpointsOptionsValidator.Validate(endpoints);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("enables TLS but Prefix 'http://localhost:6000/ibe/' is not https://"));
    }

    [Fact]
    public void Endpoint_validator_rejects_client_certificate_requirement_without_trust_reference()
    {
        var endpoints = new AgentEndpointsOptions
        {
            HttpInbound =
            [
                new Philips.IBE.IBEAgent.Endpoints.Http.HttpInboundOptions
                {
                    SourceEndpointId = 1,
                    Prefix = "https://localhost:6000/ibe/",
                    Ssl = new SslOptions { RequireClientCertificate = true },
                },
            ],
        };

        var result = AgentEndpointsOptionsValidator.Validate(endpoints);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("requires client certificates but has no trusted certificate authority/reference configured"));
    }

    [Fact]
    public void Endpoint_validator_allows_inferred_outbound_mutual_tls_from_client_certificate()
    {
        var endpoints = new AgentEndpointsOptions
        {
            TcpOutbound =
            [
                new TcpOutboundEndpointConfig
                {
                    OutputId = 1,
                    Host = "localhost",
                    Port = 6000,
                    Ssl = new SslOptions
                    {
                        Enabled = true,
                        LocalCertificate = new CertificateReference
                        {
                            Kind = CertificateReferenceKind.File,
                            Path = "client.pfx",
                        },
                    },
                },
            ],
        };

        var result = AgentEndpointsOptionsValidator.Validate(endpoints);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
    }

    [Fact]
    public void Endpoint_validator_rejects_file_certificate_reference_without_path()
    {
        var endpoints = new AgentEndpointsOptions
        {
            TcpOutbound =
            [
                new TcpOutboundEndpointConfig
                {
                    OutputId = 1,
                    Host = "localhost",
                    Port = 6000,
                    Ssl = new SslOptions
                    {
                        Enabled = true,
                        LocalCertificate = new CertificateReference { Kind = CertificateReferenceKind.File },
                    },
                },
            ],
        };

        var result = AgentEndpointsOptionsValidator.Validate(endpoints);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("uses File reference but no Path or CertificatePath is configured"));
    }

    [Fact]
    public void Endpoint_validator_rejects_private_key_path_without_certificate_path()
    {
        var endpoints = new AgentEndpointsOptions
        {
            TcpOutbound =
            [
                new TcpOutboundEndpointConfig
                {
                    OutputId = 1,
                    Host = "localhost",
                    Port = 6000,
                    Ssl = new SslOptions
                    {
                        Enabled = true,
                        LocalCertificate = new CertificateReference
                        {
                            Kind = CertificateReferenceKind.File,
                            Path = "client.pfx",
                            PrivateKeyPath = "client.key",
                        },
                    },
                },
            ],
        };

        var result = AgentEndpointsOptionsValidator.Validate(endpoints);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("PrivateKeyPath but no CertificatePath"));
    }

    [Fact]
    public void Endpoint_validator_rejects_windows_store_reference_without_selector()
    {
        var endpoints = new AgentEndpointsOptions
        {
            TcpOutbound =
            [
                new TcpOutboundEndpointConfig
                {
                    OutputId = 1,
                    Host = "localhost",
                    Port = 6000,
                    Ssl = new SslOptions
                    {
                        Enabled = true,
                        LocalCertificate = new CertificateReference { Kind = CertificateReferenceKind.WindowsStore },
                    },
                },
            ],
        };

        var result = AgentEndpointsOptionsValidator.Validate(endpoints);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("WindowsStore reference but no Thumbprint, Subject, or FriendlyName selector"));
    }

    [Fact]
    public void Endpoint_validator_allows_mounted_secret_certificate_reference_with_certificate_path()
    {
        var endpoints = new AgentEndpointsOptions
        {
            TcpOutbound =
            [
                new TcpOutboundEndpointConfig
                {
                    OutputId = 1,
                    Host = "localhost",
                    Port = 6000,
                    Ssl = new SslOptions
                    {
                        Enabled = true,
                        LocalCertificate = new CertificateReference
                        {
                            Kind = CertificateReferenceKind.MountedSecret,
                            CertificatePath = "/var/run/secrets/client.crt",
                            PrivateKeyPath = "/var/run/secrets/client.key",
                        },
                    },
                },
            ],
        };

        var result = AgentEndpointsOptionsValidator.Validate(endpoints);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
    }

    private static AgentEndpointsOptions BindEndpoints(Dictionary<string, string?> settings)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build()
            .GetSection("Endpoints")
            .Get<AgentEndpointsOptions>()!;
}
