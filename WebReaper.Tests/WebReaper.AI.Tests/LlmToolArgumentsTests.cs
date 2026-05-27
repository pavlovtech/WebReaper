using System.Text.Json;
using WebReaper.AI.Llm;

namespace WebReaper.AI.Tests;

// ADR-0059 amendment: the shared JsonElement argument extractors the
// tool-calling Llm* adapters use to parse FunctionCallContent.Arguments.
// Extracted byte-identically from the now-deleted private copies that
// lived in LlmActionResolver and LlmAgentBrain. These tests pin the
// leniency contract so a future tighten-up doesn't silently break the
// adapters depending on it.
public class LlmToolArgumentsTests
{
    private static JsonElement Parse(string json) =>
        JsonDocument.Parse(json).RootElement;

    // ---- TryGetString ------------------------------------------------------

    [Fact]
    public void TryGetString_returns_the_string_when_present()
    {
        var args = Parse("""{ "selector": ".btn" }""");
        Assert.Equal(".btn", LlmToolArguments.TryGetString(args, "selector"));
    }

    [Fact]
    public void TryGetString_returns_null_when_property_is_absent()
    {
        var args = Parse("""{ "other": "x" }""");
        Assert.Null(LlmToolArguments.TryGetString(args, "selector"));
    }

    [Fact]
    public void TryGetString_returns_null_when_value_is_json_null()
    {
        var args = Parse("""{ "selector": null }""");
        Assert.Null(LlmToolArguments.TryGetString(args, "selector"));
    }

    [Fact]
    public void TryGetString_returns_null_for_non_string_kinds()
    {
        // Number, boolean, array, object — all coerce to null per the
        // adapter contract (callers want a clean null on bad shape, not
        // a coerced ToString).
        Assert.Null(LlmToolArguments.TryGetString(Parse("""{ "x": 42 }"""), "x"));
        Assert.Null(LlmToolArguments.TryGetString(Parse("""{ "x": true }"""), "x"));
        Assert.Null(LlmToolArguments.TryGetString(Parse("""{ "x": [1,2] }"""), "x"));
        Assert.Null(LlmToolArguments.TryGetString(Parse("""{ "x": {} }"""), "x"));
    }

    [Fact]
    public void TryGetString_returns_empty_string_verbatim()
    {
        // Empty string is a real value, not absent — callers pattern-
        // match on { Length: > 0 } when they need a non-empty selector.
        var args = Parse("""{ "selector": "" }""");
        Assert.Equal(string.Empty, LlmToolArguments.TryGetString(args, "selector"));
    }

    [Fact]
    public void TryGetString_returns_null_when_args_is_not_an_object()
    {
        // The provider may hand us a malformed root (array, string,
        // primitive); the helpers swallow rather than throw so the
        // adapter can fall back to its default arm.
        Assert.Null(LlmToolArguments.TryGetString(Parse("""[1,2,3]"""), "selector"));
        Assert.Null(LlmToolArguments.TryGetString(Parse("""null"""), "selector"));
    }

    // ---- TryGetInt ---------------------------------------------------------

    [Fact]
    public void TryGetInt_returns_the_int_when_present()
    {
        var args = Parse("""{ "ms": 250 }""");
        Assert.Equal(250, LlmToolArguments.TryGetInt(args, "ms"));
    }

    [Fact]
    public void TryGetInt_returns_null_when_property_is_absent()
    {
        var args = Parse("""{ "other": 1 }""");
        Assert.Null(LlmToolArguments.TryGetInt(args, "ms"));
    }

    [Fact]
    public void TryGetInt_tolerates_string_encoded_integers()
    {
        // Some providers serialise small ints as strings; tolerate it.
        var args = Parse("""{ "timeoutMs": "30000" }""");
        Assert.Equal(30_000, LlmToolArguments.TryGetInt(args, "timeoutMs"));
    }

    [Fact]
    public void TryGetInt_returns_null_for_non_integer_strings()
    {
        var args = Parse("""{ "ms": "fast" }""");
        Assert.Null(LlmToolArguments.TryGetInt(args, "ms"));
    }

    [Fact]
    public void TryGetInt_returns_null_for_non_int32_numbers()
    {
        // Floating point and out-of-Int32 numbers read as null — the
        // adapter contract is "an int or nothing."
        Assert.Null(LlmToolArguments.TryGetInt(Parse("""{ "ms": 1.5 }"""), "ms"));
        Assert.Null(LlmToolArguments.TryGetInt(Parse("""{ "ms": 9999999999 }"""), "ms"));
    }

    [Fact]
    public void TryGetInt_returns_null_for_json_null()
    {
        var args = Parse("""{ "ms": null }""");
        Assert.Null(LlmToolArguments.TryGetInt(args, "ms"));
    }

    [Fact]
    public void TryGetInt_returns_null_when_args_is_not_an_object()
    {
        Assert.Null(LlmToolArguments.TryGetInt(Parse("""42"""), "ms"));
    }
}
