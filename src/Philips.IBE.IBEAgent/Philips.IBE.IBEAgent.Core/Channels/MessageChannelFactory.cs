using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Configuration;

namespace Philips.IBE.IBEAgent.Core;

public sealed class MessageChannelFactory(string durableRootDirectory)
{
    public IMessageChannel Create(ChannelOptions options, string scopeName, bool durable)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeName);

        if (durable || options.OverflowPolicy == OverflowPolicy.SpillToDisk)
        {
            var directory = Path.Combine(durableRootDirectory, Sanitize(scopeName));
            return new DurableMessageChannel(options.Capacity, directory);
        }

        return new BoundedInMemoryChannel(options.Capacity, options.OverflowPolicy);
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(ch => invalid.Contains(ch) ? '_' : ch));
    }
}
