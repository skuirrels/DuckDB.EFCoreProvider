using DuckDB.EFCoreProvider.Storage.Internal;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using static System.Linq.Expressions.Expression;

namespace DuckDB.EFCoreProvider.Storage.ValueConverters;

/// <summary>
///     A <see cref="ValueConverter{TModel, TProvider}" /> that packs a complex CLR
///     object (e.g. <c>Location</c>) to an <see cref="IReadOnlyDictionary{TKey, TValue}" />
///     of DuckDB struct field name → provider value, and unpacks the reverse. Nested
///     complex sub-fields are recursively packed to nested dictionaries using the
///     nested <see cref="DuckDBStructTypeMapping" />'s own
///     <see cref="DuckDBStructValueConverter{TClr}" />.
/// </summary>
/// <typeparam name="TClr">
///     The complex CLR type mapped to a single DuckDB STRUCT column. Must be a
///     reference type with a public parameterless constructor (so unpacking can
///     instantiate it).
/// </typeparam>
/// <remarks>
///     This is an internal API that supports the Entity Framework Core
///     infrastructure. It may change or be removed without notice between releases.
/// </remarks>
public sealed class DuckDBStructValueConverter<TClr>
    : ValueConverter<TClr, IReadOnlyDictionary<string, object?>>
    where TClr : class, new()
{
    /// <summary>
    ///     The ordered struct field plans used to build this converter. Exposed for
    ///     diagnostics: the compiled pack/unpack delegates carry the same structure.
    /// </summary>
    public IReadOnlyList<DuckDBStructFieldPlan> Fields { get; }

    /// <summary>
    ///     Initializes a new <see cref="DuckDBStructValueConverter{TClr}" />.
    /// </summary>
    /// <param name="fields">
    ///     Ordered list of struct field plans. Each plan carries the field's DuckDB
    ///     name, the <see cref="PropertyInfo" /> reading/writing the value on
    ///     <typeparamref name="TClr" />, and the field's relational type mapping.
    ///     For nested struct fields, <see cref="DuckDBStructFieldPlan.FieldMapping" />
    ///     is a <see cref="DuckDBStructTypeMapping" /> with its own
    ///     <see cref="ValueConverter{TModel, TProvider}" />.
    /// </param>
    public DuckDBStructValueConverter(IReadOnlyList<DuckDBStructFieldPlan> fields)
        : base(BuildPackExpression(fields), BuildUnpackExpression(fields))
    {
        Fields = fields;
    }

    private static Expression<Func<TClr, IReadOnlyDictionary<string, object?>>> BuildPackExpression(
        IReadOnlyList<DuckDBStructFieldPlan> fields)
    {
        var input = Parameter(typeof(TClr), "input");
        var dictVar = Variable(typeof(Dictionary<string, object?>), "dict");
        var dictCtor = typeof(Dictionary<string, object?>).GetConstructor([typeof(int)])!;
        var addMethod = typeof(Dictionary<string, object?>).GetMethod(
            nameof(Dictionary<string, object?>.Add),
                    [typeof(string), typeof(object)])!;

        var expressions = new List<Expression>
        {
            Assign(dictVar, New(dictCtor, Constant(fields.Count))),
        };

        foreach (var field in fields)
        {
            // fieldValue = input.<Property>
            Expression fieldValue = Property(input, field.Property);

            // For nested struct fields, recursively pack to a nested dictionary via the
            // nested DuckDBStructTypeMapping's own value converter. The lambda's parameter
            // type is the nested converter's ModelClrType (= field.Property.PropertyType).
            if (field.FieldMapping is DuckDBStructTypeMapping
                && field.FieldMapping.Converter is { } nestedConverter)
            {
                fieldValue = Invoke(nestedConverter.ConvertToProviderExpression, fieldValue);
            }

            // dict.Add(fieldName, (object?)fieldValue)
            expressions.Add(
                Call(dictVar,
                    addMethod,
                    Constant(field.FieldName),
                    Convert(fieldValue, typeof(object))));
        }

        expressions.Add(dictVar);

        // if (input is null) return null; else { ... }
        var body = Condition(
            ReferenceEqual(input, Constant(null, typeof(TClr))),
            Constant(null, typeof(IReadOnlyDictionary<string, object?>)),
            Block(dictVar.Type, new[] { dictVar }, expressions));

        return Lambda<Func<TClr, IReadOnlyDictionary<string, object?>>>(body, input);
    }

    private static Expression<Func<IReadOnlyDictionary<string, object?>, TClr>> BuildUnpackExpression(
        IReadOnlyList<DuckDBStructFieldPlan> fields)
    {
        var input = Parameter(typeof(IReadOnlyDictionary<string, object?>), "input");
        var outputVar = Variable(typeof(TClr), "output");
        var modelCtor = typeof(TClr).GetConstructor(Type.EmptyTypes)
            ?? throw new InvalidOperationException(
                $"DuckDBStructValueConverter<{typeof(TClr).Name}> requires a public parameterless constructor on {typeof(TClr).Name}.");

        var indexerProperty = typeof(IReadOnlyDictionary<string, object?>).GetProperty("Item")
            ?? throw new InvalidOperationException(
                "Could not find Item indexer on IReadOnlyDictionary<string,object?>.");

        var expressions = new List<Expression>
        {
            Assign(outputVar, New(modelCtor)),
        };

        foreach (var field in fields)
        {
            // dictValue = input[fieldName] (typed object?)
            Expression dictValue = Property(input, indexerProperty, Constant(field.FieldName));

            Expression resolvedValue = dictValue;
            if (field.FieldMapping is DuckDBStructTypeMapping
                && field.FieldMapping.Converter is { } nestedConverter)
            {
                // Nested unpack: invoke ConvertFromProvider with the nested dictionary.
                // (object?) → IReadOnlyDictionary<string,object?>
                resolvedValue = Invoke(
                    nestedConverter.ConvertFromProviderExpression,
                    Convert(dictValue, nestedConverter.ProviderClrType));
            }

            // output.Property = Convert(resolvedValue, field.Property.PropertyType)
            expressions.Add(
                Assign(
                    Property(outputVar, field.Property),
                    Convert(resolvedValue, field.Property.PropertyType)));
        }

        expressions.Add(outputVar);

        var body = Condition(
            ReferenceEqual(input, Constant(null, typeof(IReadOnlyDictionary<string, object?>))),
            Constant(null, typeof(TClr)),
            Block(outputVar.Type, new[] { outputVar }, expressions));

        return Lambda<Func<IReadOnlyDictionary<string, object?>, TClr>>(body, input);
    }
}