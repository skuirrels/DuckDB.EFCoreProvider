using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DuckDB.EFCoreProvider.Design.Internal;

/// <summary>
///     Generates provider-specific fluent API calls for STRUCT metadata in migration snapshots.
/// </summary>
public sealed class DuckDBAnnotationCodeGenerator(AnnotationCodeGeneratorDependencies dependencies)
    : AnnotationCodeGenerator(dependencies)
{
    public override IEnumerable<IAnnotation> FilterIgnoredAnnotations(IEnumerable<IAnnotation> annotations)
        => base.FilterIgnoredAnnotations(annotations)
            .Where(annotation => annotation.Name is not DuckDBAnnotationNames.StructMetadata
                and not DuckDBAnnotationNames.StructColumnMap
                and not DuckDBAnnotationNames.StructMapping);

    public override IReadOnlyList<MethodCallCodeFragment> GenerateFluentApiCalls(
        IProperty property,
        IDictionary<string, IAnnotation> annotations)
    {
        var calls = base.GenerateFluentApiCalls(property, annotations).ToList();

        if (annotations.Remove(DuckDBAnnotationNames.StructField, out var fieldAnnotation)
            && fieldAnnotation.Value is DuckDBStructFieldInfo field)
        {
            calls.Add(
                new MethodCallCodeFragment(
                    nameof(DuckDBStructPropertyBuilderExtensions.HasStructField),
                    field.StructColumnName,
                    field.NestedFieldNames.ToArray()));
        }

        AddStructFieldNameCall(calls, annotations);
        annotations.Remove(DuckDBAnnotationNames.StructMapping);
        return calls;
    }

    public override IReadOnlyList<MethodCallCodeFragment> GenerateFluentApiCalls(
        IComplexProperty complexProperty,
        IDictionary<string, IAnnotation> annotations)
    {
        var calls = base.GenerateFluentApiCalls(complexProperty, annotations).ToList();

        if (annotations.Remove(DuckDBAnnotationNames.UseStructMapping, out var useMapping)
            && useMapping.Value is true)
        {
            calls.Add(
                annotations.Remove(DuckDBAnnotationNames.StructColumnName, out var root)
                    && root.Value is string rootName
                        ? new MethodCallCodeFragment(
                            nameof(DuckDBStructPropertyBuilderExtensions.UseStructMapping),
                            rootName)
                        : new MethodCallCodeFragment(nameof(DuckDBStructPropertyBuilderExtensions.UseStructMapping)));
        }
        else
        {
            annotations.Remove(DuckDBAnnotationNames.StructColumnName);
        }

        AddStructFieldNameCall(calls, annotations);
        annotations.Remove(DuckDBAnnotationNames.StructMapping);
        return calls;
    }

    public override IReadOnlyList<MethodCallCodeFragment> GenerateFluentApiCalls(
        IComplexType complexType,
        IDictionary<string, IAnnotation> annotations)
    {
        var calls = base.GenerateFluentApiCalls(complexType, annotations).ToList();
        annotations.Remove(DuckDBAnnotationNames.StructMapping);
        return calls;
    }

    private static void AddStructFieldNameCall(
        ICollection<MethodCallCodeFragment> calls,
        IDictionary<string, IAnnotation> annotations)
    {
        if (annotations.Remove(DuckDBAnnotationNames.StructFieldName, out var fieldName)
            && fieldName.Value is string name)
        {
            calls.Add(
                new MethodCallCodeFragment(
                    nameof(DuckDBStructPropertyBuilderExtensions.HasStructFieldName),
                    name));
        }
    }
}