using System.Text.Json;
using System.Text.Json.Serialization;

namespace IbePerf;

// ---- Topology (parsed from the live config/contractData.json) ----------------------------------

public sealed record PerfEndpoint(string Proto, string Role, int Id, string Host, int Port, string? Url, string Format);

public sealed class Topology
{
    public List<PerfEndpoint> Inbound { get; } = new();
    public List<PerfEndpoint> Outbound { get; } = new();
    public List<ContractRef> Contracts { get; } = new();

    public sealed record ContractRef(string Name, List<int> Inputs, List<int> Outputs);

    public PerfEndpoint? InboundById(int id) => Inbound.Find(e => e.Id == id);

    public static Topology Load(string path)
    {
        var t = new Topology();
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;

        if (root.TryGetProperty("Endpoints", out var eps))
        {
            foreach (var e in Array(eps, "TcpInbound"))
                t.Inbound.Add(new PerfEndpoint("tcp", "in", GetInt(e, "SourceEndpointId"), "127.0.0.1", GetInt(e, "Port"), null, GetStr(e, "Format", "hl7v2")));

            foreach (var e in Array(eps, "HttpInbound"))
            {
                var prefix = GetStr(e, "Prefix", "");
                var (host, port) = ParseHostPort(prefix);
                t.Inbound.Add(new PerfEndpoint("http", "in", GetInt(e, "SourceEndpointId"), host, port, prefix, GetStr(e, "Format", "hl7v2")));
            }

            foreach (var e in Array(eps, "TcpOutbound"))
                t.Outbound.Add(new PerfEndpoint("tcp", "out", GetInt(e, "OutputId"), GetStr(e, "Host", "127.0.0.1"), GetInt(e, "Port"), null, "hl7v2"));

            foreach (var e in Array(eps, "HttpOutbound"))
            {
                var url = GetStr(e, "Endpoint", "");
                var (host, port) = ParseHostPort(url);
                t.Outbound.Add(new PerfEndpoint("http", "out", GetInt(e, "OutputId"), host, port, url, "hl7v2"));
            }
        }

        if (root.TryGetProperty("Contracts", out var contracts) && contracts.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in contracts.EnumerateArray())
            {
                var inputs = new List<int>();
                if (c.TryGetProperty("Inputs", out var ins) && ins.ValueKind == JsonValueKind.Array)
                    foreach (var i in ins.EnumerateArray())
                        inputs.Add(GetInt(i, "InputId"));

                var outputs = new List<int>();
                if (c.TryGetProperty("Outputs", out var outs) && outs.ValueKind == JsonValueKind.Array)
                    foreach (var o in outs.EnumerateArray())
                        outputs.Add(GetInt(o, "OutputId"));

                t.Contracts.Add(new ContractRef(GetStr(c, "Name", "contract"), inputs, outputs));
            }
        }

        return t;
    }

    private static IEnumerable<JsonElement> Array(JsonElement parent, string name)
    {
        if (parent.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var e in arr.EnumerateArray())
                yield return e;
    }

    private static int GetInt(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : 0;
    private static string GetStr(JsonElement e, string name, string dflt) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? dflt) : dflt;

    private static (string Host, int Port) ParseHostPort(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var u)) return (u.Host, u.Port);
        return ("localhost", 0);
    }
}

// ---- Scenario config (written by the runner, read by load + sink) ------------------------------

public sealed record MessageMix
{
    public string Type { get; init; } = "ADT^A01";
    public int SizeBytes { get; init; }
    public double WeightPct { get; init; } = 100;
}

public sealed record FailureCfg
{
    public int CloseAfterN { get; init; }
    public double NackPct { get; init; }
    public int IdleCloseMs { get; init; }
    public bool Refuse { get; init; }
    public bool ResetMidFrame { get; init; }
}

public sealed record SinkCfg
{
    public int AckDelayMs { get; init; }
    public int JitterMs { get; init; }
    public FailureCfg Failure { get; init; } = new();
}

public sealed record ScenarioCfg
{
    public string Name { get; init; } = "scenario";
    public string Mode { get; init; } = "closed";  // closed | open
    public int Connections { get; init; } = 1;
    public int DurationSec { get; init; } = 20;
    public int WarmupSec { get; init; } = 5;
    public double RateMsgsPerSec { get; init; }     // open-loop target; 0 = unbounded
    public int? InputId { get; init; }
    public int IdleGapSec { get; init; }
    public int BurstSize { get; init; }

    [JsonConverter(typeof(SingleOrArrayConverter<MessageMix>))]
    public List<MessageMix> MessageMix { get; init; } = new();

    public SinkCfg Sink { get; init; } = new();

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public static ScenarioCfg Load(string path) =>
        JsonSerializer.Deserialize<ScenarioCfg>(File.ReadAllText(path), JsonOpts)
        ?? throw new InvalidOperationException($"could not parse scenario '{path}'");
}

// Tolerates PowerShell's single-element-array unwrapping (a lone MessageMix serialized as an object)
// as well as a normal JSON array.
internal sealed class SingleOrArrayConverter<T> : JsonConverter<List<T>>
{
    public override List<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var list = new List<T>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                var item = JsonSerializer.Deserialize<T>(ref reader, options);
                if (item is not null) list.Add(item);
            }
            return list;
        }
        var one = JsonSerializer.Deserialize<T>(ref reader, options);
        return one is null ? new List<T>() : new List<T> { one };
    }

    public override void Write(Utf8JsonWriter writer, List<T> value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, options);
}
