using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using WebReaper.Extraction.Attributes;
using WebReaper.Extraction.Generators;

namespace WebReaper.Extraction.Generators.Tests;

// ADR-0045: Source generator tests. The generator is driven by
// CSharpGeneratorDriver over a source string; we assert the output is
// the expected partial class with the static Schema and Materialize.

public class ScrapeSchemaGeneratorTests
{
    [Fact]
    public void Generates_schema_property_for_a_simple_class()
    {
        var output = RunGenerator(@"
using WebReaper.Extraction.Attributes;

namespace MyApp;

[ScrapeSchema]
public partial class Article
{
    [ScrapeField(""h1"")]
    public string? Title { get; set; }

    [ScrapeField("".views"")]
    public int Views { get; set; }
}");

        Assert.Contains("partial class Article", output);
        Assert.Contains("public static readonly Schema Schema = new Schema", output);
        Assert.Contains("new SchemaElement(\"title\", \"h1\")", output);
        Assert.Contains("new SchemaElement(\"views\", \".views\")", output);
        Assert.Contains("Type = DataType.String", output);
        Assert.Contains("Type = DataType.Integer", output);
    }

    [Fact]
    public void Generates_materialize_method_with_direct_property_assignment()
    {
        var output = RunGenerator(@"
using WebReaper.Extraction.Attributes;

namespace MyApp;

[ScrapeSchema]
public partial class Article
{
    [ScrapeField(""h1"")]
    public string? Title { get; set; }

    [ScrapeField("".views"")]
    public int Views { get; set; }
}");

        Assert.Contains("public static Article Materialize(JsonObject json)", output);
        // Each property gets a direct GetValue<T>() — no reflection.
        Assert.Contains("GetValue<string>()", output);
        Assert.Contains("GetValue<int>()", output);
        Assert.Contains("result.Title =", output);
        Assert.Contains("result.Views =", output);
    }

    [Fact]
    public void Handles_list_of_strings_property()
    {
        var output = RunGenerator(@"
using System.Collections.Generic;
using WebReaper.Extraction.Attributes;

namespace MyApp;

[ScrapeSchema]
public partial class Page
{
    [ScrapeField("".tag"", IsList = true)]
    public List<string> Tags { get; set; } = new();
}");

        Assert.Contains("IsList = true", output);
        Assert.Contains("if (json[\"tags\"] is JsonArray", output);
        Assert.Contains("__list", output);
        Assert.Contains("__list.Add(__n.GetValue<string>())", output);
    }

    [Fact]
    public void Respects_explicit_type_override_on_attribute()
    {
        var output = RunGenerator(@"
using WebReaper.Extraction.Attributes;

namespace MyApp;

[ScrapeSchema]
public partial class Article
{
    // Property is `string`, but the attribute says Integer — the
    // override should win.
    [ScrapeField("".views"", Type = SchemaFieldType.Integer)]
    public string Views { get; set; } = """";
}");

        // Note: the int override produces a Type = DataType.Integer in
        // the emitted Schema. The materializer's coerced-type still
        // uses the property's CLR type for GetValue<T> (string), and
        // the fold's Coerce handles the discrepancy via Coerce-and-
        // swallow-format-error (ADR-0029).
        Assert.Contains("Type = DataType.Integer", output);
    }

    [Fact]
    public void Infers_data_type_from_property_clr_type()
    {
        var output = RunGenerator(@"
using System;
using WebReaper.Extraction.Attributes;

namespace MyApp;

[ScrapeSchema]
public partial class Mixed
{
    [ScrapeField("".s"")] public string? S { get; set; }
    [ScrapeField("".i"")] public int I { get; set; }
    [ScrapeField("".d"")] public double D { get; set; }
    [ScrapeField("".b"")] public bool B { get; set; }
    [ScrapeField("".dt"")] public DateTime Dt { get; set; }
}");

        Assert.Contains("Type = DataType.String", output);
        Assert.Contains("Type = DataType.Integer", output);
        Assert.Contains("Type = DataType.Float", output);
        Assert.Contains("Type = DataType.Boolean", output);
        Assert.Contains("Type = DataType.DataTime", output);
    }

    [Fact]
    public void Ignores_properties_without_scrape_field_attribute()
    {
        var output = RunGenerator(@"
using WebReaper.Extraction.Attributes;

namespace MyApp;

[ScrapeSchema]
public partial class Article
{
    [ScrapeField(""h1"")]
    public string? Title { get; set; }

    // No attribute — should not be in the Schema.
    public string? UntrackedProperty { get; set; }
}");

        Assert.Contains("\"title\"", output);
        // The untracked property must not appear as a SchemaElement.
        Assert.DoesNotContain("UntrackedProperty", output);
        Assert.DoesNotContain("\"untrackedProperty\"", output);
    }

    [Fact]
    public void Camel_cases_property_name_into_json_field_key()
    {
        // The generator lowercases the first character of PascalCase
        // property names so the emitted JSON keys are conventional
        // camelCase — matches the fold's general usage.
        var output = RunGenerator(@"
using WebReaper.Extraction.Attributes;

namespace MyApp;

[ScrapeSchema]
public partial class A
{
    [ScrapeField("".s"")]
    public string? SomeProperty { get; set; }
}");

        Assert.Contains("\"someProperty\"", output);
        Assert.DoesNotContain("\"SomeProperty\"", output);
    }

    // Drive the generator over `source` and return the concatenated
    // generated source text.
    private static string RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create("Test",
            new[] { CSharpSyntaxTree.ParseText(source) },
            new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(ScrapeSchemaAttribute).Assembly.Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Collections").Location),
            },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var generator = new ScrapeSchemaGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);

        driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);

        var result = driver.GetRunResult();

        // Concatenate every generated source file's text.
        var output = new System.Text.StringBuilder();
        foreach (var generated in result.GeneratedTrees)
        {
            output.AppendLine(generated.ToString());
        }

        return output.ToString();
    }
}
