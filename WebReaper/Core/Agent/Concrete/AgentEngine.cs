using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using WebReaper.Core.Actions.Abstract;
using WebReaper.Core.Agent.Abstract;
using WebReaper.Core.LinkTracker.Abstract;
using WebReaper.Core.Loaders.Abstract;
using WebReaper.Core.Markdown;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Core.Parser.Concrete;
using WebReaper.Domain.Agent;
using WebReaper.Domain.PageActions;
using WebReaper.Domain.Selectors;
using WebReaper.Domain.Telemetry;
using WebReaper.Extensions;
using WebReaper.Infra.Abstract;
using WebReaper.Processing;
using WebReaper.Processing.Abstract;
using WebReaper.Sinks.Abstract;
using WebReaper.Sinks.Models;

namespace WebReaper.Core.Agent.Concrete;

/// <summary>
/// The in-process Agent driver (ADR-0051) — the second driver shape the
/// library ships alongside <see cref="WebReaper.Core.ScraperEngine"/>'s Crawl
/// driver (ADR-0022). Sequential by design (fork 11): one
/// <see cref="IAgentBrain"/> decision at a time, the next decision depends on
/// the last extract.
/// <para>
/// The driver loop:
/// </para>
/// <list type="number">
/// <item>Resolve <c>runId</c>; load snapshot from <see cref="IAgentRunStore"/>
/// (resume if found, start fresh otherwise).</item>
/// <item>On each step: cap-check, load page, render Markdown view + candidate
/// URLs, build <see cref="AgentState"/>, ask the brain.</item>
/// <item><b>Persist-before-execute</b> — the decision and updated snapshot are
/// written to the store BEFORE the engine acts on it (sink emission /
/// scheduler enqueue / page action). Brain decisions are exactly-once in the
/// persisted history; effects are at-least-once on resume.</item>
/// <item>Execute the decision (Extract → page-processor pipeline → sinks;
/// Follow → set currentUrl; Act → re-load currentUrl with the action;
/// Stop → break).</item>
/// </list>
/// <para>
/// Termination precedence: brain returns <see cref="AgentDecision.Stop"/> →
/// <c>MaxSteps</c> cap → <c>MaxBudgetTokens</c> cap → caller cancellation. On
/// clean termination the engine calls <see cref="IAgentRunStore.DeleteAsync"/>
/// to free the snapshot.
/// </para>
/// </summary>
public sealed class AgentEngine : IAsyncDisposable
{
    private readonly IAgentBrain _brain;
    private readonly IAgentRunStore _runStore;
    private readonly IPageLoader _pageLoader;
    private readonly IContentExtractor _contentExtractor;
    private readonly ISchemaValidator _validator;
    private readonly IActionResolver _actionResolver;
    private readonly IVisitedLinkTracker _visitedTracker;
    // ADR-0076: the post-extraction surface (page-processor pipeline + Sink
    // fan-out) and the warm-up + disposal of its sinks and processors live in
    // one module the driver delegates to — the same module the Crawl driver
    // uses. Closes the pre-0076 gap where the agent never warmed or disposed
    // its sinks (durable sinks silently lost their flush-on-dispose).
    private readonly PostExtractionPipeline _pipeline;
    private bool _disposed;
    private readonly AgentEngineOptions _options;
    private readonly ILogger _logger;
    private readonly string _startUrl;
    private readonly string _goal;
    private readonly string? _explicitRunId;
    private readonly PageType _pageType;
    private readonly int _historyWindow;
    private readonly int _visitedWindow;
    private readonly int _candidateUrlCap;
    private readonly int _maxPageMarkdownChars;
    private readonly RunTelemetryHooks? _telemetryHooks;

