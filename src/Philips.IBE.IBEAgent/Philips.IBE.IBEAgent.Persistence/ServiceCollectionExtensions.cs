using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Configuration;
using Philips.IBE.IBEAgent.Security;

namespace Philips.IBE.IBEAgent.Persistence;

// §3.9/§3.10 — composition helper shared by both hosting modes: registers the store, the
// replay-target registry, and the ForwardWorker BackgroundService. The in-process host
// (Philips.IBE.IBEAgent.Service) supplies the compiled legs' replay targets; the out-of-process
// host (Philips.IBE.IBEAgent.ForwardService) supplies an empty set until it composes its own
// endpoints (Phase 7+).
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddForwardStore(this IServiceCollection services)
    {
        services.AddSingleton(DataProtectorFactory.Create());
        services.AddSingleton<IForwardStore, InMemoryForwardStore>();
        return services;
    }

    public static IServiceCollection AddForwardWorker(
        this IServiceCollection services,
        IConfiguration configuration,
        IEnumerable<KeyValuePair<int, IReplayTarget>> replayTargets)
    {
        services.Configure<ForwardOptions>(configuration.GetSection("Ibe:Forward").Bind);
        services.AddSingleton<IReplayTargetRegistry>(new ReplayTargetRegistry(replayTargets));
        services.AddHostedService<ForwardWorker>();
        return services;
    }
}
