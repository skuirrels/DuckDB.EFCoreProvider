using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Metadata.Internal;
using DuckDB.EFCoreProvider.Storage.Internal;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

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

    /// <inheritdoc />
    /// <remarks>
    ///     MAP and LIST mappings are rebuilt from their constructors and child mappings. <c>Default.Clone(...)</c>
    ///     cannot carry a MAP's key and value mappings, and EF cannot render a LIST converter's looping expression
    ///     tree as C#, so the constructor recreates the converter and comparer instead.
    /// </remarks>
#if NET11_0_OR_GREATER
    public override bool Create(CoreTypeMapping typeMapping, CSharpRuntimeAnnotationCodeGeneratorParameters parameters)
        => TryGetChildMappings(typeMapping, out var childMappings)
            ? CreateFromConstructor((RelationalTypeMapping)typeMapping, childMappings, parameters)
            : base.Create(typeMapping, parameters);
#else
    // A comparer configured on the property is emitted with the property itself, so the rebuilt mapping can keep
    // the comparers its constructor creates.
    public override bool Create(
        CoreTypeMapping typeMapping,
        CSharpRuntimeAnnotationCodeGeneratorParameters parameters,
        ValueComparer? valueComparer = null,
        ValueComparer? keyValueComparer = null,
        ValueComparer? providerValueComparer = null)
        => TryGetChildMappings(typeMapping, out var childMappings)
            ? CreateFromConstructor((RelationalTypeMapping)typeMapping, childMappings, parameters)
            : base.Create(typeMapping, parameters, valueComparer, keyValueComparer, providerValueComparer);
#endif

    private static bool TryGetChildMappings(
        CoreTypeMapping typeMapping,
        out IReadOnlyList<RelationalTypeMapping> childMappings)
    {
        childMappings = typeMapping switch
        {
            DuckDBMapTypeMapping mapMapping => [mapMapping.KeyTypeMapping, mapMapping.ValueTypeMapping],
            DuckDBArrayTypeMapping listMapping => [listMapping.ElementTypeMapping],
            _ => []
        };
        return childMappings.Count > 0;
    }

    private bool CreateFromConstructor(
        RelationalTypeMapping typeMapping,
        IReadOnlyList<RelationalTypeMapping> childMappings,
        CSharpRuntimeAnnotationCodeGeneratorParameters parameters)
    {
        var code = Dependencies.CSharpHelper;
        AddNamespace(typeMapping.GetType(), parameters.Namespaces);
        parameters.MainBuilder
            .Append("new ")
            .Append(code.Reference(typeMapping.GetType()))
            .AppendLine("(")
            .IncrementIndent()
            .Append(code.Literal(typeMapping.StoreType));

        var created = true;
        foreach (var childMapping in childMappings)
        {
            parameters.MainBuilder.AppendLine(",");
            created &= Create(childMapping, parameters);
        }

        parameters.MainBuilder
            .Append(")")
            .DecrementIndent();
        return created;
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

    public override void Generate(IColumn column, CSharpRuntimeAnnotationCodeGeneratorParameters parameters)
    {
        // The value generation strategy is a design-time column annotation for migrations; the runtime
        // relational model does not carry it.
        if (!parameters.IsRuntime)
        {
            parameters.Annotations.Remove(DuckDBAnnotationNames.ValueGenerationStrategy);
        }

        base.Generate(column, parameters);
    }

    public override void Generate(IForeignKeyConstraint foreignKey, CSharpRuntimeAnnotationCodeGeneratorParameters parameters)
    {
        // Marks a struct-field foreign key for migrations only; the runtime relational model does not carry it.
        if (!parameters.IsRuntime)
        {
            parameters.Annotations.Remove(DuckDBAnnotationNames.LogicalStructForeignKey);
        }

        base.Generate(foreignKey, parameters);
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
        var leafArgument = leaf is null ? null : $", {leaf}";
        var rootNullability = field.IsRootNullable is null
            ? null
            : $", {code.Literal(field.IsRootNullable.Value)}";

        return $"new {fieldType}({code.Literal(field.StructColumnName)}, {nestedFields}{leafArgument}{rootNullability})";
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
            + $"new Dictionary<string, {code.Reference(typeof(DuckDBStructChildMapping))}> {{ {children} }})";
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