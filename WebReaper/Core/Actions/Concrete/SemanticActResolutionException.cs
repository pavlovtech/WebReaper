namespace WebReaper.Core.Actions.Concrete;

/// <summary>
/// Thrown by the Puppeteer transport (ADR-0050) when a
/// <see cref="Domain.PageActions.PageAction.SemanticAct"/> dispatch hits the
/// resolver and the resolver returns <c>null</c> — the intent could not be
/// matched against the current page. A typed exception so consumers catch the
/// failure mode explicitly rather than a bare
/// <see cref="NullReferenceException"/>.
/// </summary>
public sealed class SemanticActResolutionException : Exception
{
    /// <summary>The unresolved intent string.</summary>
    public string Intent { get; }

    /// <summary>Construct with the unresolved intent.</summary>
    public SemanticActResolutionException(string intent)
        : base($"The IActionResolver returned null for SemanticAct intent '{intent}' — " +
               "no concrete action could be matched against the current page. " +
               "Either register a different IActionResolver " +
               "(ScraperEngineBuilder.WithActionResolver) or remove the SemanticAct.")
    {
        Intent = intent;
    }

    /// <summary>Construct with the unresolved intent and a wrapped cause.</summary>
    public SemanticActResolutionException(string intent, Exception innerException)
        : base($"The IActionResolver threw while resolving SemanticAct intent '{intent}'. " +
               "See the inner exception for details.", innerException)
    {
        Intent = intent;
    }
}
