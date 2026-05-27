using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using WebReaper.AI.Llm;
using WebReaper.Domain.PageActions;

namespace WebReaper.AI.Tools;

/// <summary>
/// The arm-local tool projections for every <see cref="PageAction"/> arm
/// (ADR-0060 amendment 2026-05-28). One nested static class per arm:
/// each owns its <see cref="Click.Name"/> (the LLM tool name), its
/// <see cref="Click.Descriptor"/> (the hand-rolled JSON Schema as an
/// <see cref="AIFunction"/>), and its <see cref="Click.FromArguments"/>
/// factory (the per-arm <see cref="JsonElement"/> argument extractor
/// returning a <see cref="ToolCallResult{T}"/>).
/// <para>
/// Before the amendment, the JSON schemas lived in
/// <see cref="AgentDecisionTools"/>'s ~280-line factory list and the
/// argument-extraction logic lived in
/// <see cref="WebReaper.AI.LlmAgentBrain"/>'s 101-line
/// <c>ParseDecisionTool</c> and <see cref="WebReaper.AI.LlmActionResolver"/>'s
/// 26-line <c>ParseActionTool</c> — three places to edit when adding an
/// arm. The amendment co-locates all three concerns per arm so adding an
/// arm means adding one nested static class here and one line to each
/// of the brain's and resolver's switch + the
/// <see cref="AgentDecisionTools.ForBrain"/> / <see cref="AgentDecisionTools.ForResolver"/>
/// registration lists.
/// </para>
/// <para>
/// AOT-friendly per ADR-0060 fork 4 — the schemas are hand-rolled
/// <see cref="JsonObject"/> literals, no reflection, no runtime code-gen.
/// Lives in the satellite, not core, so <see cref="PageAction"/> stays
/// AI-dependency-free (ADR-0009 quarantine).
/// </para>
/// <para>
/// Visibility: <c>internal</c> in v10.0.2 (symmetric with
/// <see cref="AgentDecisionTools"/>). Consumer-authored brain or
/// resolver adapters call into the LLM-tool surface via
/// <see cref="LlmCall{TResponse}"/> + their own descriptor; if a future
/// consumer use case for reusing these standard tool descriptors
/// surfaces, the class can be made public in a minor release without
/// breaking anyone (visibility widening is non-breaking).
/// </para>
/// </summary>
internal static class PageActionTools
{
    // ---- Click --------------------------------------------------------------

    /// <summary>Tool projection of <see cref="PageAction.Click"/>.</summary>
    public static class Click
    {
        /// <summary>The LLM tool name the model emits for this arm.</summary>
        public const string Name = "ActClick";

        /// <summary>The hand-rolled JSON Schema for this arm's tool call.</summary>
        public static AIFunction Descriptor { get; } = new HandRolledAIFunction(
            name: Name,
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

        /// <summary>Construct the arm from a tool call's argument JSON, or
        /// report why construction failed.</summary>
        public static ToolCallResult<PageAction.Click> FromArguments(JsonElement args)
        {
            var selector = LlmToolArguments.TryGetString(args, "selector");
            return string.IsNullOrWhiteSpace(selector)
                ? ToolCallResult<PageAction.Click>.Failed("missing 'selector'")
                : ToolCallResult<PageAction.Click>.Ok(new PageAction.Click(selector));
        }
    }

    // ---- Wait ---------------------------------------------------------------

    /// <summary>Tool projection of <see cref="PageAction.Wait"/>.</summary>
    public static class Wait
    {
        public const string Name = "ActWait";

        public static AIFunction Descriptor { get; } = new HandRolledAIFunction(
            name: Name,
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

        /// <summary>Always succeeds — missing or malformed <c>ms</c> defaults
        /// to <c>0</c>, matching the pre-amendment behaviour where ActWait
        /// without an integer was treated as a zero-length wait.</summary>
        public static ToolCallResult<PageAction.Wait> FromArguments(JsonElement args) =>
            ToolCallResult<PageAction.Wait>.Ok(
                new PageAction.Wait(LlmToolArguments.TryGetInt(args, "ms") ?? 0));
    }

    // ---- WaitForSelector ----------------------------------------------------

