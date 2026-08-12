using Microsoft.Extensions.Logging;
using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.Formats.Hl7.Filtering;

namespace Philips.IBE.IBEAgent.Formats.Hl7;

// §3.10 — module-owned stage registration for the HL7 format module (mirrors Core's AddCoreStages).
// Called from the host's ComponentRegistryBuilder so adding an HL7 stage never edits Core or the host.
// The logger is created once and captured by the factory (ILogger is reusable/thread-safe).
public static class Hl7ComponentRegistrations
{
    public static ComponentRegistry AddHl7Stages(this ComponentRegistry registry, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var classifyLogger = loggerFactory.CreateLogger<Hl7ClassifyStage>();
        registry.RegisterStage(Hl7ClassifyStage.Name, () => new Hl7ClassifyStage(classifyLogger));
        var filterLogger = loggerFactory.CreateLogger<Hl7FilterStage>();
        registry.RegisterStage(Hl7FilterStage.Name, () => new Hl7FilterStage(logger: filterLogger));

        return registry;
    }
}
