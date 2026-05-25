using WebReaper.AI.Llm;

namespace WebReaper.AI.Tests;

// ADR-0066: contract tests for the LlmCallTelemetry accumulator
// (thread-safe per-run aggregator). Pin Record / Snapshot / Reset
// semantics including the null-token sentinel logic that
// distinguishes "no call ever surfaced a value" (snapshot field
// null) from "some calls reported 0" (snapshot field 0).
public class LlmCallTelemetryTests
{
    // ---- Empty / Null ------------------------------------------------------

    [Fact]
    public void Empty_snapshot_has_zero_counts_and_null_tokens()
    {
        var telemetry = new LlmCallTelemetry();
        var snap = telemetry.Snapshot();

        Assert.Equal(0, snap.CallCount);
        Assert.Null(snap.InputTokens);
        Assert.Null(snap.OutputTokens);
        Assert.Null(snap.CachedInputTokens);
        Assert.Null(snap.TotalTokens);
        Assert.Equal(0, snap.ParseRetries);
        Assert.Equal(TimeSpan.Zero, snap.TotalDuration);
        Assert.Empty(snap.PerAdapter);
    }

    [Fact]
    public void NullLlmCallTelemetry_Snapshot_returns_Empty()
    {
        var snap = NullLlmCallTelemetry.Instance.Snapshot();
        Assert.Same(LlmTelemetrySnapshot.Empty, snap);
    }

    [Fact]
    public void NullLlmCallTelemetry_Record_is_noop()
    {
        // Just verifies no throw.
        NullLlmCallTelemetry.Instance.Record(new LlmCallUsage(
            "Test", InputTokens: 100, OutputTokens: 10,
            CachedInputTokens: null, TotalTokens: 110,
            ParseRetries: 0, Duration: TimeSpan.FromMilliseconds(5)));
    }

    // ---- Single Record -----------------------------------------------------

    [Fact]
    public void Single_Record_populates_aggregates_and_per_adapter()
    {
        var telemetry = new LlmCallTelemetry();
        telemetry.Record(new LlmCallUsage(
            DescriptorName: "Extractor",
            InputTokens: 100,
            OutputTokens: 10,
            CachedInputTokens: 80,
            TotalTokens: 110,
            ParseRetries: 0,
            Duration: TimeSpan.FromMilliseconds(50)));

        var snap = telemetry.Snapshot();
        Assert.Equal(1, snap.CallCount);
        Assert.Equal(100L, snap.InputTokens);
        Assert.Equal(10L, snap.OutputTokens);
        Assert.Equal(80L, snap.CachedInputTokens);
        Assert.Equal(110L, snap.TotalTokens);
        Assert.Equal(0, snap.ParseRetries);
        Assert.Equal(TimeSpan.FromMilliseconds(50), snap.TotalDuration);

        Assert.Single(snap.PerAdapter);
        var stats = snap.PerAdapter["Extractor"];
        Assert.Equal("Extractor", stats.Name);
        Assert.Equal(1, stats.CallCount);
        Assert.Equal(100L, stats.InputTokens);
        Assert.Equal(10L, stats.OutputTokens);
        Assert.Equal(80L, stats.CachedInputTokens);
        Assert.Equal(110L, stats.TotalTokens);
    }

    // ---- Sum across Records ------------------------------------------------

    [Fact]
    public void Two_Records_same_adapter_sum_into_one_bucket()
    {
        var telemetry = new LlmCallTelemetry();
        telemetry.Record(MakeUsage("Extractor", input: 100, output: 10, cached: 80, total: 110));
        telemetry.Record(MakeUsage("Extractor", input: 200, output: 20, cached: 150, total: 220));

        var snap = telemetry.Snapshot();
        Assert.Equal(2, snap.CallCount);
        Assert.Equal(300L, snap.InputTokens);
        Assert.Equal(30L, snap.OutputTokens);
        Assert.Equal(230L, snap.CachedInputTokens);
        Assert.Equal(330L, snap.TotalTokens);

        Assert.Single(snap.PerAdapter);
        Assert.Equal(2, snap.PerAdapter["Extractor"].CallCount);
        Assert.Equal(300L, snap.PerAdapter["Extractor"].InputTokens);
    }

