namespace Philips.IBE.IBEAgent.Endpoints.Tcp;

internal static class Mllp
{
    public const byte StartBlock = 0x0B; // VT
    public const byte EndBlock1  = 0x1C; // FS
    public const byte EndBlock2  = 0x0D; // CR
}