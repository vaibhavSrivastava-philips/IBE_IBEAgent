namespace Philips.IBE.IBEAgent.Configuration;

// §8 — catalog-level structural validation: no duplicate names (guaranteed by dictionary key),
// codec Type must be non-empty, parallel-stage branches must be well-formed.
public static class CatalogOptionsValidator
{
    public static ValidationResult Validate(CatalogOptions catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var result = ValidationResult.Success();

        foreach (var (name, codec) in catalog.Codecs)
        {
            if (string.IsNullOrWhiteSpace(codec.Type))
            {
                result.AddError($"Catalog codec '{name}' must declare a non-empty Type.");
            }
        }

        foreach (var (name, stages) in catalog.Pipelines)
        {
            if (stages.Count == 0)
            {
                result.AddError($"Catalog pipeline '{name}' must declare at least one stage.");
                continue;
            }

            foreach (var stage in stages)
            {
                switch (stage)
                {
                    case string stageName when string.IsNullOrWhiteSpace(stageName):
                        result.AddError($"Catalog pipeline '{name}' contains a blank stage name.");
                        break;
                    case string:
                        break;
                    case ParallelStageOptions parallel:
                        ValidateParallel(name, parallel, result);
                        break;
                    default:
                        result.AddError($"Catalog pipeline '{name}' has an unrecognized stage entry of type '{stage.GetType().Name}'.");
                        break;
                }
            }
        }

        return result;
    }

    private static void ValidateParallel(string pipelineName, ParallelStageOptions parallel, ValidationResult result)
    {
        if (parallel.Branches.Count < 2)
        {
            result.AddError($"Catalog pipeline '{pipelineName}' parallel stage must declare at least two branches.");
        }

        foreach (var branch in parallel.Branches)
        {
            if (branch.Count == 0)
            {
                result.AddError($"Catalog pipeline '{pipelineName}' parallel stage has an empty branch.");
            }
        }

        if (!string.Equals(parallel.Join, "all", StringComparison.OrdinalIgnoreCase))
        {
            result.AddError($"Catalog pipeline '{pipelineName}' parallel stage Join '{parallel.Join}' is not supported (only 'all').");
        }

        if (!string.Equals(parallel.OnError, "failFast", StringComparison.OrdinalIgnoreCase))
        {
            result.AddError($"Catalog pipeline '{pipelineName}' parallel stage OnError '{parallel.OnError}' is not supported (only 'failFast').");
        }
    }
}
