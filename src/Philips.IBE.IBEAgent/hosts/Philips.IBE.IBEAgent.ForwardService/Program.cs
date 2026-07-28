// Store-and-forward host (Windows service "Philips.IBE.Forward").
// Out-of-process owner of the ForwardWorker: drains IForwardStore (Pending) and re-delivers
// through the SAME IOutboundEndpoint + codec the engine uses. No duplicate senders.
// See docs/architecture/target-architecture-v3.md §3.9.

using Philips.IBE.IBEAgent.ForwardService;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options => options.ServiceName = "Philips.IBE.Forward");

builder.Services.AddForwardService(builder.Configuration);

var host = builder.Build();
host.Run();
