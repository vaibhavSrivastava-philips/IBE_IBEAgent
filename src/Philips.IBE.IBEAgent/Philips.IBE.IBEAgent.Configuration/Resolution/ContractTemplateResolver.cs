namespace Philips.IBE.IBEAgent.Configuration;

// §8 — flattens an FSE contract against the developer-owned catalog: resolves the Workflow (shared
// Pipeline + default per-leg Format) and any per-Output Format override into concrete values, so
// everything downstream (validators, ContractCompiler, ComponentRegistry) sees fully-resolved
// Encoding / batch codec / Pipeline. Only developer/code concerns are inherited from the catalog;
// message-level/operational settings (Acknowledgement, Retry, DeliveryGuarantee, Channel, batch
// triggers) pass through untouched. Idempotent: the returned contract carries no Workflow/Format refs.
public static class ContractWorkflowResolver
{
    public static ContractOptions Resolve(ContractOptions contract, CatalogOptions catalog)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(catalog);

        ContractWorkflowOptions? workflow = null;
        if (!string.IsNullOrWhiteSpace(contract.Workflow)
            && !catalog.Workflows.TryGetValue(contract.Workflow, out workflow))
        {
            throw new ContractResolutionException(contract.Name, $"references unknown Workflow '{contract.Workflow}'.");
        }

        // Pipeline: a referenced Workflow supplies the shared stages; otherwise the manual/legacy
        // Contract.Pipeline is honored (escape hatch for workflow-less contracts).
        var pipeline = workflow is not null ? workflow.Pipeline : contract.Pipeline;

        var workflowFormat = ResolveFormat(contract, catalog, workflow?.Format, "Workflow");
        var outputs = (contract.Outputs ?? [])
            .Select(output => ResolveOutput(contract, catalog, output, workflowFormat))
            .ToList();

        return contract with
        {
            Workflow = null,        // flattened away
            Pipeline = pipeline,
            // ReplyOnFilter: FSE contract override wins; else the developer Workflow default; else silent drop.
            ReplyOnFilter = contract.ReplyOnFilter ?? workflow?.ReplyOnFilter ?? false,
            Outputs = outputs,
        };
    }

    private static OutputOptions ResolveOutput(
        ContractOptions contract, CatalogOptions catalog, OutputOptions output, OutputFormatOptions? workflowFormat)
    {
        // Per-leg Format override (developer-named) wins over the workflow default.
        var legFormat = !string.IsNullOrWhiteSpace(output.Format)
            ? ResolveFormat(contract, catalog, output.Format, $"Output {output.OutputId}")
            : workflowFormat;

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
