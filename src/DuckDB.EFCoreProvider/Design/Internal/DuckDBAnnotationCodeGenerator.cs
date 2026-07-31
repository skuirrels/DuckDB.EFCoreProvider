using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Design.Internal;

/// <summary>
///     Generates provider-specific fluent API calls for STRUCT metadata in migration snapshots.
/// </summary>
internal sealed class DuckDBAnnotationCodeGenerator(AnnotationCodeGeneratorDependencies dependencies)
    : AnnotationCodeGenerator(dependencies)
{
    private static readonly MethodInfo ComplexPropertyUseStructMappingMethod =
        GetMethod(
            nameof(DuckDBStructPropertyBuilderExtensions.UseStructMapping),
            typeof(ComplexPropertyBuilder),
            parameterCount: 1);

    private static readonly MethodInfo ComplexPropertyUseStructMappingWithColumnNameMethod =
        GetMethod(
            nameof(DuckDBStructPropertyBuilderExtensions.UseStructMapping),
            typeof(ComplexPropertyBuilder),
            parameterCount: 2,
            parameterTypes: [typeof(string)]);

    private static readonly MethodInfo ComplexPropertyUseStructMappingSelectiveMethod =
        GetMethod(
            nameof(DuckDBStructPropertyBuilderExtensions.UseStructMapping),
            typeof(ComplexPropertyBuilder),
            parameterCount: 2,
            parameterTypes: [typeof(bool)]);

    private static readonly MethodInfo ComplexPropertyUseStructMappingWithColumnNameSelectiveMethod =
        GetMethod(
            nameof(DuckDBStructPropertyBuilderExtensions.UseStructMapping),
            typeof(ComplexPropertyBuilder),
            parameterCount: 3);

    private static readonly MethodInfo ComplexPropertyHasStructFieldNameMethod =
        GetMethod(
            nameof(DuckDBStructPropertyBuilderExtensions.HasStructFieldName),
            typeof(ComplexPropertyBuilder),
            parameterCount: 2);

    private static readonly MethodInfo ComplexTypePropertyHasStructFieldMethod =
        GetGenericMethod(
            nameof(DuckDBStructPropertyBuilderExtensions.HasStructField),
            typeof(ComplexTypePropertyBuilder<>),
            parameterCount: 3);

    private static readonly MethodInfo PropertyHasStructFieldMethod =
        GetGenericMethod(
            nameof(DuckDBStructPropertyBuilderExtensions.HasStructField),
            typeof(PropertyBuilder<>),
            parameterCount: 3);

    private static readonly MethodInfo ComplexTypePropertyHasStructFieldNameMethod =
        GetGenericMethod(
            nameof(DuckDBStructPropertyBuilderExtensions.HasStructFieldName),
            typeof(ComplexTypePropertyBuilder<>),
            parameterCount: 2);

    private static readonly MethodInfo PropertyHasStructFieldNameMethod =
        GetGenericMethod(
            nameof(DuckDBStructPropertyBuilderExtensions.HasStructFieldName),
            typeof(PropertyBuilder<>),
            parameterCount: 2);

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
        string? inferredFieldName = null;

        if (annotations.Remove(DuckDBAnnotationNames.StructField, out var fieldAnnotation)
            && fieldAnnotation.Value is DuckDBStructFieldInfo field)
        {
            inferredFieldName = field.LeafFieldName;
            calls.Add(
                new DuckDBMethodCallCodeFragment(
                    GetHasStructFieldMethod(property),
                    field.StructColumnName,
                    field.NestedFieldNames.ToArray()));
        }

        AddStructFieldNameCall(calls, annotations, property, inferredFieldName);
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
            var selectiveProjection = annotations.Remove(
                DuckDBAnnotationNames.SelectiveStructProjection,
                out var selective)
                && selective.Value is true;
            var hasRootName = annotations.Remove(DuckDBAnnotationNames.StructColumnName, out var root)
                && root.Value is string rootName;

            calls.Add(
                selectiveProjection
                    ? hasRootName
                        ? new DuckDBMethodCallCodeFragment(
                            ComplexPropertyUseStructMappingWithColumnNameSelectiveMethod,
                            root!.Value!,
                            true)
                        : new DuckDBMethodCallCodeFragment(
                            ComplexPropertyUseStructMappingSelectiveMethod,
                            true)
                    : hasRootName
                        ? new DuckDBMethodCallCodeFragment(
                            ComplexPropertyUseStructMappingWithColumnNameMethod,
                            root!.Value!)
                        : new DuckDBMethodCallCodeFragment(ComplexPropertyUseStructMappingMethod));
        }
        else
        {
            annotations.Remove(DuckDBAnnotationNames.StructColumnName);
            annotations.Remove(DuckDBAnnotationNames.SelectiveStructProjection);
        }

        AddStructFieldNameCall(calls, annotations, property: null);
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
        IDictionary<string, IAnnotation> annotations,
        IProperty? property,
        string? inferredFieldName = null)
    {
        string? fieldName = inferredFieldName;
        if (annotations.Remove(DuckDBAnnotationNames.StructFieldName, out var fieldNameAnnotation)
            && fieldNameAnnotation.Value is string name)
        {
            fieldName = name;
        }

        if (fieldName is not null)
        {
            calls.Add(
                new DuckDBMethodCallCodeFragment(
                    property is null
                        ? ComplexPropertyHasStructFieldNameMethod
                        : GetHasStructFieldNameMethod(property),
                    fieldName));
        }
    }

    private static MethodInfo GetHasStructFieldMethod(IProperty property)
        => (property.DeclaringType is IComplexType
                ? ComplexTypePropertyHasStructFieldMethod
                : PropertyHasStructFieldMethod)
            .MakeGenericMethod(property.ClrType);

    private static MethodInfo GetHasStructFieldNameMethod(IProperty property)
        => (property.DeclaringType is IComplexType
                ? ComplexTypePropertyHasStructFieldNameMethod
                : PropertyHasStructFieldNameMethod)
            .MakeGenericMethod(property.ClrType);

    private static MethodInfo GetMethod(
        string name,
        Type builderType,
        int parameterCount,
        Type[]? parameterTypes = null)
        => typeof(DuckDBStructPropertyBuilderExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method =>
                method.Name == name
                && !method.IsGenericMethodDefinition
                && method.GetParameters().Length == parameterCount
                && method.GetParameters()[0].ParameterType == builderType
                && (parameterTypes is null
                    || method.GetParameters()
                        .Skip(1)
                        .Select(parameter => parameter.ParameterType)
                        .SequenceEqual(parameterTypes)));

    private static MethodInfo GetGenericMethod(string name, Type builderType, int parameterCount)
        => typeof(DuckDBStructPropertyBuilderExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method =>
                method.Name == name
                && method.IsGenericMethodDefinition
                && method.GetParameters().Length == parameterCount
                && method.GetParameters()[0].ParameterType.IsGenericType
                && method.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == builderType);

    private sealed class DuckDBMethodCallCodeFragment : MethodCallCodeFragment
    {
        public DuckDBMethodCallCodeFragment(MethodInfo methodInfo, params object?[] arguments)
            : base(methodInfo, arguments)
        {
        }

        // EF's type-qualified renderer uses DeclaringType but does not emit Namespace.
        public override string DeclaringType
            => $"{base.Namespace}.{base.DeclaringType}";
    }
}