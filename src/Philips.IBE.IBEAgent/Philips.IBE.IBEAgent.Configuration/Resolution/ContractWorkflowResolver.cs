namespace Philips.IBE.IBEAgent.Configuration;

// §8 — flattens an FSE contract against the developer-owned catalog: resolves the Workflow (shared
// Pipeline + default per-leg Format) and any per-Output Format override into concrete values, so
// everything downstream (validators, ContractCompiler, ComponentRegistry) sees fully-resolved
// Encoding / batch codec / Pipeline. Only developer/code concerns are inherited from the catalog;
// message-level/operational settings (Acknowledgement, Retry, DeliveryGuarantee, Channel, batch
// triggers) pass through untouched. Idempotent: the returned contract carries no Workflow/Format refs.
public static class ContractWorkflowResolver
{
    public static ContractOptions Resolve(ContractOptions contract, CatalogOptions catalog, Action<string>? onNote = null, ResourceResolver? resources = null)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(catalog);

        var workflowName = contract.Workflow?.Use;
        ContractWorkflowOptions? workflow = null;
        if (!string.IsNullOrWhiteSpace(workflowName)
            && !catalog.Workflows.TryGetValue(workflowName, out workflow))
        {
            throw new ContractResolutionException(contract.Name, $"references unknown Workflow '{workflowName}'.");
        }

        // Validate the FSE Settings bag against the Workflow's declared Settings and bind each value onto
        // the contract (ReplyOnFilter is now just another bindable field). stage: binds collect into the
        // per-stage parameters threaded to PipelineBuilder; Kind:file/secret resolve via the resources context.
        var stageParameters = SettingBinder.Apply(contract, workflow, resources);

        // Pipeline: a referenced Workflow supplies the shared stages; otherwise the manual/legacy
        // Contract.Pipeline is honored (escape hatch for workflow-less contracts).
        var pipeline = workflow is not null ? workflow.Pipeline : contract.Pipeline;

        // A Workflow may declare a single Format (shorthand) or an ordered Formats set. [0] is the
        // default every leg inherits; when the set has >1, an output picks one (Output.Format, which
        // must be a member) and an output that omits it falls back to [0] (reported via onNote).
        var explicitSet = workflow?.Formats is { Count: > 0 };
        IReadOnlyList<string> formatSet;
        if (workflow?.Formats is { Count: > 0 } declared)
            formatSet = declared;
        else if (!string.IsNullOrWhiteSpace(workflow?.Format))
            formatSet = [workflow!.Format!];
        else
            formatSet = [];

        var defaultFormatName = formatSet.Count > 0 ? formatSet[0] : null;
        var defaultFormat = ResolveFormat(contract, catalog, defaultFormatName, "Workflow");

        var outputs = (contract.Outputs ?? [])
            .Select(output => ResolveOutput(
                contract, catalog, output, formatSet, explicitSet, defaultFormat, defaultFormatName, onNote))
            .ToList();

        return contract with
        {
            Workflow = null,        // flattened away
            Pipeline = pipeline,
            ReplyOnFilter = contract.ReplyOnFilter ?? false,
            Outputs = outputs,
            StageParameterSets = stageParameters.Count > 0 ? stageParameters : null,
        };
    }

    private static OutputOptions ResolveOutput(
        ContractOptions contract, CatalogOptions catalog, OutputOptions output,
        IReadOnlyList<string> formatSet, bool explicitSet, OutputFormatOptions? defaultFormat,
        string? defaultFormatName, Action<string>? onNote)
    {
        OutputFormatOptions? legFormat;
        if (!string.IsNullOrWhiteSpace(output.Format))
        {
            // FSE picked a per-leg Format. When the Workflow declares an explicit Formats set, the pick
            // must be one of the declared entries (least-privilege menu); otherwise any catalog Format is
            // allowed (single-Format shorthand / manual mode keep the legacy escape hatch).
            if (explicitSet && !formatSet.Contains(output.Format))
            {
                throw new ContractResolutionException(contract.Name,
                    $"Output {output.OutputId} Format '{output.Format}' is not one of the workflow's declared Formats: [{string.Join(", ", formatSet)}].");
            }

            legFormat = ResolveFormat(contract, catalog, output.Format, $"Output {output.OutputId}");
        }
        else
        {
            // No per-leg pick: inherit the default. When the Workflow declares >1 Format the FSE was
            // expected to choose, so note the fallback (the caller decides how to surface it).
            legFormat = defaultFormat;
            if (formatSet.Count > 1)
            {
                onNote?.Invoke(
                    $"Contract '{contract.Name}' Output {output.OutputId} did not specify a Format; workflow declares {formatSet.Count} formats — defaulting to '{defaultFormatName}'.");
            }
        }

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
