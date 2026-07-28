// MAIN IBE Agent host (Windows service "Philips.IBE.Agent").
// Composition root: build config -> compile IContractRuntimes + legs -> register endpoints -> run.
// See docs/architecture/Refactor_ArchitectureDoc_v4.md §3.10/§14.
using Philips.IBE.IBEAgent.Service;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options => options.ServiceName = "Philips.IBE.Agent");

builder.Services.AddIbeAgentEngine(builder.Configuration);

var host = builder.Build();
host.Run();
