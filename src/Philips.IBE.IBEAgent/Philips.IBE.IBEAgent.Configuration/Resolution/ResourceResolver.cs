namespace Philips.IBE.IBEAgent.Configuration;

// §3.6/§3.7 (ADR 0001) — resolves Kind:file / Kind:secret Setting values at contract-resolution time.
// A file value (a Resources name OR a path) resolves to an ABSOLUTE path confined to the allowed
// resources root: traversal and absolute-escape are rejected (FSE-supplied paths are untrusted). Each
// resolved file is recorded in a per-run manifest for ops discoverability. Secrets resolve through an
// injected resolver and are NEVER recorded. Existence/checksum verification is a future refinement;
// this class owns the security-critical confinement only.
public sealed class ResourceResolver
{
    private readonly string _root;
    private readonly IReadOnlyDictionary<string, ResourceDefinition> _resources;
    private readonly Func<string, string?>? _secretResolver;
    private readonly List<ResolvedResource> _manifest = [];

    public ResourceResolver(
        string allowedRoot,
        IReadOnlyDictionary<string, ResourceDefinition>? resources = null,
        Func<string, string?>? secretResolver = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(allowedRoot);
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(allowedRoot));
        _resources = resources ?? new Dictionary<string, ResourceDefinition>(StringComparer.Ordinal);
        _secretResolver = secretResolver;
    }

    public IReadOnlyList<ResolvedResource> Manifest => _manifest;

    public string ResolveFile(string contract, string setting, string value, string? expectedContentType)
    {
        var relativeOrName = value;
        var contentType = expectedContentType;
        if (_resources.TryGetValue(value, out var resource))
        {
            relativeOrName = resource.Ref
                ?? throw new ContractResolutionException(contract, $"Setting '{setting}' resource '{value}' has no Ref.");
            contentType ??= resource.ContentType;
        }

        if (Path.IsPathRooted(relativeOrName))
        {
            throw new ContractResolutionException(contract,
                $"Setting '{setting}' file '{relativeOrName}' must be a path relative to the resources root (absolute paths are rejected).");
        }

        var resolved = Path.GetFullPath(Path.Combine(_root, relativeOrName));
        if (!resolved.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new ContractResolutionException(contract,
                $"Setting '{setting}' file '{value}' escapes the allowed resources root.");
        }

        _manifest.Add(new ResolvedResource(contract, setting, resolved, contentType));
        return resolved;
    }

    public string ResolveSecret(string contract, string setting, string name)
    {
        var resolver = _secretResolver
            ?? throw new ContractResolutionException(contract, $"Setting '{setting}' is a secret but no secret resolver is configured.");
        return resolver(name)
            ?? throw new ContractResolutionException(contract, $"Setting '{setting}' secret '{name}' was not found.");
    }
}

// One entry of the resolved-resource manifest (files only; secrets are never listed).
public sealed record ResolvedResource(string Contract, string Setting, string Path, string? ContentType);
