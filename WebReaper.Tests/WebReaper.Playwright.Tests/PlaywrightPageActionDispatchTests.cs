using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using NSubstitute;
using WebReaper.Core.Actions.Abstract;
using WebReaper.Core.Actions.Concrete;
using WebReaper.Domain.PageActions;
using WebReaper.Playwright;

namespace WebReaper.Playwright.Tests;

/// <summary>
/// ADR-0078 Axis A, deferred follow-up. C# has no closed-hierarchy
/// exhaustiveness, so a forgotten <see cref="PageAction"/> arm in a transport
/// compiles silently. The CDP transport proves arm coverage by executing every
/// arm against a <c>FakeCdpSession</c>; this is the Playwright equivalent,
/// against a mocked <see cref="IPage"/> (NSubstitute — IPage is far too large
/// to hand-stub). Reflect every arm, dispatch each through
/// <see cref="PlaywrightPageActionDispatcher.PerformAsync"/>, and assert none
/// reaches the switch's <c>default: throw</c>. The reflection completeness
/// check forces a sample to be added when a <see cref="PageAction"/> arm is
/// added, and the dispatch then proves the transport handles it.
/// </summary>
public class PlaywrightPageActionDispatchTests
{
    [Fact]
    public async Task Every_PageAction_arm_dispatches_without_hitting_the_default()
    {
        PageAction[] samples =
        [
            new PageAction.Click(".x"),
            new PageAction.Wait(1),
            new PageAction.WaitForSelector(".x", 1000),
            new PageAction.WaitForNetworkIdle(),
            new PageAction.ScrollToEnd(),
            new PageAction.ScrollIntoView(".x"),
            new PageAction.EvaluateExpression("1"),
            new PageAction.Press("Enter"),
            new PageAction.Fill(".x", "y"),
            new PageAction.SemanticAct("do it"),
        ];

        // Completeness: the samples cover every concrete PageAction arm, so a
        // newly-added arm trips this until a sample is added here.
        var armTypes = typeof(PageAction).GetNestedTypes()
            .Where(t => t.IsSealed && t.IsSubclassOf(typeof(PageAction)))
            .ToHashSet();
        var sampled = samples.Select(s => s.GetType()).ToHashSet();
        Assert.True(armTypes.SetEquals(sampled),
            "Sample set must cover every PageAction arm. Add a sample for the new arm. " +
            $"Uncovered: [{string.Join(", ", armTypes.Except(sampled).Select(t => t.Name))}]");

        foreach (var arm in samples)
        {
            // NSubstitute auto-returns completed tasks for async members and
            // recursive substitutes for Locator / Keyboard, so every native
            // Playwright call on the mock succeeds without per-member setup.
            var page = Substitute.For<IPage>();
            page.ContentAsync().Returns("<html></html>");
            // SemanticAct resolves to a concrete arm via the resolver.
            var coord = new SemanticActCoordinator(
                new StubResolver(new PageAction.Click(".resolved")), NullLogger.Instance);

            var ex = await Record.ExceptionAsync(() =>
                PlaywrightPageActionDispatcher.PerformAsync(page, arm, coord, default));

            Assert.False(ex is ArgumentOutOfRangeException,
                $"{arm.GetType().Name} fell through to the dispatcher's default (unhandled arm)");
            Assert.Null(ex);
        }
    }

    [Fact]
    public async Task Click_delegates_to_native_ClickAsync()
    {
        // A representative behavioural pin (mirrors the CDP per-arm tests):
        // the dispatch wires the arm to the native Playwright call, not just
        // avoiding the default.
        var page = Substitute.For<IPage>();
        var coord = new SemanticActCoordinator(
            new StubResolver(new PageAction.Click(".resolved")), NullLogger.Instance);

        await PlaywrightPageActionDispatcher.PerformAsync(
            page, new PageAction.Click("button.signin"), coord, default);

        await page.Received(1).ClickAsync("button.signin");
    }

    private sealed class StubResolver : IActionResolver
    {
        private readonly PageAction _arm;
        public StubResolver(PageAction arm) => _arm = arm;
        public Task<PageAction?> ResolveAsync(
            string intent, string pageHtml, CancellationToken cancellationToken = default)
            => Task.FromResult<PageAction?>(_arm);
    }
}
