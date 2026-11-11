using System.Text.Json;
using FluentAssertions;

namespace Emberdrop.Core.Tests;

public class TriggerShould
{
    public static TheoryData<Trigger> All => new()
    {
        ManualTrigger.Instance,
        new OneOffTrigger(new DateTimeOffset(2026, 11, 8, 10, 30, 0, TimeSpan.Zero)),
        new RecurringTrigger("*/5 * * * *"),
    };

    [Theory]
    [MemberData(nameof(All))]
    public void SurviveATokenRoundTrip(Trigger trigger)
    {
        new TriggerKindRegistry().Parse(trigger.ToToken()).Should().Be(trigger);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void TreatNoTokenAsManual(string? token)
    {
        new TriggerKindRegistry().Parse(token).Should().Be(ManualTrigger.Instance);
    }

    [Theory]
    [InlineData("webhook:x")]
    [InlineData("recurring:")]
    [InlineData("one-off:not a date")]
    public void RefuseATokenItDoesNotKnow(string token)
    {
        var act = () => new TriggerKindRegistry().Parse(token);

        act.Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData("""{"kind":"manual"}""", "manual")]
    [InlineData("""{"kind":"recurring","cron":"0 2 * * *"}""", "recurring:0 2 * * *")]
    [InlineData("""{"kind":"one-off","at":"2030-01-01T00:00:00+00:00"}""", "one-off:2030-01-01T00:00:00.0000000+00:00")]
    public void ReadATriggerFromItsJson(string json, string token)
    {
        using var document = JsonDocument.Parse(json);

        new TriggerKindRegistry().FromJson(document.RootElement).ToToken().Should().Be(token);
    }

    [Theory]
    [InlineData("""{"kind":"recurring"}""", "needs a 'cron'")]
    [InlineData("""{"kind":"recurring","cron":"every day"}""", "5 or 6 fields")]
    [InlineData("""{"kind":"one-off"}""", "needs an 'at'")]
    [InlineData("""{"kind":"webhook"}""", "not a known trigger kind")]
    [InlineData("""{"cron":"0 2 * * *"}""", "must be an object with a 'kind'")]
    public void ExplainWhyATriggerCannotBeRead(string json, string message)
    {
        using var document = JsonDocument.Parse(json);

        var act = () => new TriggerKindRegistry().FromJson(document.RootElement);

        act.Should().Throw<FormatException>().WithMessage($"*{message}*");
    }

    [Fact]
    public void AcceptANewKindOfTrigger()
    {
        var registry = new TriggerKindRegistry();
        registry.Add(new QueueTriggerKind());

        registry.Parse("queue:orders").Should().Be(new QueueTrigger("orders"));
        registry.Kinds.Should().BeEquivalentTo("manual", "one-off", "queue", "recurring");
    }

    [Fact]
    public void RefuseTwoKindsWithTheSameName()
    {
        var act = () => new TriggerKindRegistry().Add(new QueueTriggerKind { Name = "manual" });

        act.Should().Throw<InvalidOperationException>().WithMessage("*'manual' is already registered*");
    }

    private sealed record QueueTrigger(string Queue) : Trigger
    {
        public override string Kind => "queue";

        public override string ToToken() => "queue:" + Queue;
    }

    private sealed class QueueTriggerKind : ITriggerKind
    {
        public string Name { get; init; } = "queue";

        public string Kind => Name;

        public Trigger FromJson(JsonElement element) => new QueueTrigger(element.GetProperty("queue").GetString()!);

        public Trigger FromToken(string detail) => new QueueTrigger(detail);
    }
}
