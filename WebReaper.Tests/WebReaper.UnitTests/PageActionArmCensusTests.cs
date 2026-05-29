using WebReaper.Domain.PageActions;

namespace WebReaper.UnitTests;

// ADR-0078 Axis A: a PageAction arm is interpreted in several packages that
// core cannot reference (ADR-0009) — the two browser transports' dispatch
// switches and the AI tool registries — so an arm cannot live in one file, and
// C# has no closed-hierarchy exhaustiveness to make a forgotten arm a compile
// error (a discard-less switch expression warns CS8509 even when every arm is
// handled). This census is the cheap, transport-agnostic tripwire: when the
// arm set changes it fails with a checklist of every consumer to update. It is
// the primary guard for the Playwright transport, which (unlike CDP) has no
// unit-test seam for its private IPage dispatch; CDP additionally proves it
// handles every arm in WebReaper.Cdp.Tests.CdpPageActionDispatchTests.
public class PageActionArmCensusTests
{
    [Fact]
    public void PageAction_arm_set_is_pinned_so_a_new_arm_forces_updating_every_consumer()
    {
        var arms = typeof(PageAction).GetNestedTypes()
            .Where(t => t.IsSealed && t.IsSubclassOf(typeof(PageAction)))
            .Select(t => t.Name)
            .ToHashSet();

        var expected = new HashSet<string>
        {
            nameof(PageAction.Click),
            nameof(PageAction.Wait),
            nameof(PageAction.WaitForSelector),
            nameof(PageAction.WaitForNetworkIdle),
            nameof(PageAction.ScrollToEnd),
            nameof(PageAction.ScrollIntoView),
            nameof(PageAction.EvaluateExpression),
            nameof(PageAction.Press),
            nameof(PageAction.Fill),
            nameof(PageAction.SemanticAct),
        };

        Assert.True(arms.SetEquals(expected),
            "PageAction's arm set changed. An arm is interpreted in places core cannot " +
            "reference, so update ALL of them, then this census:\n" +
            "  - WebReaper.Cdp/CdpPageActionDispatcher.PerformAsync (switch arm)\n" +
            "  - WebReaper.Playwright/PlaywrightPageLoadTransport.PerformAsync (switch arm)\n" +
            "  - WebReaper.AI Tools/PageActionTools.Arms (brain + resolver registries derive from it)\n" +
            "  - WebReaper.Builders.PageActionBuilder (if user-constructable)\n" +
            "  - the PageAction closed-sum codec under WebReaper/Serialization/Converters\n" +
            $"Expected: [{string.Join(", ", expected.OrderBy(x => x))}]\n" +
            $"Actual:   [{string.Join(", ", arms.OrderBy(x => x))}]");
    }
}
