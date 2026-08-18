using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Configuration;

// §8 — pure structural validation of a single contract's shape: inputs/outputs well-formed,
// ack XOR response, FromInputIds integrity, capacity/DOP sanity. No I/O, no codec/registry
// lookups (those need the Catalog and belong to a separate cross-reference check).
public static class ContractOptionsValidator
{
    public static ValidationResult Validate(ContractOptions contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        var result = ValidationResult.Success();

        if (string.IsNullOrWhiteSpace(contract.Name))
        {
            result.AddError("Contract.Name must not be empty.");
        }

        var inputs = ResolveInputs(contract);
        if (inputs.Count == 0)
        {
            result.AddError($"Contract '{contract.Name}' must declare at least one input.");
        }

        var inputIds = new HashSet<int>();
        foreach (var input in inputs)
        {
            if (!inputIds.Add(input.InputId))
            {
                result.AddError($"Contract '{contract.Name}' declares duplicate InputId {input.InputId}.");
            }

            ValidateChannel(contract.Name, $"Input {input.InputId}", input.Channel, result);
        }

        if (contract.Outputs is null || contract.Outputs.Count == 0)
        {
            result.AddError($"Contract '{contract.Name}' must declare at least one output.");
        }
        else
        {
            var outputIds = new HashSet<int>();
            foreach (var output in contract.Outputs)
            {
                if (!outputIds.Add(output.OutputId))
                {
                    result.AddError($"Contract '{contract.Name}' declares duplicate OutputId {output.OutputId}.");
                }

                ValidateChannel(contract.Name, $"Output {output.OutputId}", output.Channel, result);

                if (output.FromInputIds is { Count: > 0 })
                {
                    foreach (var fromId in output.FromInputIds)
                    {
                        if (!inputIds.Contains(fromId))
                        {
                            result.AddError(
                                $"Contract '{contract.Name}' Output {output.OutputId} references unknown FromInputIds entry {fromId}.");
                        }
                    }
                }

                if (output.Batching is { Enabled: true, MaxCount: <= 0 })
                {
                    result.AddError($"Contract '{contract.Name}' Output {output.OutputId} Batching.MaxCount must be > 0 when enabled.");
                }

                if (output.Retry.MaxAttempts < 1)
                {
                    result.AddError($"Contract '{contract.Name}' Output {output.OutputId} Retry.MaxAttempts must be >= 1.");
                }

                if (output.RouteWhen is { Count: > 0 })
                {
                    foreach (var (key, value) in output.RouteWhen)
                    {
                        if (string.IsNullOrWhiteSpace(key))
                            result.AddError($"Contract '{contract.Name}' Output {output.OutputId} RouteWhen has an empty key.");
                        if (string.IsNullOrEmpty(value))
                            result.AddError($"Contract '{contract.Name}' Output {output.OutputId} RouteWhen['{key}'] has an empty value.");
                    }
                }
            }
        }

        ValidateReplyMode(contract, result);

        return result;
    }

    // §6.1 — exactly one reply mode may be active for a contract: Acknowledgement XOR Response.
    private static void ValidateReplyMode(ContractOptions contract, ValidationResult result)
    {
        var ackEnabled = contract.Acknowledgement.IsEnabled;
        var responseEnabled = contract.Response.IsEnabled;

        if (ackEnabled && responseEnabled)
        {
            result.AddError($"Contract '{contract.Name}' cannot enable both Acknowledgement and Response; choose exactly one reply mode.");
        }

        // Batch ack shape (BHS..BTS) is not implemented; reject it so it can't be silently misconfigured.
        if (ackEnabled && contract.Acknowledgement.Shape == AckShape.Batch)
        {
            result.AddError($"Contract '{contract.Name}' Acknowledgement.Shape 'Batch' is not supported.");
        }

        if (responseEnabled && contract.Response.TimeoutMs <= 0)
        {
            result.AddError($"Contract '{contract.Name}' Response.TimeoutMs must be > 0.");
        }

        if (responseEnabled && contract.Response.FromOutputId is { } fromOutputId
            && contract.Outputs is not null
            && !contract.Outputs.Any(o => o.OutputId == fromOutputId))
        {
            result.AddError($"Contract '{contract.Name}' Response.FromOutputId {fromOutputId} does not match any declared output.");
        }
    }

    private static void ValidateChannel(string contractName, string subject, ChannelOptions channel, ValidationResult result)
    {
        if (channel.Capacity <= 0)
        {
            result.AddError($"Contract '{contractName}' {subject} Channel.Capacity must be > 0.");
        }

        if (channel.DegreeOfParallelism <= 0)
        {
            result.AddError($"Contract '{contractName}' {subject} Channel.DegreeOfParallelism must be > 0.");
        }

        if (channel.Ordered && channel.DegreeOfParallelism > 1)
        {
            result.AddError($"Contract '{contractName}' {subject} cannot combine Channel.Ordered with DegreeOfParallelism > 1.");
        }
    }

    // Backward-compat shorthand: Contract.InputIds -> effective Inputs list with default channels,
    // merged with any explicit Inputs entries (explicit entries win on duplicate InputId).
    public static IReadOnlyList<InputOptions> ResolveInputs(ContractOptions contract)
    {
        if ((contract.InputIds is null || contract.InputIds.Count == 0) && contract.Inputs.Count > 0)
        {
            return contract.Inputs;
        }

        var explicitIds = contract.Inputs.Select(i => i.InputId).ToHashSet();
        var merged = new List<InputOptions>(contract.Inputs);

        foreach (var id in contract.InputIds ?? [])
        {
            if (explicitIds.Add(id))
            {
                merged.Add(new InputOptions { InputId = id });
            }
        }

        return merged;
    }
}
