using System.Text;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Formats.Hl7.Filtering;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Formats.Hl7.UnitTests;

public sealed class Hl7FilterStageTests
{
    private const string Adt = "MSH|^~\\&|SRC|FAC|IBE|HOSP|20240101120000||ADT^A01|MSG-1|P|2.5\r" +
                               "PID|1||PAT-123^^^HOSP^MR||Doe^Jane||19800101|F\r";

    [Fact]
    public async Task Allows_configured_message_type()
    {
        var stage = new Hl7FilterStage(new Hl7FilterOptions { AllowedMessageTypes = ["ADT^A01"] });
        var context = MessageContextBuilder.Create(payload: Adt);

        var result = await stage.ProcessAsync(context);

        Assert.False(result.Filtered);
    }

    [Fact]
    public async Task Filters_message_type_not_in_allow_list()
    {
        var stage = new Hl7FilterStage(new Hl7FilterOptions { AllowedMessageTypes = ["ORU^R01"] });
        var context = MessageContextBuilder.Create(payload: Adt);

        var result = await stage.ProcessAsync(context);

        Assert.True(result.Filtered);
        Assert.Contains("not allowed", result.Reason);
        Assert.Equal(result.Reason, context.Headers[Hl7FilterStage.FilterReasonHeader]);
    }

    [Fact]
    public async Task Filters_blocked_message_type()
    {
        var stage = new Hl7FilterStage(new Hl7FilterOptions { BlockedMessageTypes = ["ADT^A01"] });
        var context = MessageContextBuilder.Create(payload: Adt);

        var result = await stage.ProcessAsync(context);

        Assert.True(result.Filtered);
        Assert.Contains("blocked", result.Reason);
    }

    [Fact]
    public async Task Filters_by_segment_field_rule()
    {
        var stage = new Hl7FilterStage(new Hl7FilterOptions
        {
            FieldRules =
            [
                new Hl7FieldFilterRule
                {
                    Segment = "PID",
                    Field = 8,
                    EqualsValue = "F",
                    Reason = "female patients filtered for this test route",
                },
            ],
        });
        var context = MessageContextBuilder.Create(payload: Adt);

        var result = await stage.ProcessAsync(context);

        Assert.True(result.Filtered);
        Assert.Equal("female patients filtered for this test route", result.Reason);
    }

    [Fact]
    public void Filtered_delivery_renders_hl7_application_reject_ack()
    {
        var formatter = new Hl7SingleAckFormatter();
        var context = MessageContextBuilder.Create(payload: Adt);

        var ack = Encoding.UTF8.GetString(formatter.Render(context, new DeliveryResult(DeliveryOutcome.Filtered, "blocked by filter")).Span);

        Assert.Contains("MSA|AR|MSG-1", ack);
        Assert.Contains("blocked by filter", ack);
    }
}
