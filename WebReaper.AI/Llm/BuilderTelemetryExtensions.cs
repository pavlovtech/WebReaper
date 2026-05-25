using System.Runtime.CompilerServices;
using WebReaper.Builders;
using WebReaper.Domain.Telemetry;

namespace WebReaper.AI.Llm;

/// <summary>
/// Satellite-side bridge between the core builders' public
/// <see cref="ScraperEngineBuilder.TelemetryHooks"/> /
/// <see cref="AgentEngineBuilder.TelemetryHooks"/> hook and the
/// satellite's typed <see cref="LlmCallTelemetry"/> accumulator (ADR-0066).
/// <para>
/// The typed instance is stored in a per-builder
/// <see cref="ConditionalWeakTable{TKey,TValue}"/> — entries die with the
/// builder; no leak. Multiple <c>WithLlm*</c> calls on the same builder
/// share one accumulator (the first call creates it; subsequent calls
/// reuse). The builder's public <c>TelemetryHooks</c> is set to a
/// <see cref="RunTelemetryHooks"/> wrapping the typed instance — the
/// engine consumes the hooks through the AI-clean core surface, never
/// seeing the satellite type.
/// </para>
/// </summary>
internal static class BuilderTelemetryExtensions
{
    private static readonly ConditionalWeakTable<ScraperEngineBuilder, ILlmCallTelemetry>
        ScraperTelemetry = new();

    private static readonly ConditionalWeakTable<AgentEngineBuilder, ILlmCallTelemetry>
        AgentTelemetry = new();

    /// <summary>Get-or-create the LlmCallTelemetry for this scraper
    /// builder. Idempotent — first call materialises the accumulator
    /// and registers a <see cref="RunTelemetryHooks"/> on the builder;
    /// subsequent calls return the same instance.</summary>
    internal static ILlmCallTelemetry GetOrCreateLlmTelemetry(this ScraperEngineBuilder builder)
        => ScraperTelemetry.GetValue(builder, b =>
        {
            var telemetry = new LlmCallTelemetry();
            // ??= — preserves any TelemetryHooks the consumer may have
            // pre-set on the builder; satellites composing on top of a
            // bespoke hooks instance shouldn't trample it. Edge case;
            // the typed satellite accumulator that gets returned still
            // wraps successful Record calls, but they go to a separate
            // accumulator not the consumer's — documented surprise.
            b.TelemetryHooks ??= MakeHooks(telemetry);
            return telemetry;
        });

    /// <summary>Get-or-create the LlmCallTelemetry for this agent
    /// builder. See the scraper overload for semantics.</summary>
    internal static ILlmCallTelemetry GetOrCreateLlmTelemetry(this AgentEngineBuilder builder)
        => AgentTelemetry.GetValue(builder, b =>
        {
            var telemetry = new LlmCallTelemetry();
            b.TelemetryHooks ??= MakeHooks(telemetry);
            return telemetry;
        });

    private static RunTelemetryHooks MakeHooks(LlmCallTelemetry telemetry)
        => new(
            Snapshot: () => telemetry.Snapshot(),
            Reset: telemetry.Reset,
            TotalLlmTokens: () => telemetry.Snapshot().TotalTokens);
}