    internal AgentEngine(
        string startUrl,
        string goal,
        IAgentBrain brain,
        IAgentRunStore runStore,
        IPageLoader pageLoader,
        IContentExtractor contentExtractor,
        ISchemaValidator validator,
        IActionResolver actionResolver,
        IVisitedLinkTracker visitedTracker,
        List<IScraperSink> sinks,
        IReadOnlyList<IPageProcessor> processors,
        AgentEngineOptions options,
        ILogger logger,
        string? runId = null,
        PageType pageType = PageType.Static,
        int historyWindow = 10,
        int visitedWindow = 30,
        int candidateUrlCap = 50,
        int maxPageMarkdownChars = 32_000,
        RunTelemetryHooks? telemetryHooks = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(startUrl);
        ArgumentException.ThrowIfNullOrEmpty(goal);
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentNullException.ThrowIfNull(runStore);
        ArgumentNullException.ThrowIfNull(pageLoader);
        ArgumentNullException.ThrowIfNull(contentExtractor);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(actionResolver);
        ArgumentNullException.ThrowIfNull(visitedTracker);
        ArgumentNullException.ThrowIfNull(sinks);
        ArgumentNullException.ThrowIfNull(processors);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _startUrl = startUrl;
        _goal = goal;
        _brain = brain;
        _runStore = runStore;
        _pageLoader = pageLoader;
        _contentExtractor = contentExtractor;
        _validator = validator;
        _actionResolver = actionResolver;
        _visitedTracker = visitedTracker;
        // ADR-0076: one module owns the processors + sinks and their lifecycle.
        _pipeline = new PostExtractionPipeline(sinks, processors, logger);
        _options = options;
        _logger = logger;
        _explicitRunId = runId;
        _pageType = pageType;
        _historyWindow = historyWindow;
        _visitedWindow = visitedWindow;
        _candidateUrlCap = candidateUrlCap;
        _maxPageMarkdownChars = maxPageMarkdownChars;
        // ADR-0066: per-run telemetry hooks. Null when no satellite
        // registered telemetry on the builder; MaxBudgetTokens
        // enforcement becomes inert in that case.
        _telemetryHooks = telemetryHooks;
    }

