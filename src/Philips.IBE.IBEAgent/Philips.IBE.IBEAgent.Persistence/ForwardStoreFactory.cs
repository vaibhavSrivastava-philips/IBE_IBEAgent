using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Configuration;
using Philips.IBE.IBEAgent.Security;

namespace Philips.IBE.IBEAgent.Persistence;

public static class ForwardStoreFactory
{
    public static (IForwardStore Store, IForwardStoreManagement Management) Create(ForwardOptions options, IDataProtector protector)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(protector);

        var lease = TimeSpan.FromSeconds(Math.Max(1, options.LeaseSeconds));
        return options.Store switch
        {
            ForwardStoreKind.InMemory => CreateInMemory(protector, lease),
            _ => CreateFile(options, protector, lease),
        };
    }

    private static (IForwardStore Store, IForwardStoreManagement Management) CreateInMemory(IDataProtector protector, TimeSpan lease)
    {
        var store = new InMemoryForwardStore(protector, lease);
        return (store, store);
    }

    private static (IForwardStore Store, IForwardStoreManagement Management) CreateFile(ForwardOptions options, IDataProtector protector, TimeSpan lease)
    {
        var store = new FileForwardStore(options.StoreDirectory, protector, lease);
        return (store, store);
    }
}
