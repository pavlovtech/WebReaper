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
using WebReaper.Extensions;
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
public sealed class AgentEngine
{
    private readonly IAgentBrain _brain;
    private readonly IAgentRunStore _runStore;
    private readonly IPageLoader _pageLoader;
    private readonly IContentExtractor _contentExtractor;
    private readonly IActionResolver _actionResolver;
    private readonly IVisitedLinkTracker _visitedTracker;
    private readonly List<IScraperSink> _sinks;
    private readonly IReadOnlyList<IPageProcessor> _processors;
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

    internal AgentEngine(
        string startUrl,
        string goal,
        IAgentBrain brain,
        IAgentRunStore runStore,
        IPageLoader pageLoader,
        IContentExtractor contentExtractor,
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
        int maxPageMarkdownChars = 32_000)
    {
        ArgumentException.ThrowIfNullOrEmpty(startUrl);
        ArgumentException.ThrowIfNullOrEmpty(goal);
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentNullException.ThrowIfNull(runStore);
        ArgumentNullException.ThrowIfNull(pageLoader);
        ArgumentNullException.ThrowIfNull(contentExtractor);
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
        _actionResolver = actionResolver;
        _visitedTracker = visitedTracker;
        _sinks = sinks;
        _processors = processors;
        _options = options;
        _logger = logger;
        _explicitRunId = runId;
        _pageType = pageType;
        _historyWindow = historyWindow;
        _visitedWindow = visitedWindow;
        _candidateUrlCap = candidateUrlCap;
        _maxPageMarkdownChars = maxPageMarkdownChars;
    }

    /// <summary>
    /// Run the agent loop to completion and return the
    /// <see cref="AgentResult"/>. Use the result's
    /// <see cref="AgentResult.RunId"/> to resume an interrupted run via
    /// <see cref="WebReaper.Agent.ResumeAsync"/>.
    /// </summary>
    public async Task<AgentResult> RunAsync(CancellationToken cancellationToken = default)
    {
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

            // Load the current page (the brain's working surface).
            string pageHtml;
            try
            {
                var request = new PageRequest(currentUrl, _pageType, pendingActions, Headless: true);
                pageHtml = await _pageLoader.LoadAsync(request, cancellationToken);
                pendingActions = null; // one-shot
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Agent run {RunId}: page load failed for {Url}", runId, currentUrl);
                terminationReason = $"page load failed: {ex.Message}";
                break;
            }

            // ADR-0051 fork 12: honour the visited-link tracker. The agent
            // shares it with any Crawl driver in the same process.
            await _visitedTracker.AddVisitedLinkAsync(currentUrl);
            if (visited.Count == 0 || !string.Equals(visited[^1], currentUrl, StringComparison.Ordinal))
                visited.Add(currentUrl);

            // Build the brain's view of the page (Markdown for cheap LLM
            // input) and the candidate URL pool (every <a href>). Both
            // capped per fork 3 verdict — token cost is the constraint.
            // ADR-0063: call HtmlToMarkdown.Convert directly — going
            // through the adapter would wrap-and-discard the JsonObject
            // for no reason. The try/catch around rendering stays — a
            // corrupt page might break the parser.
            string pageMarkdown;
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

            var candidates = await ExtractCandidateUrlsAsync(currentUrl, pageHtml);

            var state = new AgentState(
                Goal: _goal,
                CurrentUrl: currentUrl,
                CurrentPageMarkdown: pageMarkdown,
                CandidateUrls: candidates,
                Extracted: records,
                History: TailOf(history, _historyWindow),
                VisitedUrls: TailOf(visited, _visitedWindow),
                StepNumber: step);

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
            var postState = new AgentRunSnapshot(
                Goal: _goal,
                LastDecidedStep: step,
                History: history,
                VisitedUrls: visited,
                Records: records,
                CurrentUrl: currentUrl);
            await _runStore.SaveStepAsync(runId, decision, postState, cancellationToken);

            _logger.LogInformation(
                "Agent run {RunId} step {Step}: {Decision} — {Reason}",
                runId, step, decision.GetType().Name, decision.Reason);

            // Execute.
            switch (decision)
            {
                case AgentDecision.Stop:
                    terminationReason = decision.Reason;
                    goto Done;

                case AgentDecision.Extract extract:
                    var extracted = await _contentExtractor.ExtractAsync(pageHtml, extract.Schema);
                    var processed = await RunProcessorsAsync(currentUrl, pageHtml, extracted, extract.Schema, cancellationToken);
                    if (processed is not null)
                    {
                        records.Add(processed.Data);
                        await FanOutSinksAsync(processed, cancellationToken);
                    }
                    step++;
                    break;

                case AgentDecision.Follow follow:
                    // ADR-0051 fork 12: enforce. A brain that proposes a
                    // visited URL is logged + the next iteration will re-ask;
                    // the decision is already in history (so resume sees it)
                    // and the brain ought to see it shouldn't repeat itself.
                    if (visited.Contains(follow.Url))
                    {
                        _logger.LogWarning(
                            "Agent run {RunId} step {Step}: brain proposed already-visited URL {Url} — staying on current page",
                            runId, step, follow.Url);
                    }
                    else
                    {
                        currentUrl = follow.Url;
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
                    pendingActions = new[] { act.Action };
                    step++;
                    break;
            }
        }

    Done:
        // ADR-0051 §Decision §6: clean termination frees the snapshot —
        // subsequent .WithRunId(runId) starts a fresh run.
        await _runStore.DeleteAsync(runId, cancellationToken);

        _logger.LogInformation(
            "Agent run {RunId} complete: {Steps} steps, {Records} records — {Reason}",
            runId, step, records.Count, terminationReason);

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
            StepsExecuted: history.Count);
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

    private async Task<ParsedData?> RunProcessorsAsync(
        string url, string html, JsonObject extracted, WebReaper.Domain.Parsing.Schema schema,
        CancellationToken cancellationToken)
    {
        var record = new ParsedData(url, extracted);
        if (_processors.Count == 0) return record;

        foreach (var processor in _processors)
        {
            var context = new PageContext(record, html, new List<string>(), schema);
            PageVerdict verdict;
            try
            {
                verdict = await processor.ProcessAsync(context, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Page processor {Processor} threw on agent step for {Url}; dropping record",
                    processor.GetType().Name, url);
                return null;
            }
            switch (verdict)
            {
                case PageVerdict.Dropped dropped:
                    _logger.LogInformation("Agent record from {Url} dropped by {Processor}: {Reason}",
                        url, processor.GetType().Name, dropped.Reason);
                    return null;
                case PageVerdict.Kept kept:
                    record = kept.Data;
                    break;
            }
        }
        return record;
    }

    private async Task FanOutSinksAsync(ParsedData record, CancellationToken cancellationToken)
    {
        if (_sinks.Count == 0) return;
        // ADR-0031: hand each sink its own deep-cloned Data (sinks may mutate).
        var sinkTasks = _sinks.Select(sink => sink.EmitAsync(
            record with { Data = (JsonObject)record.Data.DeepClone() }, cancellationToken));
        await Task.WhenAll(sinkTasks);
    }

    private static IReadOnlyList<T> TailOf<T>(IReadOnlyList<T> source, int max)
    {
        if (source.Count <= max) return source;
        var result = new List<T>(max);
        for (var i = source.Count - max; i < source.Count; i++) result.Add(source[i]);
        return result;
    }
}
