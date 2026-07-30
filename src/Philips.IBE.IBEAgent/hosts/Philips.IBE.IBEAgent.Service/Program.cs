// MAIN IBE Agent host (Windows service "Philips.IBE.Agent").
// Composition root: build config -> compile IContractRuntimes + legs -> register endpoints -> run.
// See docs/architecture/Refactor_ArchitectureDoc_v4.md §3.10/§14.
using NLog.Extensions.Logging;
using Philips.IBE.IBEAgent.Service;

// Content root = the exe's directory so config resolves consistently in dev and as a Windows
// service (whose working directory is not the install folder). The shared /config files are
// copied next to the exe by the csproj.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// catalogData.json (developer-owned: pipelines/codecs) and contractData.json (FSE-owned:
// communication endpoints + contract topology) layer on top of appsettings.json.
builder.Configuration
    .AddJsonFile("catalogData.json", optional: true, reloadOnChange: true)
    .AddJsonFile("contractData.json", optional: true, reloadOnChange: true);

// Route all Microsoft.Extensions.Logging output through NLog (targets/rules in nlog.config next to the exe).
builder.Logging.ClearProviders();
builder.Logging.AddNLog();

builder.Services.AddWindowsService(options => options.ServiceName = "Philips.IBE.Agent");

builder.Services.AddIbeAgentEngine(builder.Configuration);

var host = builder.Build();
host.Run();
