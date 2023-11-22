using System;

namespace WebReaper.Proxy.Concrete;

/// <summary>
/// The result of validating a proxy.
/// </summary>
/// <remarks>
/// Either <see cref="IsValid"/> or <see cref="IsInvalid"/> will be <c>true</c> when initialized.
/// </remarks>
public readonly struct ProxyProposalValidationResult
{
    private readonly Kind _kind;

    ProxyProposalValidationResult(Kind kind, Exception? error = null)
    {
        _kind = kind;
    }

    /// <summary>
    /// A default result.
    /// </summary>
    public static ProxyProposalValidationResult Default = new ProxyProposalValidationResult(Kind.Default);

    /// <summary>
    /// A valid result.
    /// </summary>
    public static ProxyProposalValidationResult Valid() => new ProxyProposalValidationResult(Kind.Valid);
    /// <summary>
    /// An invalid result, with an error.
    /// </summary>
    public static ProxyProposalValidationResult Invalid(Exception error) => new ProxyProposalValidationResult(Kind.Invalid, error);

    /// <summary>
    /// Whether the result is the default result.
    /// </summary>
    public bool IsDefault => _kind == Kind.Default;
    /// <summary>
    /// Whether the result is valid.
    /// </summary>
    public bool IsValid => _kind == Kind.Valid;
    /// <summary>
    /// Whether the result is invalid.
    /// </summary>
    public bool IsInvalid => _kind == Kind.Invalid;
    /// <summary>
    /// The error, if any.
    /// </summary>
    public Exception? Error { get; }

    enum Kind
    {
        Default,
        Valid,
        Invalid
    }
}
