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
// host (Philips.IBE.IBEAgent.ForwardService) supplies the replay targets from its own composition root.
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddForwardStore(this IServiceCollection services)
    {
        var protector = DataProtectorFactory.Create();
        var store = new InMemoryForwardStore(protector);

        services.AddSingleton(protector);
        services.AddSingleton<IForwardStore>(store);
        services.AddSingleton<ForwardWorkerHealthReporter>();
        services.AddSingleton<IHealthReporter>(sp => sp.GetRequiredService<ForwardWorkerHealthReporter>());
        return services;
    }

    public static IServiceCollection AddForwardWorker(
        this IServiceCollection services,
        IConfiguration configuration,
        IEnumerable<KeyValuePair<int, IReplayTarget>> replayTargets)
    {
        services.Configure<ForwardOptions>(configuration.GetSection("Forward").Bind);
        services.TryAddSingleton<ForwardWorkerHealthReporter>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthReporter, ForwardWorkerHealthReporter>());
        var replayTargetList = replayTargets.ToList();
        services.AddSingleton<IReplayTargetRegistry>(_ => new ReplayTargetRegistry(replayTargetList));
        services.AddHostedService<ForwardWorker>();
        return services;
    }

    // Deferred variant: the in-process host compiles its legs lazily (so the ComponentRegistry can
    // use the host's real ILoggerFactory), so the replay-target set is built from the provider at
    // start rather than captured at registration time.
    public static IServiceCollection AddForwardWorker(
        this IServiceCollection services,
        IConfiguration configuration,
        Func<IServiceProvider, IEnumerable<KeyValuePair<int, IReplayTarget>>> replayTargetsFactory)
    {
        services.Configure<ForwardOptions>(configuration.GetSection("Forward").Bind);
        services.TryAddSingleton<ForwardWorkerHealthReporter>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthReporter, ForwardWorkerHealthReporter>());
        services.AddSingleton<IReplayTargetRegistry>(sp => new ReplayTargetRegistry(replayTargetsFactory(sp)));
        services.AddHostedService<ForwardWorker>();
        return services;
    }
}
