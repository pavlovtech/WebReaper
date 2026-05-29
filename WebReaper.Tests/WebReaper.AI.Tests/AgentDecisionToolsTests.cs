using System.Text.Json;
using Microsoft.Extensions.AI;
using WebReaper.AI.Tools;
using WebReaper.Domain.Agent;
using WebReaper.Domain.PageActions;

namespace WebReaper.AI.Tests;

// ADR-0060 + ADR-0074: schema-snapshot tests pinning the tool registry shape.
// The brain's 13 tools and the resolver's 9 tools — names, parameter
// schemas, structural property (no ActSemanticAct on the resolver).
//
// These are the "would-bite-you-on-rename" tests: an arm rename
// (or accidental removal) flips the test count, the registry name,
// or the schema. Hand-rolled JSON Schema means there's nothing else
// to verify the shape.
public class AgentDecisionToolsTests
{
    // ---- Registry composition ----------------------------------------------

    [Fact]
    public void Brain_registry_has_exactly_thirteen_tools()
    {
        var tools = AgentDecisionTools.ForBrain();
        Assert.Equal(13, tools.Count);   // +3 ADR-0074 arms: Press, ScrollIntoView, Fill
    }

    [Fact]
    public void Resolver_registry_has_exactly_nine_tools()
    {
        var tools = AgentDecisionTools.ForResolver();
        Assert.Equal(9, tools.Count);   // +3 ADR-0074 arms: Press, ScrollIntoView, Fill
    }

    [Fact]
    public void Brain_registry_names_match_expected_arms()
    {
        var names = AgentDecisionTools.ForBrain().Select(t => t.Name).ToHashSet();
        Assert.Equal(
            new HashSet<string>
            {
                "Extract", "Follow", "Stop",
                "ActClick", "ActWait", "ActWaitForSelector",
                "ActWaitForNetworkIdle", "ActScrollToEnd", "ActEvaluate",
                "ActSemanticAct",
                // ADR-0074 arms
                "ActScrollIntoView", "ActPress", "ActFill"
            },
            names);
    }

    [Fact]
    public void Resolver_registry_names_match_expected_arms_and_exclude_ActSemanticAct()
    {
        var names = AgentDecisionTools.ForResolver().Select(t => t.Name).ToHashSet();
        Assert.Equal(
            new HashSet<string>
            {
                "ActClick", "ActWait", "ActWaitForSelector",
                "ActWaitForNetworkIdle", "ActScrollToEnd", "ActEvaluate",
                // ADR-0074 arms (still no ActSemanticAct — fork 8)
                "ActScrollIntoView", "ActPress", "ActFill"
            },
            names);

        // The structural loop-prevention from fork 8: the resolver's
        // registry NEVER includes ActSemanticAct. This is the closed sum
        // closed at the LLM boundary.
        Assert.DoesNotContain("ActSemanticAct", names);
    }

    // ---- Per-tool parameter schemas ----------------------------------------

    [Fact]
    public void Extract_tool_schema_is_flat_field_to_selector_map()
    {
        var tool = AgentDecisionTools.ForBrain().Single(t => t.Name == "Extract");
        var schema = tool.JsonSchema;

        AssertSchemaIsObject(schema);
        var props = schema.GetProperty("properties");

        // schema property
        var schemaProp = props.GetProperty("schema");
        Assert.Equal("object", schemaProp.GetProperty("type").GetString());
        Assert.Equal("string",
            schemaProp.GetProperty("additionalProperties").GetProperty("type").GetString());

        // reason property
        Assert.Equal("string", props.GetProperty("reason").GetProperty("type").GetString());

        AssertRequired(schema, "schema", "reason");
    }

    [Fact]
    public void Follow_tool_schema_requires_url_and_reason()
    {
        var tool = AgentDecisionTools.ForBrain().Single(t => t.Name == "Follow");
        var schema = tool.JsonSchema;

        AssertSchemaIsObject(schema);
        var props = schema.GetProperty("properties");
        Assert.Equal("string", props.GetProperty("url").GetProperty("type").GetString());
        Assert.Equal("string", props.GetProperty("reason").GetProperty("type").GetString());
        AssertRequired(schema, "url", "reason");
    }

    [Fact]
    public void Stop_tool_schema_requires_only_reason()
    {
        var tool = AgentDecisionTools.ForBrain().Single(t => t.Name == "Stop");
        var schema = tool.JsonSchema;

        AssertSchemaIsObject(schema);
        var props = schema.GetProperty("properties");
        Assert.Equal("string", props.GetProperty("reason").GetProperty("type").GetString());
        AssertRequired(schema, "reason");
    }

