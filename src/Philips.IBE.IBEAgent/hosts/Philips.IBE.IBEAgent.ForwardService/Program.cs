// Store-and-forward host (Windows service "Philips.IBE.Forward").
// Out-of-process owner of the ForwardWorker: drains IForwardStore (Pending) and re-delivers
// through the SAME IOutboundEndpoint + codec the engine uses. No duplicate senders.
// See docs/architecture/target-architecture-v3.md §3.9.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using Philips.IBE.IBEAgent.ForwardService;

var builder = Host.CreateApplicationBuilder(args);

// Route all Microsoft.Extensions.Logging output through NLog (targets/rules in nlog.config next to the exe).
builder.Logging.ClearProviders();
builder.Logging.AddNLog();

builder.Services.AddWindowsService(options => options.ServiceName = "Philips.IBE.Forward");

builder.Services.AddForwardService(builder.Configuration);

var host = builder.Build();

// Fatal startup/runtime failures crash the process by design (fail-fast). Log them Critical first so
// the reason survives in ops before exit.
var logger = host.Services.GetRequiredService<ILogger<Program>>();
try
{
    host.Run();
}
catch (Exception ex)
{
    logger.LogCritical(ex, "ForwardService host terminated unexpectedly.");
    throw;
}
