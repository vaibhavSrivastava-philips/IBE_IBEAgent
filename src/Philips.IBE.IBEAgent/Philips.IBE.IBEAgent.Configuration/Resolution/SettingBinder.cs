using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Configuration;

// §3.5 (ADR 0001) — validates an FSE Settings bag against the Workflow's declared Setting definitions
// and binds each value onto the resolved contract. Least-privilege: unknown settings, required-but-missing
// values, and guardrail violations are fail-fast ContractResolutionException errors keyed by the friendly
// name. Binding navigates the immutable contract record graph and writes the leaf via reflection (init-only
// setters are writable at runtime); an "Outputs[]" segment fans a value across every output. Kind:file/secret
// values resolve through the ResourceResolver; "stage:<name>.<key>" targets collect into the returned
// per-stage parameters. The Workflow definitions themselves are never mutated.
internal static class SettingBinder
{
    private static readonly IReadOnlyDictionary<string, SettingDefinition> NoDefinitions =
        new Dictionary<string, SettingDefinition>(StringComparer.Ordinal);
    private static readonly IReadOnlyDictionary<string, string?> NoValues =
        new Dictionary<string, string?>(StringComparer.Ordinal);
    private static readonly IReadOnlyDictionary<string, StageParameters> NoStageParameters =
        new Dictionary<string, StageParameters>(StringComparer.Ordinal);

    // Applies the FSE Settings onto the contract's fields (in place) and returns the per-stage parameters
    // collected from any "stage:<name>.<key>" Bind targets (empty when there are none).
    public static IReadOnlyDictionary<string, StageParameters> Apply(
        ContractOptions contract, ContractWorkflowOptions? workflow, ResourceResolver? resources = null)
    {
        var declared = workflow?.Settings ?? NoDefinitions;
        var supplied = contract.Workflow?.Settings ?? NoValues;

        // Least privilege: the FSE may only set what the Workflow exposed.
        foreach (var key in supplied.Keys)
        {
            if (!declared.ContainsKey(key))
            {
                throw new ContractResolutionException(contract.Name,
                    $"Setting '{key}' is not exposed by workflow '{contract.Workflow?.Use}'.");
            }
        }

        var stageParameters = new Dictionary<string, Dictionary<string, string?>>(StringComparer.Ordinal);

        foreach (var (name, def) in declared)
        {
            var raw = supplied.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value) ? value : def.Default;
            if (raw is null)
            {
                throw new ContractResolutionException(contract.Name,
                    $"Setting '{name}' is required (it has no default) and was not provided.");
            }

            Validate(contract, name, def, raw);

            // Resolve resource-kinded values before binding: a file resolves to a security-checked path,
            // a secret to its stored value (never recorded in the manifest).
            raw = def.Kind switch
            {
                "file" => Context(contract, name, resources).ResolveFile(contract.Name, name, raw, def.ContentType),
                "secret" => Context(contract, name, resources).ResolveSecret(contract.Name, name, raw),
                _ => raw,
            };

            var target = string.IsNullOrWhiteSpace(def.Bind) ? name : def.Bind!;
            if (target.StartsWith("stage:", StringComparison.Ordinal))
            {
                AddStageParameter(contract, name, target, raw, stageParameters);
                continue;
            }

            Bind(contract, name, target, def, raw);
        }

        if (stageParameters.Count == 0)
        {
            return NoStageParameters;
        }

