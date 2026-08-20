using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class NormalizedUsageEventTests
{
    [Fact]
    public void Valid_event_normalizes_metadata_cost_and_checked_display_totals()
    {
        var item = Event(tokens: new(1, 2, 3, 4, 5, 999), metadata: new(" provider ", " api ", " model "), cost: new(0, "usd"));
        Assert.Equal((6, 9, 15), (item.Tokens.DisplayInput, item.Tokens.DisplayOutput, item.Tokens.DisplayTotal));
        Assert.Equal(999, item.Tokens.SourceReportedTotal); Assert.Equal("USD", item.Cost.Currency);
        Assert.Equal(("provider", "api", "model"), (item.Metadata.Provider, item.Metadata.Api, item.Metadata.Model));
    }

    [Theory]
    [MemberData(nameof(NegativeTokenValues))]
    public void Negative_token_values_are_rejected(long input, long read, long write, long output, long reasoning, long? total)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new UsageTokens(input, read, write, output, reasoning, total));

    [Theory]
    [InlineData(long.MaxValue, 1, 0, 0, 0)] [InlineData(0, 0, 0, long.MaxValue, 1)] [InlineData(long.MaxValue, 0, 0, 1, 0)]
    public void Display_formula_overflow_is_rejected(long input, long read, long write, long output, long reasoning)
        => Assert.Throws<OverflowException>(() => new UsageTokens(input, read, write, output, reasoning));

    [Fact]
    public void Source_reported_total_is_diagnostic_only()
    {
        var tokens = new UsageTokens(1, 2, 3, 4, 5, long.MaxValue);
        Assert.Equal(15, tokens.DisplayTotal);
    }

    [Fact]
    public void Provider_formulas_are_deterministic_and_pi_warns_when_reasoning_exceeds_output()
    {
        var openCode = UsageTokenNormalization.OpenCode(1, 2, 3, 4, 5).Tokens;
        var pi = UsageTokenNormalization.Pi(1, 2, 3, 9, 5);
        var malformedPi = UsageTokenNormalization.Pi(1, 2, 3, 4, 5);
        Assert.Equal((4, 5, 15), (openCode.OutputVisible, openCode.OutputReasoning, openCode.DisplayTotal));
        Assert.Equal((4, 5), (pi.Tokens.OutputVisible, pi.Tokens.OutputReasoning)); Assert.Null(pi.Warning);
        Assert.Equal((0, 5), (malformedPi.Tokens.OutputVisible, malformedPi.Tokens.OutputReasoning));
        Assert.Equal(TokenNormalizationWarning.ReasoningExceededOutput, malformedPi.Warning);
    }

    [Theory]
    [MemberData(nameof(InvalidCostCurrencyPairs))]
    public void Invalid_cost_currency_pairs_are_rejected(long? nanos, string? currency)
        => Assert.ThrowsAny<ArgumentException>(() => new UsageCost(nanos, currency));

    [Theory]
    [InlineData("\u0001bad")] [InlineData("\tprovider")] [InlineData("provider\t")] [InlineData("\nprovider")] [InlineData("provider\n")]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    public void Invalid_display_metadata_is_rejected(string value)
        => Assert.Throws<ArgumentException>(() => new UsageDisplayMetadata(value, null, null));

    [Fact]
    public void Blank_optional_metadata_becomes_null()
        => Assert.Null(new UsageDisplayMetadata("  ", null, null).Provider);

    [Fact]
    public void Invalid_version_and_timestamp_order_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Event(payloadVersion: 0));
        Assert.Throws<ArgumentException>(() => Event(observedAt: At.AddTicks(-1)));
        Assert.Throws<ArgumentException>(() => Event(sourceUpdatedAt: At.AddTicks(-1)));
        Assert.Throws<ArgumentException>(() => Event(occurredAt: DateTimeOffset.UnixEpoch.AddTicks(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UsageSource(new(new byte[32]), UsageTool.OpenCode, 1, 1,
            DateTimeOffset.UnixEpoch.AddTicks(-1), DateTimeOffset.UnixEpoch, UsageSourceState.Active, 0));
    }

    [Theory]
    [InlineData(0)] [InlineData(31)] [InlineData(33)]
    public void Identities_require_exactly_32_bytes(int length)
    {
        Assert.All(new Action[]
        {
            () => new UsageEventIdentity(new byte[length]),
            () => new UsageSourceIdentity(new byte[length]),
            () => new UsageSourceEventIdentity(new byte[length])
        }, create => Assert.Throws<ArgumentException>(create));
    }

    [Fact]
    public void Enum_membership_matches_the_domain_contract()
    {
        Assert.Equal(new[] { UsageTool.OpenCode, UsageTool.Pi }, Enum.GetValues<UsageTool>());
        Assert.Equal(new[] { UsageEventKind.AssistantStep, UsageEventKind.AssistantTerminal, UsageEventKind.Compaction, UsageEventKind.BranchSummary, UsageEventKind.ToolResult, UsageEventKind.OtherAggregate }, Enum.GetValues<UsageEventKind>());
        Assert.Equal(new[] { UsagePurpose.Primary, UsagePurpose.Compaction, UsagePurpose.Subagent, UsagePurpose.BranchSummary, UsagePurpose.ToolResult, UsagePurpose.Other }, Enum.GetValues<UsagePurpose>());
        Assert.Equal(new[] { UsageOutcome.Success, UsageOutcome.Error, UsageOutcome.Aborted, UsageOutcome.Deferred, UsageOutcome.Unknown }, Enum.GetValues<UsageOutcome>());
        Assert.Equal(new[] { UsageFinishReason.Unknown, UsageFinishReason.Stop, UsageFinishReason.Length, UsageFinishReason.ToolCall, UsageFinishReason.Error, UsageFinishReason.Other }, Enum.GetValues<UsageFinishReason>());
        Assert.Equal(new[] { UsageFidelity.SuccessfulSettledStep, UsageFidelity.PersistedTerminalEvent, UsageFidelity.PersistedAggregateEvent }, Enum.GetValues<UsageFidelity>());
        Assert.Equal(new[] { TokenNormalizationWarning.ReasoningExceededOutput }, Enum.GetValues<TokenNormalizationWarning>());
    }

    [Fact]
    public void Persisted_enum_codes_are_stable()
    {
        AssertCodes<UsageTool>(1, 2); AssertCodes<UsageSourceState>(1, 2, 3);
        AssertCodes<UsageEventKind>(1, 2, 3, 4, 5, 6); AssertCodes<UsagePurpose>(1, 2, 3, 4, 5, 6);
        AssertCodes<UsageOutcome>(1, 2, 3, 4, 5); AssertCodes<UsageFinishReason>(1, 2, 3, 4, 5, 6);
        AssertCodes<UsageFidelity>(1, 2, 3); AssertCodes<TokenNormalizationWarning>(1);
    }

    [Fact]
    public void Identities_are_value_equal_immutable_and_safe_to_format()
    {
        var bytes = Enumerable.Repeat((byte)7, 32).ToArray(); var identity = new UsageEventIdentity(bytes); var equal = new UsageEventIdentity(bytes);
        bytes[0] = 9;
        var exported = identity.ToArray(); exported[0] = 9;
        Assert.Equal(identity, equal); Assert.False(identity.Equals(new UsageSourceIdentity(Enumerable.Repeat((byte)7, 32).ToArray())));
        Assert.Equal(7, identity.ToArray()[0]); Assert.NotSame(exported, identity.ToArray());
        Assert.Equal("UsageEventIdentity(opaque)", identity.ToString()); Assert.DoesNotContain("07", identity.ToString());
        var friends = typeof(UsageIdentity).Assembly.GetCustomAttributes(typeof(System.Runtime.CompilerServices.InternalsVisibleToAttribute), false)
            .Cast<System.Runtime.CompilerServices.InternalsVisibleToAttribute>();
        Assert.DoesNotContain(friends, attribute => attribute.AssemblyName.StartsWith("AIBar.Application", StringComparison.Ordinal));
    }

    [Fact]
    public void Event_surface_contains_no_sensitive_source_identifiers_or_content()
    {
        var forbidden = new[] { "prompt", "content", "path", "project", "session", "message", "response" };
        Assert.DoesNotContain(typeof(NormalizedUsageEvent).GetProperties(), property => forbidden.Any(term => property.Name.Contains(term, StringComparison.OrdinalIgnoreCase)));
    }

    private static readonly DateTimeOffset At = new(2026, 8, 12, 10, 0, 0, TimeSpan.Zero);
    public static TheoryData<long, long, long, long, long, long?> NegativeTokenValues => new()
    {
        { -1, 0, 0, 0, 0, null }, { 0, -1, 0, 0, 0, null }, { 0, 0, -1, 0, 0, null },
        { 0, 0, 0, -1, 0, null }, { 0, 0, 0, 0, -1, null }, { 0, 0, 0, 0, 0, -1 }
    };
    public static TheoryData<long?, string?> InvalidCostCurrencyPairs => new()
    {
        { null, "USD" }, { 1, null }, { -1, "USD" }, { 1, "US" }, { 1, "U1D" }, { 1, "\u20acUR" }
    };
    private static NormalizedUsageEvent Event(UsageTokens? tokens = null, UsageDisplayMetadata? metadata = null, UsageCost? cost = null,
        int payloadVersion = 1, DateTimeOffset? occurredAt = null, DateTimeOffset? observedAt = null, DateTimeOffset? sourceUpdatedAt = null) => new(
        new(new byte[32]), new(new byte[32]), new(new byte[32]), UsageTool.OpenCode, UsageEventKind.AssistantStep,
        UsagePurpose.Primary, UsageOutcome.Success, UsageFidelity.SuccessfulSettledStep, UsageFinishReason.Stop, occurredAt ?? At, observedAt ?? At, sourceUpdatedAt,
        metadata ?? new(null, null, null), tokens ?? new(0, 0, 0, 0, 0), cost ?? new(null, null), payloadVersion, false);
    private static void AssertCodes<T>(params int[] expected) where T : struct, Enum
        => Assert.Equal(expected, Enum.GetValues<T>().Select(value => Convert.ToInt32(value)));
}