    [Fact]
    public void Records_across_different_adapters_aggregate_globally_split_per_adapter()
    {
        var telemetry = new LlmCallTelemetry();
        telemetry.Record(MakeUsage("Extractor", input: 100, output: 10, cached: 80, total: 110));
        telemetry.Record(MakeUsage("Brain", input: 200, output: 20, cached: 150, total: 220));
        telemetry.Record(MakeUsage("Extractor", input: 50, output: 5, cached: 40, total: 55));

        var snap = telemetry.Snapshot();
        Assert.Equal(3, snap.CallCount);
        Assert.Equal(350L, snap.InputTokens);
        Assert.Equal(35L, snap.OutputTokens);
        Assert.Equal(270L, snap.CachedInputTokens);
        Assert.Equal(385L, snap.TotalTokens);

        Assert.Equal(2, snap.PerAdapter.Count);
        Assert.Equal(2, snap.PerAdapter["Extractor"].CallCount);
        Assert.Equal(150L, snap.PerAdapter["Extractor"].InputTokens);
        Assert.Equal(1, snap.PerAdapter["Brain"].CallCount);
        Assert.Equal(200L, snap.PerAdapter["Brain"].InputTokens);
    }

    // ---- Null-token sentinel semantics -------------------------------------

    [Fact]
    public void All_records_with_null_token_field_keeps_aggregate_null()
    {
        // Two records, both with null InputTokens — snapshot's InputTokens
        // stays null (distinguishes "no provider surfaced it" from "every
        // provider reported 0").
        var telemetry = new LlmCallTelemetry();
        telemetry.Record(MakeUsage("X", input: null, output: 10, cached: null, total: 10));
        telemetry.Record(MakeUsage("X", input: null, output: 20, cached: null, total: 20));

        var snap = telemetry.Snapshot();
        Assert.Null(snap.InputTokens);
        Assert.Null(snap.CachedInputTokens);
        Assert.Equal(30L, snap.OutputTokens);
        Assert.Equal(30L, snap.TotalTokens);
    }

    [Fact]
    public void Mixed_null_and_value_records_sum_the_non_null_subset()
    {
        var telemetry = new LlmCallTelemetry();
        telemetry.Record(MakeUsage("X", input: 100, output: 10, cached: 80, total: 110));
        telemetry.Record(MakeUsage("X", input: null, output: 20, cached: null, total: 20));

        var snap = telemetry.Snapshot();
        // InputTokens / CachedInputTokens get only the first call's value
        // (second was null); OutputTokens / TotalTokens get the sum.
        Assert.Equal(100L, snap.InputTokens);
        Assert.Equal(30L, snap.OutputTokens);
        Assert.Equal(80L, snap.CachedInputTokens);
        Assert.Equal(130L, snap.TotalTokens);
    }

    [Fact]
    public void Zero_value_in_record_distinguishes_from_null()
    {
        var telemetry = new LlmCallTelemetry();
        telemetry.Record(MakeUsage("X", input: 0, output: 0, cached: 0, total: 0));

        var snap = telemetry.Snapshot();
        // Zero is a value, not null — snapshot reports 0 (not null).
        Assert.Equal(0L, snap.InputTokens);
        Assert.Equal(0L, snap.OutputTokens);
        Assert.Equal(0L, snap.CachedInputTokens);
        Assert.Equal(0L, snap.TotalTokens);
    }

    // ---- Reset ------------------------------------------------------------

    [Fact]
    public void Reset_clears_aggregates_and_per_adapter()
    {
        var telemetry = new LlmCallTelemetry();
        telemetry.Record(MakeUsage("X", input: 100, output: 10, cached: 80, total: 110));
        telemetry.Record(MakeUsage("Y", input: 200, output: 20, cached: 150, total: 220));

        telemetry.Reset();

        var snap = telemetry.Snapshot();
        Assert.Equal(0, snap.CallCount);
        Assert.Null(snap.InputTokens);
        Assert.Null(snap.OutputTokens);
        Assert.Null(snap.CachedInputTokens);
        Assert.Null(snap.TotalTokens);
        Assert.Equal(0, snap.ParseRetries);
        Assert.Empty(snap.PerAdapter);
    }

