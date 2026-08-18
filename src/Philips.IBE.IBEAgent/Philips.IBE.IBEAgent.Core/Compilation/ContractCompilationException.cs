namespace Philips.IBE.IBEAgent.Core;

// Raised when a contract fails structural or cross-reference validation at compile time (startup
// fail-fast, §8/§14) — carries the full batch of errors rather than the first one.
public sealed class ContractCompilationException : Exception
{
    public string ContractName { get; }
    public IReadOnlyList<string> Errors { get; }

    public ContractCompilationException(string contractName, IReadOnlyList<string> errors)
        : base($"Contract '{contractName}' failed compilation:{Environment.NewLine}{string.Join(Environment.NewLine, errors)}")
    {
        ContractName = contractName;
        Errors = errors;
    }
}