    /// <summary>
    /// Run the agent loop to completion and return the
    /// <see cref="AgentResult"/>. Use the result's
    /// <see cref="AgentResult.RunId"/> to resume an interrupted run via
    /// <see cref="WebReaper.Agent.ResumeAsync"/>.
    /// </summary>
    public async Task<AgentResult> RunAsync(CancellationToken cancellationToken = default)
    {
        // ADR-0066: reset telemetry at the start of each run so
        // consecutive RunAsync calls on the same engine produce
        // independent reports. Wall-clock measured entry-to-return.
        _telemetryHooks?.Reset();
        var sw = Stopwatch.StartNew();

        // ADR-0033 / ADR-0076: warm up the durable adapters the agent holds —
        // the Agent run store and the Post-extraction pipeline's sinks +
        // processors — once, before the loop. Pre-0076 the agent warmed
        // nothing, so a durable run store or sink (Redis / Mongo / Sqlite /
        // Cosmos) never ran its InitializeAsync.
        await WarmUpAdaptersAsync();

        var runId = _explicitRunId ?? Guid.NewGuid().ToString("N");

        // ADR-0051 fork 8: resume from snapshot if one exists for this runId,
        // start fresh otherwise. .WithRunId('foo') resumes by default.
        var snapshot = await _runStore.LoadAsync(runId, cancellationToken);
        var history = new List<AgentDecision>(snapshot?.History ?? Array.Empty<AgentDecision>());
        var visited = new List<string>(snapshot?.VisitedUrls ?? Array.Empty<string>());
        var records = new List<JsonObject>(snapshot?.Records ?? Array.Empty<JsonObject>());
        var step = snapshot is null ? 0 : snapshot.LastDecidedStep + 1;
        var currentUrl = snapshot?.CurrentUrl ?? _startUrl;
        IReadOnlyList<PageAction>? pendingActions = null;

        // ADR-0061: the brain's per-step outcome signal. On a fresh run the
        // first step sees None; on resume the persisted snapshot's
        // LastOutcome is the brain's first input so the run picks up
        // causally. The engine threads this across iterations — each
        // decision-execution arm sets the outcome the *next* iteration
        // surfaces to the brain.
        AgentDecisionOutcome lastOutcome = snapshot?.LastOutcome ?? new AgentDecisionOutcome.None();

        // ADR-0061: when the prior decision was a Follow, the *next*
        // iteration's page load fills in the Followed outcome (status code
        // + actual URL). This marker carries that intent across iterations.
        bool followPending = false;

        _logger.LogInformation(
            "Agent run {RunId} {Mode}: goal='{Goal}', startUrl='{StartUrl}', step={Step}",
            runId, snapshot is null ? "starting fresh" : "resuming", _goal, _startUrl, step);

        var terminationReason = "loop ended without explicit termination";

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // ADR-0051 fork 6: MaxSteps cap — defence-in-depth termination.
            if (step >= _options.MaxSteps)
            {
                terminationReason = $"MaxSteps ({_options.MaxSteps}) reached";
                _logger.LogInformation("Agent run {RunId} stopping: {Reason}", runId, terminationReason);
                break;
            }

            // ADR-0066: MaxBudgetTokens cap — defence-in-depth termination,
            // finally honouring the field documented since ADR-0051. Reads
            // the cumulative LLM token total via the satellite-provided
            // telemetry hook; silently inert when no LLM adapter ran or
            // the chat client doesn't surface usage (which is documented
            // behaviour on AgentEngineOptions.MaxBudgetTokens). Termination
            // precedence per ADR-0051 fork 6: brain Stop > MaxSteps >
            // MaxBudgetTokens > cancellation.
            if (_options.MaxBudgetTokens is long cap
                && _telemetryHooks?.TotalLlmTokens?.Invoke() is long spent
                && spent >= cap)
            {
                terminationReason = $"MaxBudgetTokens ({cap}) reached (spent={spent})";
                _logger.LogInformation("Agent run {RunId} stopping: {Reason}", runId, terminationReason);
                break;
            }

            // Load the current page (the brain's working surface).
            // ADR-0061 (behaviour change vs ADR-0051): page-load failures
            // are NO LONGER terminal — they become Failed outcomes the brain
            // sees next step and decides Stop / try another URL. Load-
            // failure-terminates-run was failure-mode #1 from §Context: one
            // bad URL killed the whole run.
            string? pageHtml = null;
            int? loadStatusCode = null;
            string actualLoadedUrl = currentUrl;
            try
            {
                var request = new PageRequest(currentUrl, _pageType, pendingActions, Headless: true);
                var result = await _pageLoader.LoadAsync(request, cancellationToken);
                pageHtml = result.Html;
                pendingActions = null; // one-shot
                // ADR-0083: the loader now surfaces the real HTTP status. Fall
                // back to the pre-0083 synthesis (200 for Static, 0 for Dynamic,
                // the "0 means dynamic" Followed-StatusCode semantics) when the
                // transport could not determine it (the CDP transport returns
                // null today).
                loadStatusCode = result.HttpStatus ?? (_pageType == PageType.Static ? 200 : 0);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Agent run {RunId}: page load failed for {Url} — surfacing as Failed outcome (loop continues per ADR-0061)",
                    runId, currentUrl);
                lastOutcome = new AgentDecisionOutcome.Failed(
                    Reason: $"load: {ex.Message}",
                    ExceptionType: ex.GetType().Name);
                pendingActions = null; // discard any pending action on load failure
                followPending = false;
            }

            // If the page load succeeded and we were resolving a pending
            // Follow, finalise the Followed outcome.
            if (pageHtml is not null && followPending)
            {
                lastOutcome = new AgentDecisionOutcome.Followed(
                    ActualUrl: actualLoadedUrl,
                    StatusCode: loadStatusCode ?? 0);
                followPending = false;
            }

