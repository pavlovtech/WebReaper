namespace WebReaper.Domain.PageActions;

/// <summary>
/// One browser interaction performed on a dynamic page before scraping (built
/// via <see cref="WebReaper.Builders.PageActionBuilder"/>). The
/// <see cref="Parameters"/> shape depends on <see cref="Type"/> — e.g. a CSS
/// selector for <see cref="PageActionType.Click"/>, a millisecond count for
/// <see cref="PageActionType.Wait"/>. Interpreted by the WebReaper.Puppeteer
/// satellite (ADR-0009).
/// </summary>
/// <param name="Type">Which interaction to perform.</param>
/// <param name="Parameters">The interaction's arguments, positional per
/// <paramref name="Type"/>.</param>
public record PageAction(PageActionType Type, params object[] Parameters);
