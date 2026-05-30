namespace WebReaper.Domain.Telemetry;

/// <summary>
/// Per-run summary returned by
/// <see cref="WebReaper.Core.ScraperEngine.RunAsync"/> and exposed via
/// <c>WebReaper.Domain.Agent.AgentResult.Report</c> (ADR-0066).
/// <para>
/// The <see cref="Llm"/> field is typed as <see cref="object"/> in core
/// to keep the satellite quarantine (ADR-0009) — when the
/// <c>WebReaper.AI</c> satellite is referenced and wired into the run,
/// this is a <c>WebReaper.AI.Llm.LlmTelemetrySnapshot</c>; consumers
/// cast to that type. <c>null</c> when no LLM adapter ran on the
/// engine — the run had no telemetry to aggregate.
/// </para>
/// </summary>
/// <param name="Llm">Opaque LLM telemetry snapshot. <c>null</c> when no
/// LLM adapter ran. Cast to
/// <c>WebReaper.AI.Llm.LlmTelemetrySnapshot</c> when the AI satellite
/// is in use.</param>
/// <param name="Duration">Wall-clock time from <c>RunAsync</c> entry to
/// completion.</param>
/// <param name="BlockedPageCount">The number of pages the block detector
/// (ADR-0083) flagged as blocked (<c>IsBlocked</c>) during the run. For a
/// single-URL scrape this is 0 or 1; for a crawl it counts every page whose
/// load looked like a bot-check challenge.</param>
public sealed record RunReport(
    object? Llm,
    TimeSpan Duration,
    int BlockedPageCount = 0);
