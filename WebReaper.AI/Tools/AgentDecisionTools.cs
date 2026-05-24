using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace WebReaper.AI.Tools;

/// <summary>
/// The closed-sum-as-tools registries (ADR-0060). Each
/// <see cref="WebReaper.Domain.Agent.AgentDecision"/> arm is one tool on
/// the brain's registry; each concrete <see cref="WebReaper.Domain.PageActions.PageAction"/>
/// arm (the six non-<c>SemanticAct</c> arms) is one tool on the resolver's
/// registry. Hand-rolled JSON Schema per arm (fork 4 — AOT-friendly, no
/// reflection-built schemas).
/// <para>
/// Brain registry: 10 tools — <c>Extract</c>, <c>Follow</c>, <c>Stop</c>,
/// plus 7 flat <c>Act*</c> arms (the seven <see cref="WebReaper.Domain.PageActions.PageAction"/>
/// arms including <c>ActSemanticAct</c>, so the brain can still hand the
/// resolver an intent). The flat packaging (fork 2 verdict) keeps every
/// arm's schema simple; the model picks one; the SDK validates the args
/// against the per-arm schema.
/// </para>
/// <para>
/// Resolver registry: 6 tools — the six concrete arms, ever. No
/// <c>ActSemanticAct</c> on the resolver (fork 8 verdict — the closed sum
/// is structural at the resolver tool list; the model literally cannot
/// emit a <c>SemanticAct</c>-loop arm).
/// </para>
/// </summary>
internal static class AgentDecisionTools
{
    /// <summary>The brain's 10-tool registry — every <see cref="WebReaper.Domain.Agent.AgentDecision"/>
    /// arm with the seven <c>PageAction</c> arms flat-packed.</summary>
    public static IReadOnlyList<AIFunction> ForBrain() =>
    [
        ExtractTool(),
        FollowTool(),
        StopTool(),
        ActClickTool(),
        ActWaitTool(),
        ActWaitForSelectorTool(),
        ActWaitForNetworkIdleTool(),
        ActScrollToEndTool(),
        ActEvaluateTool(),
        ActSemanticActTool(),
    ];

    /// <summary>The resolver's 6-tool registry — the six concrete
    /// <see cref="WebReaper.Domain.PageActions.PageAction"/> arms. No
    /// <c>ActSemanticAct</c> — structurally prevents the resolver from
    /// looping the transport (fork 8).</summary>
    public static IReadOnlyList<AIFunction> ForResolver() =>
    [
        ActClickTool(),
        ActWaitTool(),
        ActWaitForSelectorTool(),
        ActWaitForNetworkIdleTool(),
        ActScrollToEndTool(),
        ActEvaluateTool(),
    ];

    // ---- AgentDecision arms (brain-only) --------------------------------

    internal static AIFunction ExtractTool() => new HandRolledAIFunction(
        name: "Extract",
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

    internal static AIFunction FollowTool() => new HandRolledAIFunction(
        name: "Follow",
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

    internal static AIFunction StopTool() => new HandRolledAIFunction(
        name: "Stop",
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

    // ---- PageAction arms (brain + resolver) -----------------------------

    internal static AIFunction ActClickTool() => new HandRolledAIFunction(
        name: "ActClick",
        description:
            "Click the element matching a CSS selector. Use for buttons, links, or any " +
            "clickable element. Prefer id over class, class over tag; combine if needed.",
        parametersSchema: new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["selector"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "CSS selector for the element to click.",
                },
                ["reason"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Why this click is the right next step. (Brain only — resolver ignores.)",
                },
            },
            ["required"] = new JsonArray { "selector" },
        });

    internal static AIFunction ActWaitTool() => new HandRolledAIFunction(
        name: "ActWait",
        description:
            "Wait a fixed number of milliseconds — let scripted content settle, throttle " +
            "post-action work, etc.",
        parametersSchema: new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["ms"] = new JsonObject
                {
                    ["type"] = "integer",
                    ["description"] = "Milliseconds to wait.",
                },
                ["reason"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Why this wait is the right next step. (Brain only — resolver ignores.)",
                },
            },
            ["required"] = new JsonArray { "ms" },
        });

    internal static AIFunction ActWaitForSelectorTool() => new HandRolledAIFunction(
        name: "ActWaitForSelector",
        description:
            "Wait until an element matching a CSS selector appears on the page, up to a " +
            "timeout. Use for modals, lazy-loaded content, async-rendered components.",
        parametersSchema: new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["selector"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "CSS selector to wait for.",
                },
                ["timeoutMs"] = new JsonObject
                {
                    ["type"] = "integer",
                    ["description"] = "Maximum milliseconds to wait. Defaults to 30000 if omitted.",
                },
                ["reason"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Why this wait is the right next step. (Brain only — resolver ignores.)",
                },
            },
            ["required"] = new JsonArray { "selector" },
        });

    internal static AIFunction ActWaitForNetworkIdleTool() => new HandRolledAIFunction(
        name: "ActWaitForNetworkIdle",
        description:
            "Wait until the page's network activity goes idle — useful after an action " +
            "that triggers async fetches before extracting.",
        parametersSchema: new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["reason"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Why this wait is the right next step. (Brain only — resolver ignores.)",
                },
            },
            ["required"] = new JsonArray(),
        });

    internal static AIFunction ActScrollToEndTool() => new HandRolledAIFunction(
        name: "ActScrollToEnd",
        description:
            "Scroll to the bottom of the page — trigger infinite-scroll loading, reveal " +
            "lazy-loaded items below the fold.",
        parametersSchema: new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["reason"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Why this scroll is the right next step. (Brain only — resolver ignores.)",
                },
            },
            ["required"] = new JsonArray(),
        });

    internal static AIFunction ActEvaluateTool() => new HandRolledAIFunction(
        name: "ActEvaluate",
        description:
            "Evaluate a JavaScript expression in the page context — last resort for " +
            "actions no other arm covers.",
        parametersSchema: new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["expression"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "JavaScript expression to evaluate in the page context.",
                },
                ["reason"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Why this evaluation is the right next step. (Brain only — resolver ignores.)",
                },
            },
            ["required"] = new JsonArray { "expression" },
        });

    /// <summary>Brain-only — the resolver MUST NOT register this tool
    /// (would let the resolver return a <c>SemanticAct</c> arm and loop
    /// the transport's resolution path). Fork 8 — the closed sum is closed
    /// at the resolver's tool list, structurally.</summary>
    internal static AIFunction ActSemanticActTool() => new HandRolledAIFunction(
        name: "ActSemanticAct",
        description:
            "Hand a natural-language intent to the configured action resolver — the " +
            "resolver picks the concrete arm. Brain-only; the resolver itself never " +
            "registers this tool (would loop).",
        parametersSchema: new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["intent"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Natural-language intent, e.g. 'click sign in' or 'open the modal'.",
                },
                ["reason"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Why this intent is the right next step.",
                },
            },
            ["required"] = new JsonArray { "intent" },
        });
}
