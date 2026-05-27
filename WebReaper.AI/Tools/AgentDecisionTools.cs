using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using WebReaper.AI.Llm;
using WebReaper.Domain.Agent;
using WebReaper.Domain.PageActions;
using WebReaper.Domain.Parsing;

namespace WebReaper.AI.Tools;

/// <summary>
/// The closed-sum-as-tools registries (ADR-0060, ADR-0060 amendment
/// 2026-05-28). Holds the per-arm tool projections for the three
/// <see cref="AgentDecision"/> arms whose tool shape is NOT shared with
/// <see cref="PageAction"/> (<see cref="Extract"/>, <see cref="Follow"/>,
/// <see cref="Stop"/>), plus the two registration lists
/// (<see cref="ForBrain"/>, <see cref="ForResolver"/>) that compose the
/// per-arm <see cref="AIFunction"/> descriptors into the lists the brain
/// and resolver register with <see cref="ChatOptions.Tools"/>.
/// <para>
/// Hand-rolled JSON Schema per arm (fork 4 — AOT-friendly, no
/// reflection-built schemas). After the amendment each arm's tool
/// concerns (Name + Descriptor + FromArguments) live in one nested
/// static class instead of being scattered across this file's factory
/// list, the brain's parser switch, and the resolver's parser switch.
/// </para>
/// <para>
/// Brain registry: 10 tools — <see cref="Extract"/>, <see cref="Follow"/>,
/// <see cref="Stop"/>, plus 7 flat <c>Act*</c> arms (the seven
/// <see cref="PageAction"/> arms including
/// <see cref="PageAction.SemanticAct"/>). The flat packaging (fork 2)
/// keeps every arm's schema simple; the model picks one; the SDK
/// validates the args against the per-arm schema.
/// </para>
/// <para>
/// Resolver registry: 6 tools — the six concrete <see cref="PageAction"/>
/// arms, ever. No <see cref="PageAction.SemanticAct"/> on the resolver
/// (fork 8 — the closed sum is structural at the resolver tool list; the
/// model literally cannot emit a <c>SemanticAct</c>-loop arm).
/// </para>
/// </summary>
internal static class AgentDecisionTools
{
    // ---- Per-arm tool projections (AgentDecision-specific arms) -------------

    /// <summary>Tool projection of <see cref="AgentDecision.Extract"/>.</summary>
    public static class Extract
    {
        public const string Name = "Extract";

