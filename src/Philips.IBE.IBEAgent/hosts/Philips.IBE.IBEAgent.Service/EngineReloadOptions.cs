namespace Philips.IBE.IBEAgent.Service;

public sealed class EngineReloadOptions
{
    public bool Enabled { get; init; }
    public int DebounceMilliseconds { get; init; } = 500;
}
