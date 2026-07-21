// MAIN IBE Agent host (Windows service "Philips.IBE.Agent").
// Composition root: build config -> compile IContractRuntimes + legs -> register endpoints -> run.
// TODO: wire the engine (Dispatcher, Router, ContractRegistry, ContractRuntimes, ForwardWorker).
// See docs/architecture/target-architecture-v3.md §10.

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options => options.ServiceName = "Philips.IBE.Agent");

// TODO: builder.Services.AddIbeAgentEngine(builder.Configuration);

var host = builder.Build();
host.Run();
