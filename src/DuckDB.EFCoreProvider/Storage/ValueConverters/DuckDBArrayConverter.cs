using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using static System.Linq.Expressions.Expression;

namespace DuckDB.EFCoreProvider.Storage.ValueConverters;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBArrayConverter<TModelCollection, TConcreteModelCollection, TProviderCollection>
    : ValueConverter<TModelCollection, TProviderCollection>
    where TModelCollection: IEnumerable
    where TConcreteModelCollection: IEnumerable
    where TProviderCollection: IEnumerable
{
    public virtual ValueConverter? ElementConverter { get; }

    public DuckDBArrayConverter()
        : this(elementConverter: null)
    {
    }

    public DuckDBArrayConverter(ValueConverter? elementConverter)
        : base(
            ArrayConversionExpression<TModelCollection, TProviderCollection, TProviderCollection>(elementConverter?.ConvertToProviderExpression),
            ArrayConversionExpression<TProviderCollection, TModelCollection, TConcreteModelCollection>(elementConverter?.ConvertFromProviderExpression))
    {
        var modelElementType = typeof(TModelCollection).TryGetElementType(typeof(IEnumerable<>));
        var providerElementType = typeof(TProviderCollection).TryGetElementType(typeof(IEnumerable<>));

        if (modelElementType is null || providerElementType is null)
        {
            throw new ArgumentException("Can only convert between arrays");
        }

        if (elementConverter is not null)
        {
            if (modelElementType.UnwrapNullableType() != elementConverter.ModelClrType.UnwrapNullableType())
            {
                throw new ArgumentException($"The element's value converter model type ({elementConverter.ModelClrType}) doesn't match the array's ({modelElementType})");
            }
            
            if (providerElementType.UnwrapNullableType() != elementConverter.ProviderClrType.UnwrapNullableType())
            {
                throw new ArgumentException($"The element's value converter provider type ({elementConverter.ProviderClrType}) doesn't match the array's ({providerElementType})");
            }
        }

        ElementConverter = elementConverter;
    }

    private static Expression<Func<TInput, TOutput>> ArrayConversionExpression<TInput, TOutput, TConcreteOutput>(LambdaExpression? elementConversionExpression)
    {
        var inputElementType = typeof(TInput).IsArray
            ? typeof(TInput).GetElementType()
            : typeof(TInput).TryGetElementType(typeof(IEnumerable<>));
        
        var outputElementType = typeof(TOutput).IsArray
            ? typeof(TOutput).GetElementType()
            : typeof(TOutput).TryGetElementType(typeof(IEnumerable<>));

        if (inputElementType is null || outputElementType is null)
        {
            throw new ArgumentException("Both TInput and TOutput must be arrays or IList<T>");
        }

        if (elementConversionExpression is not null && inputElementType.IsNullableType() && outputElementType.IsNullableType())
        {
            var p = Parameter(inputElementType, "foo");
            elementConversionExpression = Lambda(
                Condition(
                    Equal(p, Constant(null, inputElementType)),
                    Constant(null, outputElementType),
                    Convert(
                        Invoke(
                            elementConversionExpression,
                            elementConversionExpression.Parameters[0].Type.IsNullableType()
                                ? p
                                : Convert(p, inputElementType.UnwrapNullableType())),
                        outputElementType)),
                p);
        }
        
        var input = Parameter(typeof(TInput), "input");
        var convertedInput = input;
        var output = Parameter(typeof(TConcreteOutput), "result");
        var lengthVariable = Variable(typeof(int), "length");

        var expressions = new List<Expression>();
        var variables = new List<ParameterExpression> { output, lengthVariable };

        Expression getInputLength;
        Func<Expression, Expression>? indexer;
        var inputInterfaces = input.Type.GetInterfaces();

        switch (input.Type)
        {
            case { IsArray: true }:
                getInputLength = ArrayLength(input);
                indexer = i => ArrayAccess(input, i);
                break;

            case { IsGenericType: true } when inputInterfaces.Append(input.Type).Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICollection<>)):
                {
                    getInputLength = Property(
                        input,
                        input.Type.GetProperty("Count")
                        ?? typeof(ICollection<>).MakeGenericType(input.Type.GetGenericArguments()[0]).GetProperty("Count")!);
                    indexer = null;
                    break;
                }

            case { IsGenericType: true } when inputInterfaces.Append(input.Type).Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)):
                {
                    convertedInput = Variable(typeof(List<>).MakeGenericType(inputElementType), "convertedInput");
                    variables.Add(convertedInput);
                    expressions.Add(
                        Assign(
                            convertedInput,
                            Call(typeof(Enumerable).GetMethod(nameof(Enumerable.ToList))!.MakeGenericMethod(inputElementType), input)));
                    getInputLength = Property(convertedInput, convertedInput.Type.GetProperty("Count")!);
                    indexer = i => Property(convertedInput, convertedInput.Type.FindIndexerProperty()!, i);
                    break;
                }
            
            default:
                throw new NotSupportedException($"Array value converter input type must be an IEnumerable, but is {typeof(TInput)}");
        }

        Expression? instantiateOutput = typeof(TConcreteOutput) switch
        {
            var t when t.IsArray => NewArrayBounds(outputElementType, lengthVariable),
            var t when typeof(TConcreteOutput).GetConstructor([typeof(int)]) is ConstructorInfo ctorWithLength => New(ctorWithLength, lengthVariable),
            var t when typeof(TConcreteOutput).GetConstructor([]) is not null => New(typeof(TConcreteOutput)),

            _ => null
        };

        if (instantiateOutput is null)
        {
            return Lambda<Func<TInput, TOutput>>(
                Throw(
                    New(
                        typeof(InvalidOperationException).GetConstructor([typeof(string)])!,
                        Constant($"Type {typeof(TConcreteOutput)} cannot be instantiated as it does not have a public parameterless constructor.")),
                    typeof(TOutput)),
                input);
        }

        expressions.AddRange(
        [
            Assign(lengthVariable, getInputLength),

            Assign(output, instantiateOutput)
        ]);
        
        if (indexer is not null)
        {
            var counter = Parameter(typeof(int), "i");

            expressions.Add(
                ForLoop(
                    loopVar: counter,
                    initValue: Constant(0),
                    condition: LessThan(counter, lengthVariable),
                    increment: AddAssign(counter, Constant(1)),
                    loopContent:
                    typeof(TConcreteOutput).IsArray
                        ? Assign(
                            ArrayAccess(output, counter),
                            elementConversionExpression is null
                                ? indexer(counter)
                                : Invoke(elementConversionExpression, indexer(counter)))
                        : Call(
                            output,
                            typeof(TConcreteOutput).GetMethod("Add", [outputElementType])!,
                            elementConversionExpression is null
                                ? indexer(counter)
                                : Invoke(elementConversionExpression, indexer(counter)))));
        }
        else
        {
            var enumerableType = typeof(IEnumerable<>).MakeGenericType(inputElementType);
            var enumeratorType = typeof(IEnumerator<>).MakeGenericType(inputElementType);

            var enumeratorVariable = Variable(enumeratorType, "enumerator");
            var counterVariable = Variable(typeof(int), "variable");
            variables.AddRange([enumeratorVariable, counterVariable]);

            expressions.AddRange(
            [
                // enumerator = input.GetEnumerator();
                Assign(enumeratorVariable, Call(input, enumerableType.GetMethod(nameof(IEnumerable<object>.GetEnumerator))!)),

                // counter = 0;
                Assign(counterVariable, Constant(0))
            ]);

            var breakLabel = Label("LoopBreak");

            var loop =
                Loop(
                    IfThenElse(
                        Equal(Call(enumeratorVariable, typeof(IEnumerator).GetMethod(nameof(IEnumerator.MoveNext))!), Constant(true)),
                        Block(
                            typeof(TConcreteOutput).IsArray
                                // output[counter] = enumerator.Current;
                                ? Assign(
                                    ArrayAccess(output, counterVariable),
                                    elementConversionExpression is null
                                        ? Property(enumeratorVariable, "Current")
                                        : Invoke(elementConversionExpression, Property(enumeratorVariable, "Current")))
                                // output.Add(enumerator.Current);
                                : Call(
                                    output,
                                    typeof(TConcreteOutput).GetMethod("Add", [outputElementType])!,
                                    elementConversionExpression is null
                                        ? Property(enumeratorVariable, "Current")
                                        : Invoke(elementConversionExpression, Property(enumeratorVariable, "Current"))),

                            // counter++;
                            AddAssign(counterVariable, Constant(1))),
                        Break(breakLabel)),
                    breakLabel);

            expressions.Add(
                TryFinally(
                    loop,
                    Call(enumeratorVariable, typeof(IDisposable).GetMethod(nameof(IDisposable.Dispose))!)));
        }

        // return output;
        expressions.Add(output);

        Expression body = Block(typeof(TOutput), variables, expressions);

        if (!typeof(TInput).IsValueType)
        {
            body = Condition(
                ReferenceEqual(input, Constant(null, typeof(TInput))),
                typeof(TOutput).IsValueType
                    ? New(typeof(TConcreteOutput))
                    : Constant(null, typeof(TOutput)),
                body);
        }

        return Lambda<Func<TInput, TOutput>>(body, input);
    }

    private static Expression ForLoop(
        ParameterExpression loopVar,
        Expression initValue,
        Expression condition,
        Expression increment,
        Expression loopContent)
    {
        var initAssign = Assign(loopVar, initValue);
        var breakLabel = Label("LoopBreak");
        var loop = Block(
            [loopVar],
            initAssign,
            Loop(
                IfThenElse(
                    condition,
                    Block(
                        loopContent,
                        increment
                    ),
                    Break(breakLabel)
                ),
                breakLabel)
        );

        return loop;
    }
}