        var result = new Dictionary<string, StageParameters>(StringComparer.Ordinal);
        foreach (var (stage, values) in stageParameters)
        {
            result[stage] = new StageParameters { Values = values };
        }
        return result;
    }

    private static ResourceResolver Context(ContractOptions contract, string setting, ResourceResolver? resources)
        => resources ?? throw new ContractResolutionException(contract.Name,
            $"Setting '{setting}' needs a resources context but none is configured.");

    private static void AddStageParameter(
        ContractOptions contract, string name, string target, string raw,
        Dictionary<string, Dictionary<string, string?>> stageParameters)
    {
        var rest = target["stage:".Length..];
        var dot = rest.IndexOf('.');
        if (dot <= 0 || dot == rest.Length - 1)
        {
            throw new ContractResolutionException(contract.Name,
                $"Setting '{name}' Bind '{target}' must be 'stage:<stage>.<param>'.");
        }

        var stageName = rest[..dot];
        var paramKey = rest[(dot + 1)..];
        if (!stageParameters.TryGetValue(stageName, out var bag))
        {
            stageParameters[stageName] = bag = new Dictionary<string, string?>(StringComparer.Ordinal);
        }
        bag[paramKey] = raw;
    }

    private static void Validate(ContractOptions contract, string name, SettingDefinition def, string raw)
    {
        if (def.Allowed is { Count: > 0 } allowed && !allowed.Contains(raw))
        {
            throw new ContractResolutionException(contract.Name,
                $"Setting '{name}' must be one of: {string.Join(", ", allowed)} (got '{raw}').");
        }

        if (def.Min is not null || def.Max is not null)
        {
            if (!double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
            {
                throw new ContractResolutionException(contract.Name, $"Setting '{name}' must be a number (got '{raw}').");
            }
            if (def.Min is not null && number < def.Min)
            {
                throw new ContractResolutionException(contract.Name, $"Setting '{name}' must be >= {def.Min} (got {number}).");
            }
            if (def.Max is not null && number > def.Max)
            {
                throw new ContractResolutionException(contract.Name, $"Setting '{name}' must be <= {def.Max} (got {number}).");
            }
        }

        if (!string.IsNullOrEmpty(def.Regex) && !Regex.IsMatch(raw, def.Regex, RegexOptions.None, TimeSpan.FromSeconds(1)))
        {
            throw new ContractResolutionException(contract.Name, $"Setting '{name}' must match /{def.Regex}/ (got '{raw}').");
        }
    }

    private static void Bind(ContractOptions contract, string name, string path, SettingDefinition def, string raw)
    {
        var segments = path.Split('.');
        IEnumerable<object> targets = new object[] { contract };

        for (var i = 0; i < segments.Length - 1; i++)
        {
            var isList = segments[i].EndsWith("[]", StringComparison.Ordinal);
            var member = isList ? segments[i][..^2] : segments[i];
            var next = new List<object>();
            foreach (var current in targets)
            {
                var value = Property(contract, name, path, current, member).GetValue(current)
                    ?? throw new ContractResolutionException(contract.Name,
                        $"Setting '{name}' Bind '{path}' navigated through a null '{member}'.");
                if (isList)
                {
                    foreach (var item in (IEnumerable)value)
                    {
                        if (item is not null) next.Add(item);
                    }
                }
                else
                {
                    next.Add(value);
                }
            }
            targets = next;
        }

        var leaf = segments[^1];
        foreach (var current in targets)
        {
            var property = Property(contract, name, path, current, leaf);
            property.SetValue(current, Convert(contract, name, def, property.PropertyType, raw));
        }
    }

    private static PropertyInfo Property(ContractOptions contract, string name, string path, object target, string member)
        => target.GetType().GetProperty(member)
           ?? throw new ContractResolutionException(contract.Name,
               $"Setting '{name}' Bind '{path}' has no member '{member}' on {target.GetType().Name}.");

    private static object? Convert(ContractOptions contract, string name, SettingDefinition def, Type targetType, string raw)
    {
        var type = Nullable.GetUnderlyingType(targetType) ?? targetType;
        try
        {
            if (type == typeof(string)) return raw;
            if (type == typeof(bool)) return bool.Parse(raw);
            if (type.IsEnum) return Enum.Parse(type, raw, ignoreCase: true);

            var number = double.Parse(raw, NumberStyles.Any, CultureInfo.InvariantCulture) * (def.Scale ?? 1.0);
            if (type == typeof(int)) return checked((int)Math.Round(number));
            if (type == typeof(long)) return checked((long)Math.Round(number));
            if (type == typeof(double)) return number;
            if (type == typeof(float)) return (float)number;

            return System.Convert.ChangeType(raw, type, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            throw new ContractResolutionException(contract.Name,
                $"Setting '{name}' value '{raw}' is not a valid {type.Name}.");
        }
    }
}
