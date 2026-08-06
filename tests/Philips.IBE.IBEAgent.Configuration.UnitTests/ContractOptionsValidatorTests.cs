using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Configuration;

namespace Philips.IBE.IBEAgent.Configuration.UnitTests;

public sealed class ContractOptionsValidatorTests
{
    private static ContractOptions ValidContract() => new()
    {
        Name = "Adt",
        Inputs = [new InputOptions { InputId = 1 }],
        Outputs = [new OutputOptions { OutputId = 100 }],
    };

    [Fact]
    public void Valid_contract_passes()
    {
        var result = ContractOptionsValidator.Validate(ValidContract());

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Batch_ack_shape_is_rejected_as_unsupported()
    {
        var contract = ValidContract() with
        {
            Acknowledgement = new AckOptions { IsEnabled = true, Shape = AckShape.Batch },
        };

        var result = ContractOptionsValidator.Validate(contract);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Batch is not supported"));
    }

    [Fact]
    public void Blank_name_fails()
    {
        var contract = ValidContract() with { Name = " " };

        var result = ContractOptionsValidator.Validate(contract);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Name"));
    }

    [Fact]
    public void No_inputs_fails()
    {
        var contract = ValidContract() with { Inputs = [] };

        var result = ContractOptionsValidator.Validate(contract);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("at least one input"));
    }

    [Fact]
    public void Duplicate_input_ids_fail()
    {
        var contract = ValidContract() with
        {
            Inputs = [new InputOptions { InputId = 1 }, new InputOptions { InputId = 1 }],
        };

        var result = ContractOptionsValidator.Validate(contract);

        Assert.Contains(result.Errors, e => e.Contains("duplicate InputId"));
    }

    [Fact]
    public void No_outputs_fails()
    {
        var contract = ValidContract() with { Outputs = [] };

        var result = ContractOptionsValidator.Validate(contract);

        Assert.Contains(result.Errors, e => e.Contains("at least one output"));
    }

    [Fact]
    public void FromInputIds_referencing_unknown_input_fails()
    {
        var contract = ValidContract() with
        {
            Outputs = [new OutputOptions { OutputId = 100, FromInputIds = [99] }],
        };

        var result = ContractOptionsValidator.Validate(contract);

        Assert.Contains(result.Errors, e => e.Contains("unknown FromInputIds"));
    }

    [Fact]
    public void Ack_and_response_both_enabled_fails()
    {
        var contract = ValidContract() with
        {
            Acknowledgement = new AckOptions { IsEnabled = true },
            Response = new ResponseOptions { IsEnabled = true },
        };

        var result = ContractOptionsValidator.Validate(contract);

        Assert.Contains(result.Errors, e => e.Contains("cannot enable both"));
    }

    [Fact]
    public void Response_with_non_positive_timeout_fails()
    {
        var contract = ValidContract() with
        {
            Acknowledgement = new AckOptions { IsEnabled = false },
            Response = new ResponseOptions { IsEnabled = true, TimeoutMs = 0 },
        };

        var result = ContractOptionsValidator.Validate(contract);

        Assert.Contains(result.Errors, e => e.Contains("TimeoutMs"));
    }

    [Fact]
    public void Ordered_channel_with_dop_greater_than_one_fails()
    {
        var contract = ValidContract() with
        {
            Inputs = [new InputOptions { InputId = 1, Channel = new ChannelOptions { Ordered = true, DegreeOfParallelism = 2 } }],
        };

        var result = ContractOptionsValidator.Validate(contract);

        Assert.Contains(result.Errors, e => e.Contains("Ordered"));
    }

    [Fact]
    public void InputIds_shorthand_resolves_to_default_inputs()
    {
        var contract = new ContractOptions
        {
            Name = "Legacy",
            Inputs = [],
            InputIds = [1, 2],
            Outputs = [new OutputOptions { OutputId = 100 }],
        };

        var result = ContractOptionsValidator.Validate(contract);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void RouteWhen_with_empty_key_fails()
    {
        var contract = ValidContract() with
        {
            Outputs = [new OutputOptions { OutputId = 100, RouteWhen = new Dictionary<string, string> { [" "] = "ADT" } }],
        };

        var result = ContractOptionsValidator.Validate(contract);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("RouteWhen has an empty key"));
    }

    [Fact]
    public void RouteWhen_with_empty_value_fails()
    {
        var contract = ValidContract() with
        {
            Outputs = [new OutputOptions { OutputId = 100, RouteWhen = new Dictionary<string, string> { ["hl7.messageType"] = "" } }],
        };

        var result = ContractOptionsValidator.Validate(contract);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("empty value"));
    }

    [Fact]
    public void RouteWhen_with_valid_facts_passes()
    {
        var contract = ValidContract() with
        {
            Outputs = [new OutputOptions { OutputId = 100, RouteWhen = new Dictionary<string, string> { ["hl7.messageType"] = "ADT" } }],
        };

        var result = ContractOptionsValidator.Validate(contract);

        Assert.True(result.IsValid);
    }
}
