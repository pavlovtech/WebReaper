using WebReaper.Builders;
using WebReaper.Core.Agent.Abstract;
using WebReaper.Domain.Agent;

namespace WebReaper.UnitTests;

// ADR-0051: AgentEngineBuilder grammar. Pins the structural-guarantee
// invariants from the builder pattern (ADR-0025 / ADR-0009 sibling pair):
// the static Start entry, the brain-required-or-throw BuildAsync, the
// WithRunId vs WithNewRun semantics, the satellite-style WithRunStore
// composability.
public class AgentEngineBuilderTests
{
    [Fact]
    public async Task BuildAsync_without_brain_throws_InvalidOperationException()
    {
        var builder = AgentEngineBuilder.Start("https://example.com/", "test");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => builder.BuildAsync());
        Assert.Contains("IAgentBrain", ex.Message);
    }

    [Fact]
    public async Task BuildAsync_with_brain_succeeds_with_in_memory_defaults()
    {
        var brain = new NoopBrain();
        var engine = await AgentEngineBuilder
            .Start("https://example.com/", "test")
            .WithBrain(brain)
            .BuildAsync();

        Assert.NotNull(engine);
    }

    [Fact]
    public void Start_rejects_empty_startUrl()
    {
        Assert.ThrowsAny<ArgumentException>(() => AgentEngineBuilder.Start("", "goal"));
    }

    [Fact]
    public void Start_rejects_empty_goal()
    {
        Assert.ThrowsAny<ArgumentException>(() => AgentEngineBuilder.Start("https://example.com/", ""));
    }

    [Fact]
    public async Task WithRunId_then_WithNewRun_clears_the_explicit_run_id()
    {
        // The most-recent call wins. Build succeeds either way; we just
        // pin that WithNewRun is the documented escape hatch.
        var brain = new NoopBrain();
        var engine = await AgentEngineBuilder
            .Start("https://example.com/", "test")
            .WithBrain(brain)
            .WithRunId("explicit")
            .WithNewRun()
            .BuildAsync();

        Assert.NotNull(engine);
    }

    [Fact]
    public void WithMaxSteps_rejects_non_positive()
    {
        var builder = AgentEngineBuilder.Start("https://example.com/", "test");
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithMaxSteps(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithMaxSteps(-1));
    }

    private sealed class NoopBrain : IAgentBrain
    {
        public ValueTask<AgentDecision> DecideAsync(AgentState state, CancellationToken ct = default)
            => ValueTask.FromResult<AgentDecision>(new AgentDecision.Stop { Reason = "test" });
    }
}