    [Fact]
    public void Reset_then_Record_starts_fresh_accumulation()
    {
        var telemetry = new LlmCallTelemetry();
        telemetry.Record(MakeUsage("X", input: 999, output: 999, cached: 999, total: 1998));
        telemetry.Reset();
        telemetry.Record(MakeUsage("X", input: 5, output: 1, cached: 0, total: 6));

        var snap = telemetry.Snapshot();
        Assert.Equal(1, snap.CallCount);
        Assert.Equal(5L, snap.InputTokens);
        Assert.Equal(1L, snap.OutputTokens);
        Assert.Equal(0L, snap.CachedInputTokens);
        Assert.Equal(6L, snap.TotalTokens);
    }

    // ---- ParseRetries + Duration ------------------------------------------

    [Fact]
    public void ParseRetries_sums_across_records()
    {
        var telemetry = new LlmCallTelemetry();
        telemetry.Record(MakeUsage("X", input: 10, output: 1, cached: null, total: 11, retries: 1));
        telemetry.Record(MakeUsage("X", input: 10, output: 1, cached: null, total: 11, retries: 0));
        telemetry.Record(MakeUsage("X", input: 10, output: 1, cached: null, total: 11, retries: 1));

        var snap = telemetry.Snapshot();
        Assert.Equal(2L, snap.ParseRetries);
        Assert.Equal(2L, snap.PerAdapter["X"].ParseRetries);
    }

    [Fact]
    public void TotalDuration_sums_across_records()
    {
        var telemetry = new LlmCallTelemetry();
        telemetry.Record(MakeUsage("X", input: 10, output: 1, cached: null, total: 11,
            duration: TimeSpan.FromMilliseconds(50)));
        telemetry.Record(MakeUsage("X", input: 10, output: 1, cached: null, total: 11,
            duration: TimeSpan.FromMilliseconds(100)));

        var snap = telemetry.Snapshot();
        Assert.Equal(TimeSpan.FromMilliseconds(150), snap.TotalDuration);
    }

    // ---- Thread safety ----------------------------------------------------

    [Fact]
    public async Task Parallel_Record_from_many_tasks_aggregates_correctly()
    {
        var telemetry = new LlmCallTelemetry();
        const int tasksCount = 50;
        const int recordsPerTask = 100;
        const long perRecord = 7;

        await Task.WhenAll(Enumerable.Range(0, tasksCount).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < recordsPerTask; i++)
            {
                telemetry.Record(new LlmCallUsage(
                    DescriptorName: "Concurrent",
                    InputTokens: perRecord,
                    OutputTokens: perRecord,
                    CachedInputTokens: perRecord,
                    TotalTokens: perRecord * 2,
                    ParseRetries: 0,
                    Duration: TimeSpan.FromMilliseconds(1)));
            }
        })));

        var snap = telemetry.Snapshot();
        var expectedCalls = (long)(tasksCount * recordsPerTask);
        Assert.Equal(expectedCalls, snap.CallCount);
        Assert.Equal(expectedCalls * perRecord, snap.InputTokens);
        Assert.Equal(expectedCalls * perRecord, snap.OutputTokens);
        Assert.Equal(expectedCalls * perRecord, snap.CachedInputTokens);
        Assert.Equal(expectedCalls * perRecord * 2, snap.TotalTokens);
        Assert.Equal(expectedCalls, snap.PerAdapter["Concurrent"].CallCount);
    }

    [Fact]
    public void Snapshot_returns_independent_copy_of_dictionary()
    {
        var telemetry = new LlmCallTelemetry();
        telemetry.Record(MakeUsage("X", input: 10, output: 1, cached: null, total: 11));

        var snap1 = telemetry.Snapshot();
        telemetry.Record(MakeUsage("Y", input: 20, output: 2, cached: null, total: 22));

        // snap1 captured at one moment; subsequent records don't mutate it.
        Assert.Single(snap1.PerAdapter);
        Assert.True(snap1.PerAdapter.ContainsKey("X"));
        Assert.False(snap1.PerAdapter.ContainsKey("Y"));
    }

    // ---- Argument validation ----------------------------------------------

    [Fact]
    public void Record_null_usage_throws()
    {
        var telemetry = new LlmCallTelemetry();
        Assert.Throws<ArgumentNullException>(() => telemetry.Record(null!));
    }

    // ---- Helpers ----------------------------------------------------------

    private static LlmCallUsage MakeUsage(
        string name,
        long? input,
        long? output,
        long? cached,
        long? total,
        int retries = 0,
        TimeSpan? duration = null)
        => new(name, input, output, cached, total, retries, duration ?? TimeSpan.FromMilliseconds(1));
}
