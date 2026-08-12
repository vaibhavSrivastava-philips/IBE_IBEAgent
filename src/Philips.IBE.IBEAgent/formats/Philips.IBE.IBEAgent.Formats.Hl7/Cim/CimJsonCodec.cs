using System.Text.Json;
using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Formats.Hl7.Cim;

public sealed class CimJsonCodec : IMessageCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    private readonly Hl7CimMapper _mapper;

    public CimJsonCodec(Hl7CimMapper? mapper = null)
    {
        _mapper = mapper ?? new Hl7CimMapper();
    }

    public ReadOnlyMemory<byte> Encode(MessageContext context)
    {
        var record = _mapper.Map(context.Payload);
        return JsonSerializer.SerializeToUtf8Bytes(record, JsonOptions);
    }
}
