using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace WebReaper.Extraction.Generators;

// ADR-0045: the Roslyn source generator. Finds every class carrying
// [ScrapeSchema], inspects its [ScrapeField]-decorated properties,
// emits a partial extension with `public static Schema Schema { get; }`
// and `public static T Materialize(JsonObject)`. Compile-time;
// reflection-free output; AOT-clean.

[Generator]
public sealed class ScrapeSchemaGenerator : IIncrementalGenerator
{
    private const string ScrapeSchemaAttributeName = "WebReaper.Extraction.Attributes.ScrapeSchemaAttribute";
    private const string ScrapeFieldAttributeName = "WebReaper.Extraction.Attributes.ScrapeFieldAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var sources = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ScrapeSchemaAttributeName,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, ct) => GatherTarget(ctx, ct))
            .Where(static t => t is not null)
            .Select(static (t, _) => t!);

        context.RegisterSourceOutput(sources, static (spc, target) =>
        {
            var source = Emit(target);
            var hint = $"{target.Namespace}.{target.ClassName}.ScrapeSchema.g.cs"
                .Replace("global::", "")
                .Replace("<", "_")
                .Replace(">", "_");
            spc.AddSource(hint, SourceText.From(source, Encoding.UTF8));
        });
    }

    // The data the generator extracts from a [ScrapeSchema] class —
    // an immutable, equatable record so the incremental cache can
    // skip re-generation when the source hasn't changed.
    private record TargetClass(
        string Namespace,
        string ClassName,
        string FullName,
        bool IsRecord,
        Accessibility Accessibility,
        EquatableArray<TargetField> Fields);

    private record TargetField(
        string PropertyName,
        string PropertyType,
        bool IsNullable,
        string Selector,
        string? Type,        // null → auto-inferred
        bool IsList,
        string? Attr,
        string CoercedType); // the C# accessor type to GetValue<T>() — fold-Coerce-compatible

    private static TargetClass? GatherTarget(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol typeSymbol) return null;

        var fields = new List<TargetField>();

        foreach (var member in typeSymbol.GetMembers())
        {
            if (member is not IPropertySymbol property) continue;
            if (property.GetMethod is null || property.SetMethod is null) continue;

            var fieldAttr = property.GetAttributes().FirstOrDefault(a =>
                a.AttributeClass?.ToDisplayString() == ScrapeFieldAttributeName);

            if (fieldAttr is null) continue;

            var selector = fieldAttr.ConstructorArguments.Length > 0
                ? fieldAttr.ConstructorArguments[0].Value as string ?? string.Empty
                : string.Empty;

            string? type = null;
            bool isList = false;
            string? attr = null;

            foreach (var named in fieldAttr.NamedArguments)
            {
                switch (named.Key)
                {
                    case "Type":
                        // The enum value is the int; we re-encode to
                        // the member name in Emit.
                        type = named.Value.Value?.ToString();
                        break;
                    case "IsList":
                        isList = named.Value.Value is bool b && b;
                        break;
                    case "Attr":
                        attr = named.Value.Value as string;
                        break;
                }
            }

            var (clrType, isNullable, coerced) = InspectPropertyType(property.Type);

            fields.Add(new TargetField(
                PropertyName: property.Name,
                PropertyType: clrType,
                IsNullable: isNullable,
                Selector: selector,
                Type: type,
                IsList: isList,
                Attr: attr,
                CoercedType: coerced));
        }

        var ns = typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : typeSymbol.ContainingNamespace.ToDisplayString();

        return new TargetClass(
            Namespace: ns,
            ClassName: typeSymbol.Name,
            FullName: typeSymbol.ToDisplayString(),
            IsRecord: typeSymbol.IsRecord,
            Accessibility: typeSymbol.DeclaredAccessibility,
            Fields: new EquatableArray<TargetField>(fields.ToImmutableArray()));
    }

    // Inspect a property's CLR type to derive:
    //   - the rendered type for the field's `public T PropertyName`
    //     (used in error diagnostics)
    //   - whether the type is nullable (so the materializer can
    //     guard against null)
    //   - the C# type to call GetValue<T>() with on the JsonNode —
    //     this is the fold-Coerce-compatible accessor type
    //     ("string" for String, "int" for Integer, etc.).
    private static (string ClrType, bool IsNullable, string Coerced) InspectPropertyType(ITypeSymbol type)
    {
        // Handle List<T> / T[] — coerced type is the element type;
        // the materializer iterates.
        if (type is INamedTypeSymbol named && named.IsGenericType
            && named.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.List<T>")
        {
            var element = named.TypeArguments[0];
            var (_, _, coerced) = InspectScalar(element);
            return (type.ToDisplayString(), type.NullableAnnotation == NullableAnnotation.Annotated, coerced);
        }

        if (type is IArrayTypeSymbol array)
        {
            var (_, _, coerced) = InspectScalar(array.ElementType);
            return (type.ToDisplayString(), false, coerced);
        }

        return InspectScalar(type);
    }

    private static (string ClrType, bool IsNullable, string Coerced) InspectScalar(ITypeSymbol type)
    {
        var underlying = type.NullableAnnotation == NullableAnnotation.Annotated && type is INamedTypeSymbol n
            ? n.TypeArguments.FirstOrDefault() ?? type
            : type;

        // System.Nullable<T> → T.
        if (underlying is INamedTypeSymbol named
            && named.OriginalDefinition.ToDisplayString() == "System.Nullable<T>")
        {
            underlying = named.TypeArguments[0];
        }

        var display = underlying.ToDisplayString();

        var coerced = display switch
        {
            "string" => "string",
            "int" or "System.Int32" => "int",
            "long" or "System.Int64" => "long",
            "short" or "System.Int16" => "short",
            "byte" or "System.Byte" => "byte",
            "float" or "System.Single" => "float",
            "double" or "System.Double" => "double",
            "decimal" or "System.Decimal" => "decimal",
            "bool" or "System.Boolean" => "bool",
            "System.DateTime" => "System.DateTime",
            "System.DateTimeOffset" => "System.DateTimeOffset",
            _ => "string"
        };

        return (type.ToDisplayString(),
            type.NullableAnnotation == NullableAnnotation.Annotated,
            coerced);
    }

    private static string Emit(TargetClass target)
    {
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated>");
        sb.AppendLine("// Generated by WebReaper.Extraction.Generators (ADR-0045).");
        sb.AppendLine("// Do not modify by hand — your changes will be overwritten.");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Text.Json.Nodes;");
        sb.AppendLine("using WebReaper.Domain.Parsing;");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(target.Namespace))
        {
            sb.AppendLine($"namespace {target.Namespace};");
            sb.AppendLine();
        }

        var keyword = target.IsRecord ? "record" : "class";
        sb.AppendLine($"partial {keyword} {target.ClassName}");
        sb.AppendLine("{");

        // Static Schema property.
        sb.AppendLine("    /// <summary>The Schema generated from this class's [ScrapeField] properties.</summary>");
        sb.AppendLine("    public static readonly Schema Schema = new Schema");
        sb.AppendLine("    {");

        foreach (var field in target.Fields)
        {
            EmitSchemaElement(sb, field, indent: "        ");
        }

        sb.AppendLine("    };");
        sb.AppendLine();

        // Static Materialize method.
        sb.AppendLine($"    /// <summary>Materialize an instance from the JsonObject the fold produces.</summary>");
        sb.AppendLine($"    public static {target.ClassName} Materialize(JsonObject json)");
        sb.AppendLine("    {");
        sb.AppendLine($"        var result = new {target.ClassName}();");

        foreach (var field in target.Fields)
        {
            EmitMaterializeStatement(sb, field, indent: "        ");
        }

        sb.AppendLine("        return result;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static void EmitSchemaElement(StringBuilder sb, TargetField field, string indent)
    {
        // SchemaElement(field, selector) { Type = ..., IsList = ..., Attr = ... }
        var typeExpr = SchemaTypeExpression(field);
        var selector = EscapeString(field.Selector);

        sb.AppendLine($"{indent}new SchemaElement(\"{ToFieldName(field.PropertyName)}\", \"{selector}\")");
        sb.AppendLine($"{indent}{{");

        if (typeExpr is not null)
            sb.AppendLine($"{indent}    Type = {typeExpr},");

        if (field.IsList)
            sb.AppendLine($"{indent}    IsList = true,");

        if (!string.IsNullOrEmpty(field.Attr))
            sb.AppendLine($"{indent}    Attr = \"{EscapeString(field.Attr!)}\",");

        sb.AppendLine($"{indent}}},");
    }

    private static string? SchemaTypeExpression(TargetField field)
    {
        // Attribute-supplied Type (an enum value name from the
        // SemanticModel side, encoded as the int value here — we map
        // back to the enum name).
        if (field.Type is not null && field.Type != "0")
        {
            return field.Type switch
            {
                "1" => "DataType.String",
                "2" => "DataType.Integer",
                "3" => "DataType.Float",
                "4" => "DataType.Boolean",
                "5" => "DataType.DataTime",
                _ => null
            };
        }

        // Auto-inferred from the CLR coerced type.
        return field.CoercedType switch
        {
            "string" => "DataType.String",
            "int" or "long" or "short" or "byte" => "DataType.Integer",
            "float" or "double" or "decimal" => "DataType.Float",
            "bool" => "DataType.Boolean",
            "System.DateTime" or "System.DateTimeOffset" => "DataType.DataTime",
            _ => null
        };
    }

    private static void EmitMaterializeStatement(StringBuilder sb, TargetField field, string indent)
    {
        var jsonKey = ToFieldName(field.PropertyName);
        var prop = field.PropertyName;
        var coerced = field.CoercedType;

        if (field.IsList)
        {
            sb.AppendLine($"{indent}if (json[\"{jsonKey}\"] is JsonArray {jsonKey}_array)");
            sb.AppendLine($"{indent}{{");
            sb.AppendLine($"{indent}    var __list = new List<{coerced}>();");
            sb.AppendLine($"{indent}    foreach (var __n in {jsonKey}_array)");
            sb.AppendLine($"{indent}    {{");
            sb.AppendLine($"{indent}        if (__n is null) continue;");
            sb.AppendLine($"{indent}        __list.Add(__n.GetValue<{coerced}>());");
            sb.AppendLine($"{indent}    }}");
            // Assign — works for both List<T> and T[] properties.
            sb.AppendLine($"{indent}    result.{prop} = __list" +
                (IsArrayProp(field) ? ".ToArray();" : ";"));
            sb.AppendLine($"{indent}}}");
        }
        else
        {
            sb.AppendLine($"{indent}if (json[\"{jsonKey}\"] is JsonNode {jsonKey}_node)");
            sb.AppendLine($"{indent}{{");
            sb.AppendLine($"{indent}    try {{ result.{prop} = {jsonKey}_node.GetValue<{coerced}>(); }}");
            sb.AppendLine($"{indent}    catch (System.FormatException) {{ /* ADR-0029: log-and-leave-unset is the fold's");
            sb.AppendLine($"{indent}        per-leaf policy; the materializer mirrors it. */ }}");
            sb.AppendLine($"{indent}    catch (System.InvalidOperationException) {{ /* mistyped JsonNode -> default. */ }}");
            sb.AppendLine($"{indent}}}");
        }
    }

    private static bool IsArrayProp(TargetField field) =>
        field.PropertyType.EndsWith("[]", StringComparison.Ordinal);

    // PascalCase → camelCase, e.g. "MyField" → "myField". The
    // fold's JsonObject keys use the field name; we lowercase the
    // first character so JSON consumers see standard JS camelCase.
    private static string ToFieldName(string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName)) return propertyName;
        if (char.IsLower(propertyName[0])) return propertyName;
        return char.ToLowerInvariant(propertyName[0]) + propertyName.Substring(1);
    }

    private static string EscapeString(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}

// EquatableArray<T> — an immutable array wrapper with structural
// equality, required by IIncrementalGenerator's caching: an
// ImmutableArray<T> has reference equality, which breaks cache hits.
// Standard idiom across the .NET source-generator ecosystem.
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
    where T : IEquatable<T>?
{
    private readonly ImmutableArray<T> _array;

    public EquatableArray(ImmutableArray<T> array) => _array = array;

    public T this[int index] => _array[index];
    public int Count => _array.IsDefault ? 0 : _array.Length;

    public bool Equals(EquatableArray<T> other)
    {
        if (_array.IsDefault) return other._array.IsDefault;
        if (other._array.IsDefault) return false;
        if (_array.Length != other._array.Length) return false;

        for (int i = 0; i < _array.Length; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(_array[i], other._array[i]))
                return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        if (_array.IsDefault) return 0;
        var hash = 17;
        foreach (var item in _array)
            hash = hash * 31 + (item is null ? 0 : item.GetHashCode());
        return hash;
    }

    public IEnumerator<T> GetEnumerator() =>
        _array.IsDefault ? Enumerable.Empty<T>().GetEnumerator() : ((IEnumerable<T>)_array).GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