    /// <summary>Tool projection of <see cref="PageAction.WaitForSelector"/>.</summary>
    public static class WaitForSelector
    {
        public const string Name = "ActWaitForSelector";

        public static AIFunction Descriptor { get; } = new HandRolledAIFunction(
            name: Name,
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

        public static ToolCallResult<PageAction.WaitForSelector> FromArguments(JsonElement args)
        {
            var selector = LlmToolArguments.TryGetString(args, "selector");
            if (string.IsNullOrWhiteSpace(selector))
                return ToolCallResult<PageAction.WaitForSelector>.Failed("missing 'selector'");
            var timeout = LlmToolArguments.TryGetInt(args, "timeoutMs") ?? 30_000;
            return ToolCallResult<PageAction.WaitForSelector>.Ok(
                new PageAction.WaitForSelector(selector, timeout));
        }
    }

    // ---- WaitForNetworkIdle -------------------------------------------------

    /// <summary>Tool projection of <see cref="PageAction.WaitForNetworkIdle"/>.</summary>
    public static class WaitForNetworkIdle
    {
        public const string Name = "ActWaitForNetworkIdle";

        public static AIFunction Descriptor { get; } = new HandRolledAIFunction(
            name: Name,
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

        /// <summary>Always succeeds — the arm carries no required arguments.</summary>
        public static ToolCallResult<PageAction.WaitForNetworkIdle> FromArguments(JsonElement args) =>
            ToolCallResult<PageAction.WaitForNetworkIdle>.Ok(new PageAction.WaitForNetworkIdle());
    }

    // ---- ScrollToEnd --------------------------------------------------------

    /// <summary>Tool projection of <see cref="PageAction.ScrollToEnd"/>.</summary>
    public static class ScrollToEnd
    {
        public const string Name = "ActScrollToEnd";

        public static AIFunction Descriptor { get; } = new HandRolledAIFunction(
            name: Name,
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

        /// <summary>Always succeeds — the arm carries no required arguments.</summary>
        public static ToolCallResult<PageAction.ScrollToEnd> FromArguments(JsonElement args) =>
            ToolCallResult<PageAction.ScrollToEnd>.Ok(new PageAction.ScrollToEnd());
    }

    // ---- EvaluateExpression -------------------------------------------------

    /// <summary>Tool projection of <see cref="PageAction.EvaluateExpression"/>.</summary>
    public static class EvaluateExpression
    {
        public const string Name = "ActEvaluate";

        public static AIFunction Descriptor { get; } = new HandRolledAIFunction(
            name: Name,
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

        public static ToolCallResult<PageAction.EvaluateExpression> FromArguments(JsonElement args)
        {
            var expression = LlmToolArguments.TryGetString(args, "expression");
            return string.IsNullOrWhiteSpace(expression)
                ? ToolCallResult<PageAction.EvaluateExpression>.Failed("missing 'expression'")
                : ToolCallResult<PageAction.EvaluateExpression>.Ok(
                    new PageAction.EvaluateExpression(expression));
        }
    }

    // ---- SemanticAct (brain-only — never registered on resolver) ------------

    /// <summary>Tool projection of <see cref="PageAction.SemanticAct"/>.
    /// Brain-only — never appears in
    /// <see cref="AgentDecisionTools.ForResolver"/> (would let the resolver
    /// return a SemanticAct arm and loop the transport's resolution
    /// path; fork 8 verdict — the closed sum is closed at the resolver's
    /// tool list, structurally).</summary>
    public static class SemanticAct
    {
        public const string Name = "ActSemanticAct";

        public static AIFunction Descriptor { get; } = new HandRolledAIFunction(
            name: Name,
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

        public static ToolCallResult<PageAction.SemanticAct> FromArguments(JsonElement args)
        {
            var intent = LlmToolArguments.TryGetString(args, "intent");
            return string.IsNullOrWhiteSpace(intent)
                ? ToolCallResult<PageAction.SemanticAct>.Failed("missing 'intent'")
                : ToolCallResult<PageAction.SemanticAct>.Ok(new PageAction.SemanticAct(intent));
        }
    }
}
