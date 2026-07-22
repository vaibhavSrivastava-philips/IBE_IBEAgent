namespace Philips.IBE.IBEAgent.Abstractions;

public interface IMessageCodec             // per-message: canonical model -> destination wire bytes.
{
    ReadOnlyMemory<byte> Encode(MessageContext context);
}