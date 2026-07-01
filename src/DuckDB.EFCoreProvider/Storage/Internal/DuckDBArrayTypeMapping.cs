using DuckDB.EFCoreProvider.Extensions.Internal;
using DuckDB.EFCoreProvider.Storage.ValueConverters;
using DuckDB.NET.Data;
using DuckDB.NET.Native;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace DuckDB.EFCoreProvider.Storage.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public abstract class DuckDBArrayTypeMapping : RelationalTypeMapping
{
    protected DuckDBArrayTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    public override RelationalTypeMapping ElementTypeMapping
    {
        get
        {
            var elementTypeMapping = base.ElementTypeMapping;
            
            Debug.Assert(
                elementTypeMapping is not null,
                "DuckDBArrayTypeMapping without an element type mapping");
            Debug.Assert(
                elementTypeMapping is RelationalTypeMapping,
                "DuckDBArrayTypeMapping with non-relational element type mapping");
            return (RelationalTypeMapping)elementTypeMapping;
        }
    }
}

public class DuckDBArrayTypeMapping<TCollection, TConcreteCollection, TElement> : DuckDBArrayTypeMapping
{
    public static DuckDBArrayTypeMapping<TCollection, TConcreteCollection, TElement> Default { get; } = new();

    public virtual DuckDBType? DuckDBDbType { get; }

    public DuckDBArrayTypeMapping(RelationalTypeMapping elementTypeMapping)
        : this(elementTypeMapping.StoreType + "[]", elementTypeMapping)
    {
    }

    public DuckDBArrayTypeMapping(string storeType, RelationalTypeMapping elementTypeMapping)
        : this(CreateParameters(storeType, elementTypeMapping))
    {
        Debug.Assert(storeType.EndsWith("[]", StringComparison.Ordinal), "DuckDBArrayTypeMapping created for a non-array store type");
    }

    private static RelationalTypeMappingParameters CreateParameters(string storeType, RelationalTypeMapping elementMapping)
    {
        ValueConverter? converter = null;

        var elementType = typeof(TCollection).TryGetElementType(typeof(IEnumerable<>)) ?? typeof(TCollection).GetElementType();

        Debug.Assert(elementType is not null, "modelElementType cannot be null");

        if (elementMapping.Converter is { } elementConverter)
        {
            var providerElementType = elementConverter.ProviderClrType;

            if (elementType.IsNullableValueType())
            {
                providerElementType = providerElementType.MakeNullable();
            }

            converter = (ValueConverter)Activator.CreateInstance(
                typeof(DuckDBArrayConverter<,,>).MakeGenericType(
                    typeof(TCollection), typeof(TConcreteCollection), typeof(List<>).MakeGenericType(providerElementType)),
                elementConverter)!;
        }
        else if (typeof(TCollection) != typeof(TConcreteCollection))
        {
            converter = (ValueConverter)Activator.CreateInstance(
                typeof(DuckDBArrayConverter<,,>).MakeGenericType(
                    typeof(TCollection), typeof(TConcreteCollection), typeof(List<>).MakeGenericType(elementType)))!;
        }
        else if (typeof(TCollection) != typeof(TConcreteCollection) || typeof(TCollection).IsArray)
        {
            converter = (ValueConverter)Activator.CreateInstance(
                typeof(DuckDBArrayConverter<,,>).MakeGenericType(
                    typeof(TCollection), typeof(TConcreteCollection), typeof(List<>).MakeGenericType(elementType)))!;
        }

        #pragma warning disable EF1001
        var comparer = typeof(TCollection).IsArray && typeof(TCollection).GetArrayRank() > 1
            // Multidimensional arrays have no element value comparer here, so change tracking compares them by
            // reference. This is a known limitation; single-dimension arrays and lists use a deep comparer.
            ? null
            : (ValueComparer?)Activator.CreateInstance(
                elementType.IsNullableValueType() || elementMapping.Comparer.Type.IsNullableValueType()
                    ? typeof(ListOfNullableValueTypesComparer<,>)
                        .MakeGenericType(typeof(TConcreteCollection), elementType.UnwrapNullableType())
                    : elementType.IsValueType
                        ? typeof(ListOfValueTypesComparer<,>).MakeGenericType(typeof(TConcreteCollection), elementType)
                        : typeof(ListOfReferenceTypesComparer<,>).MakeGenericType(typeof(TConcreteCollection), elementType),
                elementMapping.Comparer.ToNullableComparer(elementType)!);
#pragma warning restore EF1001

        var elementJsonReaderWriter = elementMapping.JsonValueReaderWriter;
        if (elementJsonReaderWriter is not null && !typeof(TElement).UnwrapNullableType().IsAssignableTo(elementJsonReaderWriter.ValueType))
        {
            throw new InvalidOperationException(
                $"When building an array mapping over '{typeof(TElement).Name}', the JsonValueReaderWriter for element mapping '{elementMapping.GetType().Name}' is incorrect ('{elementJsonReaderWriter.ValueType.GetType().Name}' instead of '{typeof(TElement).UnwrapNullableType()}', the JsonValueReaderWriter is '{elementJsonReaderWriter.GetType().Name}').");
        }

        var collectionJsonReaderWriter =
            elementJsonReaderWriter is null || typeof(TCollection).IsArray && typeof(TCollection).GetArrayRank() > 1
                ? null
                : (JsonValueReaderWriter?)Activator.CreateInstance(
                    (elementType.IsNullableValueType()
                        ? typeof(JsonCollectionOfNullableStructsReaderWriter<,>)
                        : elementType.IsValueType
                            ? typeof(JsonCollectionOfStructsReaderWriter<,>)
                            : typeof(JsonCollectionOfReferencesReaderWriter<,>))
                    .MakeGenericType(typeof(TConcreteCollection), elementType.UnwrapNullableType()),
                    elementJsonReaderWriter);

        return new RelationalTypeMappingParameters(
            new CoreTypeMappingParameters(
                typeof(TCollection), converter, comparer, elementMapping: elementMapping,
                jsonValueReaderWriter: collectionJsonReaderWriter),
            storeType);
    }

