using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DuckDB.EFCoreProvider.Design.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBCSharpRuntimeAnnotationCodeGenerator : RelationalCSharpRuntimeAnnotationCodeGenerator
{
    public DuckDBCSharpRuntimeAnnotationCodeGenerator(
        CSharpRuntimeAnnotationCodeGeneratorDependencies dependencies,
        RelationalCSharpRuntimeAnnotationCodeGeneratorDependencies relationalDependencies)
        : base(dependencies, relationalDependencies)
    {
    }

    public override void Generate(IModel model, CSharpRuntimeAnnotationCodeGeneratorParameters parameters)
    {
        RemoveOpaqueAnnotations(parameters);
        base.Generate(model, parameters);
    }

    public override void Generate(IEntityType entityType, CSharpRuntimeAnnotationCodeGeneratorParameters parameters)
    {
        RemoveOpaqueAnnotations(parameters);
        base.Generate(entityType, parameters);
    }

    public override void Generate(IComplexProperty complexProperty, CSharpRuntimeAnnotationCodeGeneratorParameters parameters)
    {
        RemoveOpaqueAnnotations(parameters);
        base.Generate(complexProperty, parameters);
    }

    public override void Generate(IComplexType complexType, CSharpRuntimeAnnotationCodeGeneratorParameters parameters)
    {
        RemoveOpaqueAnnotations(parameters);
        base.Generate(complexType, parameters);
    }

    public override void Generate(IProperty property, CSharpRuntimeAnnotationCodeGeneratorParameters parameters)
    {
        RemoveOpaqueAnnotations(parameters);
        base.Generate(property, parameters);
    }

    protected override void GenerateSimpleAnnotations(CSharpRuntimeAnnotationCodeGeneratorParameters parameters)
    {
        foreach (var (name, value) in parameters.Annotations.OrderBy(annotation => annotation.Key))
        {
            if (value is DuckDBStructFieldInfo field)
            {
                AddNamespace(typeof(DuckDBStructFieldInfo), parameters.Namespaces);
                GenerateSimpleAnnotation(name, GenerateFieldLiteral(field), parameters);
            }
            else if (value is DuckDBStructMapping mapping)
            {
                AddNamespace(typeof(DuckDBStructMapping), parameters.Namespaces);
                AddNamespace(typeof(DuckDBStructChildMapping), parameters.Namespaces);
                GenerateSimpleAnnotation(name, GenerateMappingLiteral(mapping), parameters);
            }
            else if (value is IReadOnlyDictionary<string, DuckDBStructFieldInfo> columnMap)
            {
                AddNamespace(typeof(DuckDBStructFieldInfo), parameters.Namespaces);
                GenerateSimpleAnnotation(name, GenerateColumnMapLiteral(columnMap), parameters);
            }
            else
            {
                base.GenerateSimpleAnnotations(
                    parameters with
                    {
                        Annotations = new Dictionary<string, object?> { [name] = value }
                    });
            }
        }
    }

    private static void RemoveOpaqueAnnotations(CSharpRuntimeAnnotationCodeGeneratorParameters parameters)
    {
        if (parameters.Annotations.TryGetValue(
                DuckDBAnnotationNames.StructMetadata,
                out var metadataValue)
            && metadataValue is DuckDBStructEntityMetadata metadata)
        {
            parameters.Annotations[DuckDBAnnotationNames.StructColumnMap] = metadata.Columns;
        }

        parameters.Annotations.Remove(DuckDBAnnotationNames.StructMetadata);
    }

    private string GenerateFieldLiteral(DuckDBStructFieldInfo field)
    {
        var code = Dependencies.CSharpHelper;
        var fieldType = code.Reference(typeof(DuckDBStructFieldInfo));
        var nestedFields = code.Literal(field.NestedFieldNames.ToArray());
        var leaf = field.LeafFieldName is null ? null : code.Literal(field.LeafFieldName);

        return leaf is null
            ? $"new {fieldType}({code.Literal(field.StructColumnName)}, {nestedFields})"
            : $"new {fieldType}({code.Literal(field.StructColumnName)}, {nestedFields}, {leaf})";
    }

    private string GenerateMappingLiteral(DuckDBStructMapping mapping)
    {
        var code = Dependencies.CSharpHelper;
        var children = string.Join(
            ", ",
            mapping.Children
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair =>
                    $"[{code.Literal(pair.Key)}] = {GenerateChildLiteral(pair.Value)}"));

        return $"new {code.Reference(typeof(DuckDBStructMapping))}("
            + $"{code.Literal(mapping.StructColumnName)}, {code.Literal(mapping.FieldName)}, "
            + $"new Dictionary<string, {code.Reference(typeof(DuckDBStructChildMapping))}> {{ {children} }}, "
            + $"{code.Literal(mapping.SelectiveProjection)})";
    }

    private string GenerateColumnMapLiteral(
        IReadOnlyDictionary<string, DuckDBStructFieldInfo> columnMap)
    {
        var code = Dependencies.CSharpHelper;
        var entries = string.Join(
            ", ",
            columnMap
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair =>
                    $"[{code.Literal(pair.Key)}] = {GenerateFieldLiteral(pair.Value)}"));

        return $"new Dictionary<string, {code.Reference(typeof(DuckDBStructFieldInfo))}> {{ {entries} }}";
    }

    private string GenerateChildLiteral(DuckDBStructChildMapping child)
    {
        var code = Dependencies.CSharpHelper;
        var nested = child.Nested is null ? "null" : GenerateMappingLiteral(child.Nested);
        return $"new {code.Reference(typeof(DuckDBStructChildMapping))}({code.Literal(child.FieldName)}, {nested})";
    }
}