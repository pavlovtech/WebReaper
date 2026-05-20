using Microsoft.Extensions.Logging;
using WebReaper.Core.Parser.Concrete;
using WebReaper.Domain.Parsing;

namespace WebReaper.UnitTests;

// ADR-0029: the Schema fold's per-leaf swallow-and-log policy is the
// documented contract — a malformed leaf must leave the field unset,
// no exception propagating, with a coercion-specific log message that
// distinguishes parse failures from backend errors. These tests pin
// the policy across every typed Coerce arm.
public class TypedCoercionFailureTests
{
    private static (AngleSharpContentParser Parser, CapturingLogger Logger) Html()
    {
        var logger = new CapturingLogger();
        return (new AngleSharpContentParser(logger), logger);
    }

    [Fact]
    public async Task Integer_field_with_non_numeric_text_leaves_field_unset_and_logs_format_failure()
    {
        var (parser, logger) = Html();

        var result = await parser.ParseToJsonAsync(
            "<i>abc</i>",
            new Schema { new SchemaElement("n", "i", DataType.Integer) });

        Assert.Null(result["n"]); // unset, not crashed
        var entry = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.IsType<FormatException>(entry.Exception);
        Assert.Contains("Coercion to Integer failed", entry.Message);
        Assert.Contains("'n'", entry.Message);
    }

    [Fact]
    public async Task Integer_field_with_overflow_value_leaves_field_unset_and_logs_overflow()
    {
        var (parser, logger) = Html();

        var result = await parser.ParseToJsonAsync(
            "<i>99999999999</i>", // exceeds Int32.MaxValue
            new Schema { new SchemaElement("n", "i", DataType.Integer) });

        Assert.Null(result["n"]);
        var entry = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.IsType<OverflowException>(entry.Exception);
        Assert.Contains("Coercion to Integer overflowed", entry.Message);
        Assert.Contains("'n'", entry.Message);
    }

    [Fact]
    public async Task Float_field_with_non_numeric_text_leaves_field_unset()
    {
        var (parser, logger) = Html();

        var result = await parser.ParseToJsonAsync(
            "<i>xyz</i>",
            new Schema { new SchemaElement("f", "i", DataType.Float) });

        Assert.Null(result["f"]);
        var entry = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.IsType<FormatException>(entry.Exception);
        Assert.Contains("Coercion to Float failed", entry.Message);
    }

    [Fact]
    public async Task Boolean_field_with_non_boolean_text_leaves_field_unset()
    {
        var (parser, logger) = Html();

        var result = await parser.ParseToJsonAsync(
            "<i>neither</i>",
            new Schema { new SchemaElement("b", "i", DataType.Boolean) });

        Assert.Null(result["b"]);
        var entry = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.IsType<FormatException>(entry.Exception);
        Assert.Contains("Coercion to Boolean failed", entry.Message);
    }

    [Fact]
    public async Task DateTime_field_with_garbage_text_leaves_field_unset()
    {
        var (parser, logger) = Html();

        var result = await parser.ParseToJsonAsync(
            "<i>not-a-date</i>",
            new Schema { new SchemaElement("d", "i", DataType.DataTime) });

        Assert.Null(result["d"]);
        var entry = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.IsType<FormatException>(entry.Exception);
        Assert.Contains("Coercion to DataTime failed", entry.Message);
    }

    [Fact]
    public async Task Typed_leaf_list_with_one_malformed_element_drops_the_whole_list_by_design()
    {
        // ADR-0029 pin: the per-leaf catch wraps the entire list-build,
        // so one bad element drops the whole array (not just the bad
        // element). Documented as deliberate; per-element drop is a
        // distinct follow-up candidate listed in ADR-0029's
        // "Considered options".
        var (parser, logger) = Html();

        var result = await parser.ParseToJsonAsync(
            "<i>1</i><i>oops</i><i>3</i>",
            new Schema { new SchemaElement("ns", "i", DataType.Integer) { IsList = true } });

        Assert.Null(result["ns"]); // whole list unassigned
        var entry = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.IsType<FormatException>(entry.Exception);
    }

    [Fact]
    public async Task Well_formed_typed_value_does_not_log_an_error()
    {
        // Sanity: the happy path emits no error log entry. Pinning the
        // *absence* of a log message is what makes the failure-path
        // pins above meaningful.
        var (parser, logger) = Html();

        var result = await parser.ParseToJsonAsync(
            "<i>42</i>",
            new Schema { new SchemaElement("n", "i", DataType.Integer) });

        Assert.Equal(42, result["n"]!.GetValue<int>());
        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Error);
    }
}