    protected DuckDBArrayTypeMapping(RelationalTypeMappingParameters parameters) : base(parameters)
    {
        var clrType = parameters.CoreParameters.ClrType;

        if (clrType.TryGetElementType(typeof(IEnumerable<>)) == null && clrType.GetElementType() == null)
        {
            //throw new ArgumentException($"CLR type '{parameters.CoreParameters.ClrType}' isn't an IEnumerable");
        }

        DuckDBDbType = parameters.FixedLength ? DuckDBType.Array : DuckDBType.List;
    }

    private DuckDBArrayTypeMapping()
        : base(new RelationalTypeMappingParameters(
            new CoreTypeMappingParameters(typeof(TCollection), elementMapping: NullMapping),
            storeType: "INTEGER[]"))
    {
    }

    /// <inheritdoc />
    public override DbParameter CreateParameter(
        DbCommand command,
        string name,
        object? value,
        bool? nullable = null,
        ParameterDirection direction = ParameterDirection.Input)
    {
        if (value is not null && Converter is null && !value.GetType().IsArrayOrGenericList())
        {
            switch (value)
            {
                case IEnumerable<TElement> elements:
                    value = elements.ToList();
                    break;

                case IEnumerable elements:
                    value = elements.Cast<TElement>().ToList();
                    break;
            }
        }
        
        var param = base.CreateParameter(command, name, value, nullable, direction);

        if (param is not DuckDBParameter)
        {
            throw new InvalidOperationException(
                $"DuckDB-specific type mapping {GetType().Name} being used with non-DuckDB parameter type {param.GetType().Name}");
        }

        // The array element type is inferred by DuckDB from the bound list value; the parameter's explicit
        // DuckDB type name is intentionally not set here.
        return param;
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
    {
        Debug.Assert(
            parameters.CoreParameters.ClrType == typeof(TCollection), "DuckDBArrayTypeMapping.Clone attempting to change ClrType");
        Debug.Assert(
            parameters.CoreParameters.ElementTypeMapping is not null, "DuckDBArrayTypeMapping.Clone without an element type mapping");
        Debug.Assert(
            parameters.CoreParameters.ElementTypeMapping.ClrType == typeof(TElement).UnwrapNullableType(),
            "DuckDBArrayTypeMapping.Clone attempting to change element ClrType");

        return new DuckDBArrayTypeMapping<TCollection, TConcreteCollection, TElement>(parameters);
    }

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value)
    {
        if (value is not IEnumerable enumerable)
        {
            throw new ArgumentException($"'{value}' is not an IEnumerable", nameof(value));
        }

        if (value is Array array && array.Rank != 1)
        {
            throw new NotSupportedException("Multidimensional arrays are not supported");
        }

        var sb = new StringBuilder("[");
        
        var isFirst = true;

        foreach (var element in enumerable)
        {
            if (isFirst)
            {
                isFirst = false;
            }
            else
            {
                sb.Append(", ");
            }
            
            sb.Append(ElementTypeMapping.GenerateProviderValueSqlLiteral(element));
        }
        
        sb.Append("]::").Append(ElementTypeMapping.StoreType).Append("[]");

        return sb.ToString();
    }

    /// <inheritdoc />
    protected override void ConfigureParameter(DbParameter parameter)
    {
        ((DuckDBParameter)parameter).RemoveDollarSign();
        base.ConfigureParameter(parameter);
    }

    /// <inheritdoc />
    public override MethodInfo GetDataReaderMethod()
    {
        var elementMapping = ElementTypeMapping;
        var elementType = typeof(TCollection).TryGetElementType(typeof(IEnumerable<>))
            ?? typeof(TCollection).GetElementType()
            ?? throw new InvalidOperationException(
                $"Could not determine the element type for array/collection type '{typeof(TCollection)}'.");

        if (elementMapping.Converter is { } elementConverter)
        {
            var providerElementType = elementConverter.ProviderClrType;

            if (elementType.IsNullableValueType())
            {
                providerElementType = providerElementType.MakeNullable();
            }

            return GetDataReaderMethod(typeof(List<>).MakeGenericType(providerElementType));
        }

        return GetDataReaderMethod(typeof(List<>).MakeGenericType(elementType));
    }
}