    [Fact]
    public void ActClick_schema_requires_selector_string()
    {
        var tool = AgentDecisionTools.ForResolver().Single(t => t.Name == "ActClick");
        var schema = tool.JsonSchema;

        AssertSchemaIsObject(schema);
        Assert.Equal("string", schema.GetProperty("properties").GetProperty("selector").GetProperty("type").GetString());
        AssertRequired(schema, "selector");
    }

    [Fact]
    public void ActWait_schema_requires_ms_integer()
    {
        var tool = AgentDecisionTools.ForResolver().Single(t => t.Name == "ActWait");
        var schema = tool.JsonSchema;

        AssertSchemaIsObject(schema);
        Assert.Equal("integer", schema.GetProperty("properties").GetProperty("ms").GetProperty("type").GetString());
        AssertRequired(schema, "ms");
    }

    [Fact]
    public void ActWaitForSelector_schema_requires_selector_optional_timeoutMs()
    {
        var tool = AgentDecisionTools.ForResolver().Single(t => t.Name == "ActWaitForSelector");
        var schema = tool.JsonSchema;

        AssertSchemaIsObject(schema);
        var props = schema.GetProperty("properties");
        Assert.Equal("string", props.GetProperty("selector").GetProperty("type").GetString());
        Assert.Equal("integer", props.GetProperty("timeoutMs").GetProperty("type").GetString());
        // timeoutMs is optional (defaults to 30000 in ParseToolCall).
        AssertRequired(schema, "selector");
        AssertNotRequired(schema, "timeoutMs");
    }

    [Fact]
    public void ActWaitForNetworkIdle_schema_has_no_required_properties()
    {
        var tool = AgentDecisionTools.ForResolver().Single(t => t.Name == "ActWaitForNetworkIdle");
        var schema = tool.JsonSchema;

        AssertSchemaIsObject(schema);
        // Required is the empty array — no params required.
        var required = schema.GetProperty("required");
        Assert.Equal(JsonValueKind.Array, required.ValueKind);
        Assert.Equal(0, required.GetArrayLength());
    }

    [Fact]
    public void ActScrollToEnd_schema_has_no_required_properties()
    {
        var tool = AgentDecisionTools.ForResolver().Single(t => t.Name == "ActScrollToEnd");
        var schema = tool.JsonSchema;

        AssertSchemaIsObject(schema);
        var required = schema.GetProperty("required");
        Assert.Equal(0, required.GetArrayLength());
    }

    [Fact]
    public void ActEvaluate_schema_requires_expression_string()
    {
        var tool = AgentDecisionTools.ForResolver().Single(t => t.Name == "ActEvaluate");
        var schema = tool.JsonSchema;

        AssertSchemaIsObject(schema);
        Assert.Equal("string", schema.GetProperty("properties").GetProperty("expression").GetProperty("type").GetString());
        AssertRequired(schema, "expression");
    }

    [Fact]
    public void ActSemanticAct_schema_requires_intent_string_brain_only()
    {
        // Brain-only tool — verify it's in the brain registry and absent
        // from the resolver registry.
        var brainTool = AgentDecisionTools.ForBrain().Single(t => t.Name == "ActSemanticAct");
        var schema = brainTool.JsonSchema;

        AssertSchemaIsObject(schema);
        Assert.Equal("string", schema.GetProperty("properties").GetProperty("intent").GetProperty("type").GetString());
        AssertRequired(schema, "intent");

        Assert.DoesNotContain(AgentDecisionTools.ForResolver(), t => t.Name == "ActSemanticAct");
    }

    // ---- Tool metadata pinning ---------------------------------------------

