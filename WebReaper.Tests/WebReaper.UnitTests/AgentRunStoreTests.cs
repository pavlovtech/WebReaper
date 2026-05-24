using System.Text.Json.Nodes;
using WebReaper.Core.Agent.Abstract;
using WebReaper.Core.Agent.Concrete;
using WebReaper.Domain.Agent;
using WebReaper.Domain.Parsing;

namespace WebReaper.UnitTests;

// ADR-0051 §Decision §6: pins the IAgentRunStore contract — Load returns
// null on missing runId, Save+Load round-trips, Delete clears, concurrent
// runIds don't collide. Both core adapters (InMemory + File) derive from
// the shared shape (the satellite adapters Redis/Mongo/Sqlite/Cosmos run
// the same contract from their respective integration test projects).
public class InMemoryAgentRunStoreTests : AgentRunStoreContract
{
    protected override IAgentRunStore CreateStore() => new InMemoryAgentRunStore();
}

public class FileAgentRunStoreTests : AgentRunStoreContract, IDisposable
{
    private readonly string _tempDir;

    public FileAgentRunStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "webreaper-agent-tests-" + Guid.NewGuid().ToString("N"));
    }

    protected override IAgentRunStore CreateStore() => new FileAgentRunStore(_tempDir);

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public async Task File_runId_with_filesystem_unfriendly_chars_round_trips()
    {
        var store = CreateStore();
        var awkward = "run/with:weird?chars*";
        var snapshot = SampleSnapshot();

        await store.SaveStepAsync(awkward, new AgentDecision.Stop { Reason = "ok" }, snapshot);
        var loaded = await store.LoadAsync(awkward);

        Assert.NotNull(loaded);
        Assert.Equal(snapshot.Goal, loaded!.Goal);
    }

    private static AgentRunSnapshot SampleSnapshot()
        => new("goal", 0, Array.Empty<AgentDecision>(), Array.Empty<string>(), Array.Empty<JsonObject>(), null);
}

public abstract class AgentRunStoreContract
{
    protected abstract IAgentRunStore CreateStore();

    [Fact]
    public async Task LoadAsync_returns_null_for_unknown_runId()
    {
        var store = CreateStore();
        Assert.Null(await store.LoadAsync("never-saved"));
    }

    [Fact]
    public async Task SaveStepAsync_then_LoadAsync_round_trips_all_fields()
    {
        var store = CreateStore();
        var schema = new Schema { new SchemaElement("title", "h1") };
        var record = new JsonObject { ["title"] = "hello", ["url"] = "https://example.com/" };
        var snapshot = new AgentRunSnapshot(
            Goal: "get titles",
            LastDecidedStep: 3,
            History: new AgentDecision[]
            {
                new AgentDecision.Follow("https://example.com/p2") { Reason = "next" },
                new AgentDecision.Extract(schema) { Reason = "rich page" },
                new AgentDecision.Stop { Reason = "done" }
            },
            VisitedUrls: new[] { "https://example.com/", "https://example.com/p2" },
            Records: new[] { record },
            CurrentUrl: "https://example.com/p2");

        await store.SaveStepAsync("r1",
            new AgentDecision.Stop { Reason = "done" }, snapshot);

        var loaded = await store.LoadAsync("r1");

        Assert.NotNull(loaded);
        Assert.Equal("get titles", loaded!.Goal);
        Assert.Equal(3, loaded.LastDecidedStep);
        Assert.Equal(3, loaded.History.Count);
        Assert.Equal(2, loaded.VisitedUrls.Count);
        Assert.Single(loaded.Records);
        Assert.Equal("hello", loaded.Records[0]["title"]?.GetValue<string>());
        Assert.Equal("https://example.com/p2", loaded.CurrentUrl);

        // Each decision arm survives the round-trip with its type identity.
        Assert.IsType<AgentDecision.Follow>(loaded.History[0]);
        Assert.IsType<AgentDecision.Extract>(loaded.History[1]);
        Assert.IsType<AgentDecision.Stop>(loaded.History[2]);
    }

    [Fact]
    public async Task DeleteAsync_clears_the_snapshot()
    {
        var store = CreateStore();
        var snapshot = new AgentRunSnapshot("g", 0,
            Array.Empty<AgentDecision>(), Array.Empty<string>(), Array.Empty<JsonObject>(), null);

        await store.SaveStepAsync("r1", new AgentDecision.Stop { Reason = "x" }, snapshot);
        await store.DeleteAsync("r1");

        Assert.Null(await store.LoadAsync("r1"));
    }

    [Fact]
    public async Task DeleteAsync_on_missing_runId_is_idempotent()
    {
        var store = CreateStore();
        // Should not throw.
        await store.DeleteAsync("never-existed");
    }

    [Fact]
    public async Task Concurrent_runIds_do_not_collide()
    {
        var store = CreateStore();
        var s1 = new AgentRunSnapshot("g1", 1,
            Array.Empty<AgentDecision>(), new[] { "u1" }, Array.Empty<JsonObject>(), null);
        var s2 = new AgentRunSnapshot("g2", 2,
            Array.Empty<AgentDecision>(), new[] { "u2" }, Array.Empty<JsonObject>(), null);

        await store.SaveStepAsync("r1", new AgentDecision.Stop { Reason = "" }, s1);
        await store.SaveStepAsync("r2", new AgentDecision.Stop { Reason = "" }, s2);

        var loaded1 = await store.LoadAsync("r1");
        var loaded2 = await store.LoadAsync("r2");

        Assert.Equal("g1", loaded1?.Goal);
        Assert.Equal("g2", loaded2?.Goal);
        Assert.Equal(new[] { "u1" }, loaded1?.VisitedUrls);
        Assert.Equal(new[] { "u2" }, loaded2?.VisitedUrls);
    }
}
