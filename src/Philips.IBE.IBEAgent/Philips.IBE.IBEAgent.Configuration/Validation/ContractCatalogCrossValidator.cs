namespace Philips.IBE.IBEAgent.Configuration;

// §8 — cross-reference validation between a Contract and the Catalog it draws names from:
// Pipeline name, Output.Encoding, and Output.Batching.Codec must all resolve. This is the last
// structural gate before ContractCompiler starts constructing real components (Core).
public static class ContractCatalogCrossValidator
{
    public static ValidationResult Validate(ContractOptions contract, CatalogOptions catalog)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(catalog);
        var result = ValidationResult.Success();

        if (!string.IsNullOrWhiteSpace(contract.Pipeline) && !catalog.Pipelines.ContainsKey(contract.Pipeline))
        {
            result.AddError($"Contract '{contract.Name}' references unknown Pipeline '{contract.Pipeline}'.");
        }

        foreach (var output in contract.Outputs ?? [])
        {
            if (string.IsNullOrWhiteSpace(output.Encoding))
            {
                result.AddError($"Contract '{contract.Name}' Output {output.OutputId} has no resolved Encoding (set an Output.Encoding, an Output.Format, or a Workflow with a Format).");
            }
            else if (!catalog.Codecs.ContainsKey(output.Encoding))
            {
                result.AddError($"Contract '{contract.Name}' Output {output.OutputId} references unknown Encoding codec '{output.Encoding}'.");
            }

            if (output.Batching is { Enabled: true } batching)
            {
                if (string.IsNullOrWhiteSpace(batching.Codec))
                {
                    result.AddError($"Contract '{contract.Name}' Output {output.OutputId} enables Batching but has no resolved batch Codec (set Batching.Codec or a Format with a BatchCodec).");
                }
                else if (!catalog.Codecs.ContainsKey(batching.Codec))
                {
                    result.AddError($"Contract '{contract.Name}' Output {output.OutputId} references unknown Batching.Codec '{batching.Codec}'.");
                }
            }
        }

        return result;
    }
}
