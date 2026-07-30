namespace Philips.IBE.IBEAgent.Configuration;

// §8 — catalog-level structural validation: no duplicate names (guaranteed by dictionary key),
// codec Type must be non-empty, every pipeline declares at least one non-blank stage name.
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

        foreach (var (name, format) in catalog.Formats)
        {
            if (string.IsNullOrWhiteSpace(format.Codec))
            {
                result.AddError($"Catalog format '{name}' must declare a non-empty Codec.");
            }
            else if (!catalog.Codecs.ContainsKey(format.Codec))
            {
                result.AddError($"Catalog format '{name}' references unknown Codec '{format.Codec}'.");
            }

            if (!string.IsNullOrWhiteSpace(format.BatchCodec) && !catalog.Codecs.ContainsKey(format.BatchCodec))
            {
                result.AddError($"Catalog format '{name}' references unknown BatchCodec '{format.BatchCodec}'.");
            }
        }

        foreach (var (name, template) in catalog.Templates)
        {
            if (!string.IsNullOrWhiteSpace(template.Pipeline) && !catalog.Pipelines.ContainsKey(template.Pipeline))
            {
                result.AddError($"Catalog template '{name}' references unknown Pipeline '{template.Pipeline}'.");
            }

            if (!string.IsNullOrWhiteSpace(template.Format) && !catalog.Formats.ContainsKey(template.Format))
            {
                result.AddError($"Catalog template '{name}' references unknown Format '{template.Format}'.");
            }
        }

        foreach (var (name, stages) in catalog.Pipelines)
        {
            if (stages.Count == 0)
            {
                result.AddError($"Catalog pipeline '{name}' must declare at least one stage.");
                continue;
            }

            foreach (var stageName in stages)
            {
                if (string.IsNullOrWhiteSpace(stageName))
                {
                    result.AddError($"Catalog pipeline '{name}' contains a blank stage name.");
                }
            }
        }

        return result;
    }
}
