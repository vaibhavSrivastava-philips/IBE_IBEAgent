namespace Philips.IBE.IBEAgent.Configuration;

// §8 — flattens an FSE contract against the developer-owned catalog: resolves the Template (shared
// Pipeline + default per-leg Format) and any per-Output Format override into concrete values, so
// everything downstream (validators, ContractCompiler, ComponentRegistry) sees fully-resolved
// Encoding / batch codec / Pipeline. Only developer/code concerns are inherited from the catalog;
// message-level/operational settings (Acknowledgement, Retry, DeliveryGuarantee, Channel, batch
// triggers) pass through untouched. Idempotent: the returned contract carries no Template/Format refs.
public static class ContractTemplateResolver
{
    public static ContractOptions Resolve(ContractOptions contract, CatalogOptions catalog)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(catalog);

        ContractTemplateOptions? template = null;
        if (!string.IsNullOrWhiteSpace(contract.Template)
            && !catalog.Templates.TryGetValue(contract.Template, out template))
        {
            throw new ContractResolutionException(contract.Name, $"references unknown Template '{contract.Template}'.");
        }

        // Pipeline: a referenced Template supplies the shared stages; otherwise the manual/legacy
        // Contract.Pipeline is honored (escape hatch for template-less contracts).
        var pipeline = template is not null ? template.Pipeline : contract.Pipeline;

        var templateFormat = ResolveFormat(contract, catalog, template?.Format, "Template");
        var outputs = (contract.Outputs ?? [])
            .Select(output => ResolveOutput(contract, catalog, output, templateFormat))
            .ToList();

        return contract with
        {
            Template = null,        // flattened away
            Pipeline = pipeline,
            // ReplyOnFilter: FSE contract override wins; else the developer Template default; else silent drop.
            ReplyOnFilter = contract.ReplyOnFilter ?? template?.ReplyOnFilter ?? false,
            Outputs = outputs,
        };
    }

    private static OutputOptions ResolveOutput(
        ContractOptions contract, CatalogOptions catalog, OutputOptions output, OutputFormatOptions? templateFormat)
    {
        // Per-leg Format override (developer-named) wins over the template default.
        var legFormat = !string.IsNullOrWhiteSpace(output.Format)
            ? ResolveFormat(contract, catalog, output.Format, $"Output {output.OutputId}")
            : templateFormat;

        // Encoding precedence: inline codec-name override -> Format codec (may stay null; the
        // cross-validator flags an unresolved encoding).
        var encoding = output.Encoding ?? legFormat?.Codec;

        var batching = output.Batching;
        if (batching is not null)
        {
            batching = batching with { Codec = batching.Codec ?? legFormat?.BatchCodec };
        }

        return output with
        {
            Format = null,          // flattened away
            Encoding = encoding,
            Batching = batching,
        };
    }

    private static OutputFormatOptions? ResolveFormat(
        ContractOptions contract, CatalogOptions catalog, string? formatName, string subject)
    {
        if (string.IsNullOrWhiteSpace(formatName))
        {
            return null;
        }

        if (!catalog.Formats.TryGetValue(formatName, out var format))
        {
            throw new ContractResolutionException(contract.Name, $"{subject} references unknown Format '{formatName}'.");
        }

        return format;
    }
}