            // Build the brain's view of the page (Markdown for cheap LLM
            // input) and the candidate URL pool (every <a href>). Both
            // capped per fork 3 verdict — token cost is the constraint.
            // ADR-0063: call HtmlToMarkdown.Convert directly — going
            // through the adapter would wrap-and-discard the JsonObject
            // for no reason. The try/catch around rendering stays — a
            // corrupt page might break the parser.
            string pageMarkdown;
            IReadOnlyList<string> candidates;
            if (pageHtml is not null)
            {
                // ADR-0051 fork 12: honour the visited-link tracker. The
                // agent shares it with any Crawl driver in the same process.
                await _visitedTracker.AddVisitedLinkAsync(currentUrl);
                if (visited.Count == 0 || !string.Equals(visited[^1], currentUrl, StringComparison.Ordinal))
                    visited.Add(currentUrl);

                try
                {
                    pageMarkdown = HtmlToMarkdown.Convert(pageHtml);
                    if (pageMarkdown.Length > _maxPageMarkdownChars)
                        pageMarkdown = pageMarkdown[.._maxPageMarkdownChars];
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Agent run {RunId}: Markdown rendering failed for {Url}; using raw HTML excerpt", runId, currentUrl);
                    pageMarkdown = pageHtml.Length > _maxPageMarkdownChars
                        ? pageHtml[.._maxPageMarkdownChars]
                        : pageHtml;
                }

                candidates = await ExtractCandidateUrlsAsync(currentUrl, pageHtml);
            }
            else
            {
                // Load failed — the brain still gets a state, but with an
                // empty page view and no candidates. It'll see the Failed
                // outcome and (per the prompt) typically pick a different
                // URL or Stop.
                pageMarkdown = string.Empty;
                candidates = Array.Empty<string>();
            }

            var state = new AgentState(
                Goal: _goal,
                CurrentUrl: currentUrl,
                CurrentPageMarkdown: pageMarkdown,
                CandidateUrls: candidates,
                Extracted: records,
                History: TailOf(history, _historyWindow),
                VisitedUrls: TailOf(visited, _visitedWindow),
                StepNumber: step,
                LastOutcome: lastOutcome);

