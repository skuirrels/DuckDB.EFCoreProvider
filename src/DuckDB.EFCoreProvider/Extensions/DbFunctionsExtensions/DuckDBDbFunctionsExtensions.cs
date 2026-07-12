using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Runtime.CompilerServices;

namespace DuckDB.EFCoreProvider.Extensions.DbFunctionsExtensions;

/// <summary>
///     Provides DuckDB-specific extension methods for <see cref="DbFunctions" />.
/// </summary>
public static class DuckDBDbFunctionsExtensions
{
    /// <summary>Returns the one-based field from a delimited string using DuckDB's <c>split_part</c>.</summary>
    public static string SplitPart(this DbFunctions _, string value, string separator, int index)
        => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(SplitPart)));

    /// <summary>Computes the sample standard deviation of integer values.</summary>
    public static double? StandardDeviationSample(this DbFunctions _, IEnumerable<int> values)
        => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(StandardDeviationSample)));

    /// <summary>Computes the sample standard deviation of long integer values.</summary>
    public static double? StandardDeviationSample(this DbFunctions _, IEnumerable<long> values)
        => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(StandardDeviationSample)));

    /// <summary>Computes the sample standard deviation of single-precision values.</summary>
    public static double? StandardDeviationSample(this DbFunctions _, IEnumerable<float> values)
        => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(StandardDeviationSample)));

    /// <summary>Computes the sample standard deviation of double-precision values.</summary>
    public static double? StandardDeviationSample(this DbFunctions _, IEnumerable<double> values)
        => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(StandardDeviationSample)));

    /// <summary>Computes the sample standard deviation of decimal values.</summary>
    public static double? StandardDeviationSample(this DbFunctions _, IEnumerable<decimal> values)
        => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(StandardDeviationSample)));

    /// <summary>
    ///     Returns the value associated with the greatest ordering key. DuckDB chooses one value when keys tie;
    ///     callers requiring deterministic ties should include a unique tie-breaker in the ordering key.
    /// </summary>
    public static TValue? ArgMax<TValue, TOrder>(
        this DbFunctions _,
        IEnumerable<(TValue Value, TOrder Order)> values)
        => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(ArgMax)));

    /// <summary>
    ///     Returns the value associated with the least ordering key. DuckDB chooses one value when keys tie;
    ///     callers requiring deterministic ties should include a unique tie-breaker in the ordering key.
    /// </summary>
    public static TValue? ArgMin<TValue, TOrder>(
        this DbFunctions _,
        IEnumerable<(TValue Value, TOrder Order)> values)
        => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(ArgMin)));

    /// <summary>
    ///     Returns whether the row value represented by <paramref name="a" /> is greater than the row value represented by <paramref name="b" />.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
    /// <param name="a">The first row value.</param>
    /// <param name="b">The second row value.</param>
    /// <returns>The result of the row-value comparison.</returns>
    /// <exception cref="InvalidOperationException">Thrown if called directly; this method is only intended for translation inside a LINQ query.</exception>
    public static bool GreaterThan(this DbFunctions _, ITuple a, ITuple b)
        => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(GreaterThan)));

    /// <summary>
    ///     Returns whether the row value represented by <paramref name="a" /> is less than the row value represented by <paramref name="b" />.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
    /// <param name="a">The first row value.</param>
    /// <param name="b">The second row value.</param>
    /// <returns>The result of the row-value comparison.</returns>
    /// <exception cref="InvalidOperationException">Thrown if called directly; this method is only intended for translation inside a LINQ query.</exception>
    public static bool LessThan(this DbFunctions _, ITuple a, ITuple b)
        => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(LessThan)));

    /// <summary>
    ///     Returns whether the row value represented by <paramref name="a" /> is greater than or equal to the row value represented by <paramref name="b" />.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
    /// <param name="a">The first row value.</param>
    /// <param name="b">The second row value.</param>
    /// <returns>The result of the row-value comparison.</returns>
    /// <exception cref="InvalidOperationException">Thrown if called directly; this method is only intended for translation inside a LINQ query.</exception>
    public static bool GreaterThanOrEqual(this DbFunctions _, ITuple a, ITuple b)
        => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(GreaterThanOrEqual)));

    /// <summary>
    ///    Returns whether the row value represented by <paramref name="a" /> is less than or equal to the row value represented by <paramref name="b" />.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
    /// <param name="a">The first row value.</param>
    /// <param name="b">The second row value.</param>
    /// <returns>The result of the row-value comparison.</returns>
    /// <exception cref="InvalidOperationException">Thrown if called directly; this method is only intended for translation inside a LINQ query.</exception>
    public static bool LessThanOrEqual(this DbFunctions _, ITuple a, ITuple b)
        => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(LessThanOrEqual)));
}