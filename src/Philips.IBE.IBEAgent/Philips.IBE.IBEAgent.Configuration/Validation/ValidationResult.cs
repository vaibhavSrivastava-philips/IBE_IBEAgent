namespace Philips.IBE.IBEAgent.Configuration;

// §8 — structural validation result: a batch of human-readable, actionable errors (not exceptions).
// Used at startup by the host/ContractCompiler to fail fast with a full picture, not one-at-a-time.
public sealed class ValidationResult
{
    private readonly List<string> _errors = [];

    public IReadOnlyList<string> Errors => _errors;
    public bool IsValid => _errors.Count == 0;

    public void AddError(string message) => _errors.Add(message);

    public static ValidationResult Success() => new();
}
