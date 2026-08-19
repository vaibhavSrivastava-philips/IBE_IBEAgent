using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Configuration;

namespace Philips.IBE.IBEAgent.Core;

// §3.10 — name/type-keyed factories for the pluggable building blocks (OCP): stages, codecs,
// outbound endpoints. Distinct from the Contract Registry (compiled contracts) and the config
// Catalog (named DTOs) — this is the "name -> real instance" resolver used by the compiler.
public sealed class ComponentRegistry
{
    private readonly Dictionary<string, Func<StageParameters, IMessageStage>> _stages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<CodecOptions, IMessageCodec>> _messageCodecs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<CodecOptions, IBatchCodec>> _batchCodecs = new(StringComparer.Ordinal);
    private readonly Dictionary<int, Func<OutputOptions, IOutboundEndpoint>> _endpointFactories = new();
    private readonly Dictionary<(string Format, AckShape Shape), IAckFormatter> _ackFormatters = new();
    private readonly List<IEndpointLifecycle> _outboundEndpointLifecycles = [];

    public IReadOnlyList<IEndpointLifecycle> OutboundEndpointLifecycles => _outboundEndpointLifecycles;

    public ComponentRegistry RegisterStage(string name, Func<StageParameters, IMessageStage> factory)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(factory);
        _stages[name] = factory;
        return this;
    }

    public ComponentRegistry RegisterMessageCodec(string type, Func<CodecOptions, IMessageCodec> factory)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        ArgumentNullException.ThrowIfNull(factory);
        _messageCodecs[type] = factory;
        return this;
    }

    public ComponentRegistry RegisterBatchCodec(string type, Func<CodecOptions, IBatchCodec> factory)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        ArgumentNullException.ThrowIfNull(factory);
        _batchCodecs[type] = factory;
        return this;
    }

    // Endpoint construction is transport-specific (Tcp/Http/File live in their own projects, never
    // referenced by Core) so callers register a factory keyed by OutputId at composition time.
    public ComponentRegistry RegisterOutboundEndpoint(int outputId, Func<OutputOptions, IOutboundEndpoint> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _endpointFactories[outputId] = factory;
        return this;
    }

    // §3.8/§6 — keyed by (Format x Shape). Formats.* plug-ins register their generated-ack renderer
    // here; the resolver looks up by the source's own Format tag and the contract's configured Shape.
    public ComponentRegistry RegisterAckFormatter(IAckFormatter formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        _ackFormatters[(formatter.Format, formatter.Shape)] = formatter;
        return this;
    }

    public bool TryGetAckFormatter(string format, AckShape shape, out IAckFormatter? formatter)
        => _ackFormatters.TryGetValue((format, shape), out formatter);

    public IMessageStage CreateStage(string name, StageParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (!_stages.TryGetValue(name, out var factory))
            throw new InvalidOperationException($"No stage registered with name '{name}'.");
        return factory(parameters);
    }

    public IMessageCodec CreateMessageCodec(string name, CodecOptions options)
    {
        if (!_messageCodecs.TryGetValue(options.Type, out var factory))
            throw new InvalidOperationException($"No message codec registered for Type '{options.Type}' (referenced as '{name}').");
        return factory(options);
    }

    public IBatchCodec CreateBatchCodec(string name, CodecOptions options)
    {
        if (!_batchCodecs.TryGetValue(options.Type, out var factory))
            throw new InvalidOperationException($"No batch codec registered for Type '{options.Type}' (referenced as '{name}').");
        return factory(options);
    }

    public IOutboundEndpoint CreateOutboundEndpoint(OutputOptions output)
    {
        if (!_endpointFactories.TryGetValue(output.OutputId, out var factory))
            throw new InvalidOperationException($"No outbound endpoint registered for OutputId {output.OutputId}.");
        var endpoint = factory(output);
        if (endpoint is IEndpointLifecycle lifecycle)
            _outboundEndpointLifecycles.Add(lifecycle);
        return endpoint;
    }
}