        public static AIFunction Descriptor { get; } = new HandRolledAIFunction(
            name: Name,
            description:
                "Extract a record from the current page using a flat field-to-CSS-selector " +
                "schema. Use when the current page contains the records you want to capture. " +
                "Single-level only; no nested objects in v1.",
            parametersSchema: new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["schema"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["description"] = "Map of output field name to CSS selector. Single level — no nested objects.",
                        ["additionalProperties"] = new JsonObject { ["type"] = "string" },
                    },
                    ["reason"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Why this is the right next step.",
                    },
                },
                ["required"] = new JsonArray { "schema", "reason" },
            });

        /// <summary>Construct the arm from a tool call's argument JSON plus
        /// the audit-trail reason. Two failure modes:
        /// <c>"missing 'schema'"</c> when the schema property is absent or
        /// non-object, and <c>"schema was empty"</c> when the schema parses
        /// but yields no usable field/selector pairs (the v1 single-level
        /// flat shape rejects nested objects — they round-trip to an empty
        /// Schema).</summary>
        public static ToolCallResult<AgentDecision.Extract> FromArguments(JsonElement args, string reason)
        {
            if (!args.TryGetProperty("schema", out var schemaEl) ||
                schemaEl.ValueKind != JsonValueKind.Object)
            {
                return ToolCallResult<AgentDecision.Extract>.Failed("missing 'schema'");
            }
            var schema = BuildFlatSchema(schemaEl);
            if (schema.Children.Count == 0)
            {
                return ToolCallResult<AgentDecision.Extract>.Failed("schema was empty");
            }
            return ToolCallResult<AgentDecision.Extract>.Ok(
                new AgentDecision.Extract(schema) { Reason = reason });
        }

        // v1: single-level flat schema { "field": "selector", ... }. Nested
        // schemas (objects-within-objects, lists-of-objects) are a v2
        // deferral matching ADR-0045's source-gen v1 constraint. Kept
        // arm-local — the brain's Extract is the only consumer; the
        // schema-inferrer adapter has its own (more lenient) flattener
        // that handles two LLM-emitted shapes.
        private static Schema BuildFlatSchema(JsonElement schemaEl)
        {
            var s = new Schema();
            foreach (var prop in schemaEl.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.String) continue;
                var fieldName = prop.Name;
                var selector = prop.Value.GetString();
                if (string.IsNullOrWhiteSpace(fieldName) || string.IsNullOrWhiteSpace(selector)) continue;
                s.Add(new SchemaElement(fieldName, selector));
            }
            return s;
        }
    }

    /// <summary>Tool projection of <see cref="AgentDecision.Follow"/>.</summary>
    public static class Follow
    {
        public const string Name = "Follow";

        public static AIFunction Descriptor { get; } = new HandRolledAIFunction(
            name: Name,
            description:
                "Load a candidate URL as the next agent step. Pick the URL from the listed " +
                "candidate URLs on the current page. Do not propose a URL you have already " +
                "visited (the engine will reject it).",
            parametersSchema: new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["url"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Absolute URL chosen from the candidate list.",
                    },
                    ["reason"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Why this URL is the right next step.",
                    },
                },
                ["required"] = new JsonArray { "url", "reason" },
            });

        public static ToolCallResult<AgentDecision.Follow> FromArguments(JsonElement args, string reason)
        {
            var url = LlmToolArguments.TryGetString(args, "url");
            return string.IsNullOrWhiteSpace(url)
                ? ToolCallResult<AgentDecision.Follow>.Failed("missing 'url'")
                : ToolCallResult<AgentDecision.Follow>.Ok(
                    new AgentDecision.Follow(url) { Reason = reason });
        }
    }

    /// <summary>Tool projection of <see cref="AgentDecision.Stop"/>.</summary>
    public static class Stop
    {
        public const string Name = "Stop";

        public static AIFunction Descriptor { get; } = new HandRolledAIFunction(
            name: Name,
            description:
                "Terminate the agent run. Use when the goal is satisfied OR the page set has " +
                "been exhausted without further progress.",
            parametersSchema: new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["reason"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Why the run is stopping — goal satisfied, dead-end, etc.",
                    },
                },
                ["required"] = new JsonArray { "reason" },
            });

        /// <summary>Always succeeds — Stop has no required arguments beyond
        /// the audit-trail <c>Reason</c>. The <paramref name="args"/>
        /// JsonElement is ignored.</summary>
        public static ToolCallResult<AgentDecision.Stop> FromArguments(JsonElement args, string reason) =>
            ToolCallResult<AgentDecision.Stop>.Ok(new AgentDecision.Stop { Reason = reason });
    }

    // ---- Registration lists --------------------------------------------------

    /// <summary>The brain's 13-tool registry: every <see cref="AgentDecision"/>
    /// arm with the ten <see cref="PageAction"/> arms flat-packed (seven
    /// original + <see cref="PageAction.Press"/>,
    /// <see cref="PageAction.ScrollIntoView"/>, and <see cref="PageAction.Fill"/>
    /// from ADR-0074).</summary>
    public static IReadOnlyList<AIFunction> ForBrain() =>
    [
        Extract.Descriptor,
        Follow.Descriptor,
        Stop.Descriptor,
        PageActionTools.Click.Descriptor,
        PageActionTools.Wait.Descriptor,
        PageActionTools.WaitForSelector.Descriptor,
        PageActionTools.WaitForNetworkIdle.Descriptor,
        PageActionTools.ScrollToEnd.Descriptor,
        PageActionTools.ScrollIntoView.Descriptor,
        PageActionTools.EvaluateExpression.Descriptor,
        PageActionTools.SemanticAct.Descriptor,
        PageActionTools.Press.Descriptor,
        PageActionTools.Fill.Descriptor,
    ];

    /// <summary>The resolver's 9-tool registry: the nine concrete
    /// <see cref="PageAction"/> arms (six original + <see cref="PageAction.Press"/>,
    /// <see cref="PageAction.ScrollIntoView"/>, and <see cref="PageAction.Fill"/>
    /// from ADR-0074). No <see cref="PageAction.SemanticAct"/>; structurally
    /// prevents the resolver from looping the transport (fork 8).</summary>
    public static IReadOnlyList<AIFunction> ForResolver() =>
    [
        PageActionTools.Click.Descriptor,
        PageActionTools.Wait.Descriptor,
        PageActionTools.WaitForSelector.Descriptor,
        PageActionTools.WaitForNetworkIdle.Descriptor,
        PageActionTools.ScrollToEnd.Descriptor,
        PageActionTools.ScrollIntoView.Descriptor,
        PageActionTools.EvaluateExpression.Descriptor,
        PageActionTools.Press.Descriptor,
        PageActionTools.Fill.Descriptor,
    ];
}
