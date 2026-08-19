namespace Philips.IBE.IBEAgent.Core;

// §3.10 — module-owned stage registration. Core owns the names and factories of its generic stages
// here, so the host composition root stays thin and adding a Core stage never edits the host (OCP).
// Protocol modules (e.g. Formats.Hl7) expose their own AddHl7Stages() the same way when they gain
// stages (generic stages live in Core; protocol-specific extractors live in the protocol module).
public static class CoreComponentRegistrations
{
    public static ComponentRegistry AddCoreStages(this ComponentRegistry registry, IReadOnlyDictionary<string, string>? mediaTypeMap = null)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.RegisterStage(PassThroughStage.Name, _ => new PassThroughStage());
        registry.RegisterStage(BlobEnvelopeExtractStage.Name, _ => new BlobEnvelopeExtractStage());

        // The extension -> media-type map is developer config (catalog "MediaTypes"); an empty map makes
        // the stage a no-op so nothing is hardwired into the classifier.
        var mediaTypes = mediaTypeMap ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        registry.RegisterStage(MediaTypeClassifierStage.Name, _ => new MediaTypeClassifierStage(mediaTypes));

        return registry;
    }
}