    [Fact]
    public void Every_tool_has_a_non_empty_description()
    {
        foreach (var tool in AgentDecisionTools.ForBrain())
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Description),
                $"tool {tool.Name} must have a description");
        }
        foreach (var tool in AgentDecisionTools.ForResolver())
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Description),
                $"resolver tool {tool.Name} must have a description");
        }
    }

    // ---- Derived-registry parse coverage (ADR-0078 Axis B) -----------------
    //
    // The anti-drift invariant: both registries and both parse paths derive
    // from one PageActionTools.Arms list, so every offered tool is parseable.
    // Pre-derivation this could silently break — an arm in ForBrain() but not
    // in the brain's switch fell through to Stop; in ForResolver() but not the
    // resolver's switch fell through to null — with no error.

    [Fact]
    public void Every_brain_tool_name_resolves_through_ParseBrainTool()
    {
        // Empty args: arms with required fields return Stop("brain X missing
        // ..."), which is fine. The point is that none fall through to the
        // "unregistered tool" branch — every offered tool is recognized.
        foreach (var tool in AgentDecisionTools.ForBrain())
        {
            var decision = AgentDecisionTools.ParseBrainTool(tool.Name, Args("{}"));
            if (decision is AgentDecision.Stop stop)
            {
                Assert.DoesNotContain("unregistered tool", stop.Reason);
            }
        }
    }

    [Fact]
    public void Unregistered_brain_tool_name_is_the_only_unregistered_path()
    {
        var decision = AgentDecisionTools.ParseBrainTool("NotARealTool", Args("{}"));
        var stop = Assert.IsType<AgentDecision.Stop>(decision);
        Assert.Contains("unregistered tool 'NotARealTool'", stop.Reason);
    }

    [Fact]
    public void Every_resolver_tool_name_resolves_through_ParseResolverTool()
    {
        // The resolver returns null for BOTH an unknown tool and a per-arm
        // failure, so the only way to prove "recognized" is valid args ->
        // non-null. ValidArgs must carry an entry per resolver tool; a new
        // resolver arm without one trips the indexer (and this assertion).
        foreach (var tool in AgentDecisionTools.ForResolver())
        {
            var action = AgentDecisionTools.ParseResolverTool(tool.Name, ValidArgs[tool.Name]);
            Assert.NotNull(action);
        }
    }

    [Fact]
    public void Resolver_does_not_parse_ActSemanticAct_even_with_valid_args()
    {
        // Fork 8 at the parse level (registry-absence is pinned above): the
        // brain parses ActSemanticAct into Act(SemanticAct); the resolver does
        // not recognize it at all, even with otherwise-valid arguments.
        var intent = Args("""{ "intent": "open the menu" }""");

        var brain = AgentDecisionTools.ParseBrainTool("ActSemanticAct", intent);
        var act = Assert.IsType<AgentDecision.Act>(brain);
        Assert.IsType<PageAction.SemanticAct>(act.Action);

        Assert.Null(AgentDecisionTools.ParseResolverTool("ActSemanticAct", intent));
    }

    // Minimal valid arguments per resolver tool — enough for FromArguments to
    // succeed so a recognized tool returns a non-null arm.
    private static readonly IReadOnlyDictionary<string, JsonElement> ValidArgs =
        new Dictionary<string, JsonElement>
        {
            ["ActClick"] = Args("""{ "selector": ".x" }"""),
            ["ActWait"] = Args("""{ "ms": 0 }"""),
            ["ActWaitForSelector"] = Args("""{ "selector": ".x" }"""),
            ["ActWaitForNetworkIdle"] = Args("{}"),
            ["ActScrollToEnd"] = Args("{}"),
            ["ActEvaluate"] = Args("""{ "expression": "1" }"""),
            ["ActScrollIntoView"] = Args("""{ "selector": ".x" }"""),
            ["ActPress"] = Args("""{ "key": "Enter" }"""),
            ["ActFill"] = Args("""{ "selector": ".x", "value": "y" }"""),
        };

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement;

    // ---- Helpers -----------------------------------------------------------

    private static void AssertSchemaIsObject(JsonElement schema)
    {
        Assert.Equal(JsonValueKind.Object, schema.ValueKind);
        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.True(schema.TryGetProperty("properties", out var props));
        Assert.Equal(JsonValueKind.Object, props.ValueKind);
        Assert.True(schema.TryGetProperty("required", out var required));
        Assert.Equal(JsonValueKind.Array, required.ValueKind);
    }

    private static void AssertRequired(JsonElement schema, params string[] names)
    {
        var actual = schema.GetProperty("required").EnumerateArray()
            .Select(e => e.GetString()!).ToHashSet();
        foreach (var n in names)
        {
            Assert.Contains(n, actual);
        }
    }

    private static void AssertNotRequired(JsonElement schema, string name)
    {
        var actual = schema.GetProperty("required").EnumerateArray()
            .Select(e => e.GetString()!).ToHashSet();
        Assert.DoesNotContain(name, actual);
    }
}
