using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class QuotaProjectionPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Contracts_keep_provider_windows_and_identity_outcomes_explicit()
    {
        var capability = new QuotaWindowCapability(new("codex:weekly"), QuotaWindowRole.Hero, "Weekly", TimeSpan.FromDays(7));
        var envelope = new ProviderQuotaEnvelope(new("codex"), [new(capability, 42m, Now.AddDays(7))], Now, AccountIdentityOutcome.Verified, null);

        Assert.Equal("codex", envelope.Provider.Value); Assert.Equal("codex:weekly", envelope.Windows[0].Capability.Key.Value);
        Assert.Equal(QuotaWindowRole.Hero, envelope.Windows[0].Capability.Role); Assert.Equal(AccountIdentityOutcome.Verified, envelope.Identity);
    }

    [Fact]
    public void Withholds_projection_for_sparse_or_boundary_evidence()
    {
        var cycle = Cycle();
        var sparse = Policy().Evaluate(new(cycle, Samples(cycle, 5, 20m, 1m), 24m, Now));
        var increased = Policy().Evaluate(new(cycle, Samples(cycle, 6, 20m, 1m).Append(new QuotaSample(cycle, Now.AddHours(-1), 40m, "increase")).ToArray(), 40m, Now));
        var otherCycle = new QuotaCycle(new("codex"), new("codex:short"), Now.AddDays(7), TimeSpan.FromDays(7));
        var reset = Policy().Evaluate(new(cycle, Samples(otherCycle, 6, 20m, 1m), 25m, Now));

        Assert.Equal(QuotaProjectionState.Learning, sparse.State); Assert.Equal("Learning your pace", sparse.Label);
        Assert.Equal(QuotaProjectionState.Learning, increased.State); Assert.Equal(QuotaProjectionState.Learning, reset.State);
        Assert.Null(sparse.EstimatedExhaustionAt); Assert.Null(increased.Risk);
    }

    [Fact]
    public void Projects_repeatably_from_deduplicated_recency_weighted_evidence()
    {
        var cycle = Cycle(); var samples = Samples(cycle, 6, 20m, 1m);
        var request = new QuotaProjectionRequest(cycle, samples.Append(samples[0]).ToArray(), 25m, Now);
        var baseline = Policy().Evaluate(new(cycle, samples.Take(5).ToArray(), 24m, Now)); var first = Policy().Evaluate(request); var second = Policy().Evaluate(request);

        Assert.Equal(QuotaProjectionState.Learning, baseline.State); Assert.Equal(QuotaProjectionState.Available, first.State); Assert.Equal(first, second);
        var perturbed = samples.Select((sample, index) => index == 4 ? sample with { PercentageUsed = 24.4m } : sample).ToArray();
        Assert.Equal(6, first.Evidence.SampleCount); Assert.Equal(.5m, first.RatePerHour); Assert.Equal(ProjectionConfidence.Low, first.Confidence);
        Assert.Equal(ResetRelativeRisk.ProjectedToRunOut, first.Risk); Assert.Equal(first.Risk, Policy().Evaluate(new(cycle, perturbed, 25m, Now)).Risk);
    }

    [Fact]
    public void Rejects_outliers_non_positive_rates_and_current_value_mismatches()
    {
        var cycle = Cycle(); var normal = Samples(cycle, 6, 20m, 1m);
        var outlier = normal.Select((sample, index) => sample with { PercentageUsed = index < 4 ? sample.PercentageUsed : sample.PercentageUsed + 60m }).ToArray();
        var flat = Samples(cycle, 6, 20m, 0m);

        Assert.Equal(QuotaProjectionState.Inconsistent, Policy().Evaluate(new(cycle, outlier, 85m, Now)).State);
        Assert.Equal(QuotaProjectionState.Learning, Policy().Evaluate(new(cycle, flat, 20m, Now)).State);
        Assert.Equal(QuotaProjectionState.Inconsistent, Policy().Evaluate(new(cycle, normal, 90m, Now)).State);
    }

    [Theory]
    [InlineData(6, ProjectionConfidence.Low)]
    [InlineData(10, ProjectionConfidence.Medium)]
    [InlineData(16, ProjectionConfidence.High)]
    public void Classifies_confidence_from_evidence_coverage(int count, ProjectionConfidence confidence)
    {
        var cycle = Cycle(); var result = Policy().Evaluate(new(cycle, Samples(cycle, count, 20m, .5m, 3), 20m + ((count - 1) * .5m), Now));

        Assert.Equal(QuotaProjectionState.Available, result.State); Assert.Equal(confidence, result.Confidence);
    }

    [Theory]
    [InlineData(25, ResetRelativeRisk.ProjectedToRunOut)]
    [InlineData(20, ResetRelativeRisk.CloseToReset)]
    [InlineData(18, ResetRelativeRisk.ProjectedToLast)]
    public void Classifies_reset_relative_risk_with_a_stability_margin(int resetHours, ResetRelativeRisk risk)
    {
        var cycle = Cycle(reset: Now.AddHours(resetHours));
        var result = Policy().Evaluate(new(cycle, Samples(cycle, 6, 85m, 1m), 90m, Now));

        Assert.Equal(QuotaProjectionState.Available, result.State); Assert.Equal(risk, result.Risk);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Withholds_all_projection_fields_at_or_after_reset(int resetMinutes)
    {
        var cycle = Cycle(Now.AddMinutes(resetMinutes));
        var result = Policy().Evaluate(new(cycle, Samples(cycle, 6, 20m, 1m), 25m, Now));

        Assert.Equal(QuotaProjectionState.Learning, result.State);
        Assert.Equal("Learning your pace", result.Label);
        Assert.Null(result.RatePerHour);
        Assert.Null(result.EstimatedExhaustionAt);
        Assert.Null(result.Confidence);
        Assert.Null(result.Risk);
    }

    [Fact]
    public void Excluded_interval_endpoints_do_not_reenter_through_neighbors()
    {
        var cycle = Cycle();
        var samples = Samples(cycle, 12, 20m, 1m)
            .Select((sample, index) => index >= 6
                ? sample with { PercentageUsed = sample.PercentageUsed + 2m }
                : sample).ToArray();
        var result = Policy().Evaluate(new(cycle, samples, 33m, Now));

        Assert.Equal(1, result.Evidence.ExcludedIntervals);
        Assert.Equal(10, result.Evidence.SampleCount);
        Assert.Equal(QuotaProjectionState.Available, result.State);
    }

    [Fact]
    public void Compares_fitted_current_value_at_evaluation_time_not_last_sample()
    {
        var cycle = Cycle();
        var samples = Samples(cycle, 6, 20m, 1m)
            .Select(sample => sample with { RetrievedAt = sample.RetrievedAt.AddHours(-8) }).ToArray();
        var result = Policy().Evaluate(new(cycle, samples, 25m, Now));

        Assert.Equal(QuotaProjectionState.Inconsistent, result.State);
        Assert.Null(result.RatePerHour);
        Assert.Null(result.EstimatedExhaustionAt);
        Assert.Null(result.Confidence);
        Assert.Null(result.Risk);
    }

    [Fact]
    public void Backward_timestamp_cuts_arrival_sequence_and_withholds_projection()
    {
        var cycle = Cycle();
        var samples = Samples(cycle, 6, 20m, 1m)
            .Append(new QuotaSample(cycle, Now.AddHours(-1), 25m, "backward")).ToArray();
        var result = Policy().Evaluate(new(cycle, samples, 25m, Now));

        Assert.Equal(QuotaProjectionState.Learning, result.State);
        Assert.Equal("Learning your pace", result.Label);
        Assert.Equal(1, result.Evidence.SampleCount);
        Assert.Null(result.RatePerHour);
        Assert.Null(result.EstimatedExhaustionAt);
        Assert.Null(result.Confidence);
        Assert.Null(result.Risk);
    }

    [Fact]
    public void Projection_recovers_using_only_sufficient_post_cut_evidence()
    {
        var cycle = Cycle();
        var previous = Samples(cycle, 6, 60m, 1m);
        var current = Samples(cycle, 6, 20m, 1m)
            .Select(sample => sample with { SampleKey = "new-" + sample.SampleKey }).ToArray();
        var expected = Policy().Evaluate(new(cycle, current, 25m, Now));
        var actual = Policy().Evaluate(new(cycle, previous.Concat(current).ToArray(), 25m, Now));

        Assert.Equal(QuotaProjectionState.Available, actual.State);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(21, ProjectionConfidence.Medium)]
    [InlineData(20, ProjectionConfidence.Low)]
    public void Medium_confidence_uses_total_interval_exclusion_ratio(int count, ProjectionConfidence confidence)
    {
        var cycle = Cycle();
        var samples = Samples(cycle, count, 20m, 1m)
            .Select((sample, index) => sample with
            {
                PercentageUsed = sample.PercentageUsed + (index >= 6 ? 2m : 0m) + (index >= 14 ? 2m : 0m)
            }).ToArray();
        var result = Policy().Evaluate(new(cycle, samples, samples[^1].PercentageUsed, Now));

        Assert.Equal(QuotaProjectionState.Available, result.State);
        Assert.Equal(2, result.Evidence.ExcludedIntervals);
        Assert.Equal(count - 4, result.Evidence.SampleCount);
        Assert.Equal(confidence, result.Confidence);
        Assert.NotNull(result.RatePerHour);
        Assert.NotNull(result.EstimatedExhaustionAt);
        Assert.NotNull(result.Risk);
    }

    private static QuotaProjectionPolicy Policy() => new();
    private static QuotaCycle Cycle(DateTimeOffset? reset = null) => new(new("codex"), new("codex:weekly"), reset ?? Now.AddDays(7), TimeSpan.FromDays(7));
    private static QuotaSample[] Samples(QuotaCycle cycle, int count, decimal first, decimal change, int intervalHours = 2) => Enumerable.Range(0, count)
        .Select(index => new QuotaSample(cycle, Now.AddHours(-((count - 1 - index) * intervalHours)), first + (index * change), $"sample-{index}")).ToArray();
}