            // Ask the brain.
            AgentDecision decision;
            try
            {
                decision = await _brain.DecideAsync(state, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Agent run {RunId}: brain DecideAsync threw at step {Step}", runId, step);
                terminationReason = $"brain threw: {ex.Message}";
                break;
            }

            history.Add(decision);

            // ADR-0051 fork 8 §Decision §6: PERSIST BEFORE EXECUTE. The
            // snapshot's LastDecidedStep is the current step — a resume
            // restarts at step + 1 and re-executes the just-persisted
            // decision's effect (sink emissions may dup; idempotent sinks
            // assumed).
            // ADR-0061: the persisted snapshot carries the *prior* step's
            // outcome (lastOutcome here is from the previous iteration's
            // execution). The current step's outcome is computed in the
            // switch below and persisted next iteration — atomicity holds:
            // history[^1] is the just-decided decision; LastOutcome refers
            // to history[^2]'s execution (or None on the first step).
            var postState = new AgentRunSnapshot(
                Goal: _goal,
                LastDecidedStep: step,
                History: history,
                VisitedUrls: visited,
                Records: records,
                CurrentUrl: currentUrl,
                LastOutcome: lastOutcome);
            await _runStore.SaveStepAsync(runId, decision, postState, cancellationToken);

            _logger.LogInformation(
                "Agent run {RunId} step {Step}: {Decision} — {Reason}",
                runId, step, decision.GetType().Name, decision.Reason);

            // Execute. ADR-0061: each arm computes lastOutcome for the next
            // iteration to surface to the brain on its next DecideAsync.
            switch (decision)
            {
                case AgentDecision.Stop:
                    // Persist Stopped into the final snapshot below (after
                    // the loop) for resume-tooling inspection; the brain
                    // never sees this since the loop terminates.
                    lastOutcome = new AgentDecisionOutcome.Stopped(decision.Reason);
                    terminationReason = decision.Reason;
                    goto Done;

                case AgentDecision.Extract extract:
                    if (pageHtml is null)
                    {
                        // The page never loaded — the load-failure outcome
                        // is already set; the brain shouldn't have proposed
                        // Extract on a failed-load state, but if it did, we
                        // simply skip the extraction (the load Failed
                        // outcome is what they see next step).
                        step++;
                        break;
                    }
                    try
                    {
                        var extracted = await _contentExtractor.ExtractAsync(pageHtml, extract.Schema);
                        // ADR-0061 ↔ ADR-0062: consult the registered
                        // ISchemaValidator on every Extract. A failed verdict
                        // becomes a Failed("validation: <reason>") outcome on
                        // the next step so the brain can revise the schema.
                        // Default validator is SchemaSatisfiedValidator
                        // (required-leaves-non-empty, ADR-0029 alignment).
                        // Pre-check: SchemaSatisfiedValidator treats a null
                        // record as trivially valid (its "no data, no check"
                        // posture matches the Markdown/LLM no-schema case)
                        // — the agent path needs null to surface as failure
                        // so the brain can revise. Null extracted ⇒ Failed
                        // bypassing the validator.
                        ValidationResult verdict;
                        if (extracted is null)
                        {
                            verdict = ValidationResult.Invalid("extractor returned null");
                        }
                        else
                        {
                            verdict = _validator.Validate(extracted, extract.Schema);
                        }
                        if (!verdict.IsValid)
                        {
                            lastOutcome = new AgentDecisionOutcome.Failed(
                                Reason: $"validation: {verdict.Reason ?? "schema not satisfied"}",
                                ExceptionType: null);
                        }
                        else
                        {
                            // The null branch above (extracted is null →
                            // Invalid verdict → !IsValid → outer if) ensures
                            // extracted is non-null here; the compiler's
                            // narrowing doesn't span the verdict variable so
                            // a forgiving operator surfaces the invariant.
                            // ADR-0076: one fused call runs the page-processor
                            // pipeline then fans the survivor out to the sinks,
                            // returning the surviving record (or null if a
                            // processor dropped it). The agent uses the return
                            // for its run-scoped record bookkeeping; the crawl
                            // driver ignores it.
                            var processed = await _pipeline.ProcessAndEmitAsync(
                                new ParsedData(currentUrl, extracted!),
                                pageHtml,
                                Array.Empty<string>(),
                                extract.Schema,
                                cancellationToken);
                            if (processed is not null)
                            {
                                records.Add(processed.Data);
                                lastOutcome = new AgentDecisionOutcome.Extracted(
                                    Record: processed.Data,
                                    RecordCount: records.Count);
                            }
                            else
                            {
                                // Page processor dropped — no sink emitted it.
                                // Brain sees Extracted(null, count) — count
                                // unchanged.
                                lastOutcome = new AgentDecisionOutcome.Extracted(
                                    Record: null,
                                    RecordCount: records.Count);
                            }
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "Agent run {RunId}: Extract failed at step {Step} — surfacing as Failed outcome",
                            runId, step);
                        lastOutcome = new AgentDecisionOutcome.Failed(
                            Reason: $"extract: {ex.Message}",
                            ExceptionType: ex.GetType().Name);
                    }
                    step++;
                    break;

                case AgentDecision.Follow follow:
                    // ADR-0051 fork 12: enforce. A brain that proposes a
                    // visited URL is rejected at the engine; the next
                    // iteration sees a Failed outcome so the brain can
                    // re-decide (rather than the engine silently looping).
                    if (visited.Contains(follow.Url))
                    {
                        _logger.LogWarning(
                            "Agent run {RunId} step {Step}: brain proposed already-visited URL {Url} — staying on current page",
                            runId, step, follow.Url);
                        lastOutcome = new AgentDecisionOutcome.Failed(
                            Reason: $"already visited: {follow.Url}",
                            ExceptionType: null);
                    }
                    else
                    {
                        currentUrl = follow.Url;
                        // Defer the Followed outcome to next iteration's
                        // page load — we don't know the actualUrl /
                        // statusCode yet. The load block above finalises it.
                        followPending = true;
                    }
                    step++;
                    break;

                case AgentDecision.Act act:
                    // v1: re-load the current URL with the action attached.
                    // The browser transport applies the action during load
                    // (a SemanticAct resolves through the registered
                    // IActionResolver — same cache as the Crawl path). The
                    // HTTP transport ignores actions, so Act on a static
                    // page is silently a no-op — agents that use Act should
                    // pick the browser page type via .WithBrowser() on the
                    // builder.
                    try
                    {
                        pendingActions = new[] { act.Action };
                        lastOutcome = new AgentDecisionOutcome.ActDispatched(act.Action);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "Agent run {RunId}: Act dispatch failed at step {Step} — surfacing as Failed outcome",
                            runId, step);
                        lastOutcome = new AgentDecisionOutcome.Failed(
                            Reason: $"act dispatch: {ex.Message}",
                            ExceptionType: ex.GetType().Name);
                    }
                    step++;
                    break;
            }
        }

    Done:
        // ADR-0061: write the final Stopped outcome into the persisted
        // snapshot BEFORE deletion so resume tooling can read it. The
        // engine already deletes the snapshot on clean termination
        // (ADR-0051 §Decision §6); the final write here is the
        // resume-tooling read window between RunAsync returning and any
        // store inspection caller (logging hooks, observability, tests).
        // The brain never sees Stopped — the loop has terminated.
        // (No-op for the in-memory default since DeleteAsync runs
        // immediately after; durable adapters can still observe the final
        // shape via a caller-side LoadAsync prior to DeleteAsync if they
        // hook in. The conventional path is: read history, see Stop arm.)

        // ADR-0051 §Decision §6: clean termination frees the snapshot —
        // subsequent .WithRunId(runId) starts a fresh run.
        await _runStore.DeleteAsync(runId, cancellationToken);

        _logger.LogInformation(
            "Agent run {RunId} complete: {Steps} steps, {Records} records — {Reason}",
            runId, step, records.Count, terminationReason);

        sw.Stop();
        return new AgentResult(
            RunId: runId,
            Records: records,
            TerminationReason: terminationReason,
            History: history,
            VisitedUrls: visited,
            // StepsExecuted is the number of brain decisions returned —
            // includes Stop, which is itself a decision but doesn't advance
            // the in-loop step counter. history.Count is the correct count
            // for the result; the inner `step` is the next-decision index.
            StepsExecuted: history.Count,
            // ADR-0066: per-run telemetry summary. Llm is null when no
            // satellite registered telemetry (no LLM adapter ran).
            Report: new RunReport(_telemetryHooks?.Snapshot(), sw.Elapsed));
    }

    private async Task<List<string>> ExtractCandidateUrlsAsync(string baseUrl, string html)
    {
        try
        {
            var uri = new Uri(baseUrl);
            var links = await LinkExtractor.GetLinksAsync(uri, html, "a");
            if (links.Count > _candidateUrlCap) links.RemoveRange(_candidateUrlCap, links.Count - _candidateUrlCap);
            return links;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract candidate URLs from {Url}; empty pool", baseUrl);
            return new List<string>();
        }
    }

    // ADR-0033 / ADR-0076: warm up the durable adapters the agent owns — the
    // Agent run store and the Post-extraction pipeline (its sinks + processors)
    // — once, before the loop. Idempotent; adapters with no warm-up are skipped.
    private async Task WarmUpAdaptersAsync()
    {
        if (_runStore is IAsyncInitializable runStore)
            await runStore.InitializeAsync();

        // ADR-0076: warms every sink + processor that opts into ADR-0033.
        await _pipeline.InitializeAsync();
    }

    /// <summary>
    /// ADR-0076 / ADR-0058: dispose the durable adapters the agent owns — the
    /// Post-extraction pipeline (its processors then sinks, reverse order) and
    /// the Agent run store. The recommended consumer pattern is
    /// <c>await using var engine = await builder.BuildAsync();</c> so a durable
    /// sink's flush-on-dispose runs on scope exit. Per-adapter disposal
    /// exceptions log at Warning and are swallowed (ADR-0058). Idempotent.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await _pipeline.DisposeAsync();
        await SafeDisposeAsync(_runStore);
    }

    private async ValueTask SafeDisposeAsync(object? obj)
    {
        try
        {
            switch (obj)
            {
                case IAsyncDisposable a: await a.DisposeAsync(); break;
                case IDisposable d: d.Dispose(); break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Disposal of {Type} threw", obj?.GetType().Name ?? "(null)");
        }
    }

    private static IReadOnlyList<T> TailOf<T>(IReadOnlyList<T> source, int max)
    {
        if (source.Count <= max) return source;
        var result = new List<T>(max);
        for (var i = source.Count - max; i < source.Count; i++) result.Add(source[i]);
        return result;
    }
}
