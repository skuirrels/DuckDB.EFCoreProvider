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
    ///     Returns 1 when <paramref name="keyMember" /> is aggregated away in the current group of a
    ///     <c>GroupByRollup</c>, <c>GroupByCube</c> or <c>GroupByGroupingSets</c> query, and 0 when the group is
    ///     keyed by it. Translates to DuckDB's <c>GROUPING</c>; use it to tell a subtotal from a NULL key value.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
    /// <param name="keyMember">A member of the grouping key, for example <c>g.Key.Region</c>.</param>
    /// <returns>1 for a subtotal over <paramref name="keyMember" />; otherwise 0.</returns>
    public static int Grouping<T>(this DbFunctions _, T keyMember)
        => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(Grouping)));

    /// <summary>
    ///     Returns a bit mask of <c>GROUPING</c> flags for two key members: bit 1 for <paramref name="first" /> and
    ///     bit 0 for <paramref name="second" />. Translates to DuckDB's <c>GROUPING(first, second)</c>.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
    /// <param name="first">The most significant key member.</param>
    /// <param name="second">The least significant key member.</param>
    /// <returns>A value from 0 (grouped by both) to 3 (grouped by neither).</returns>
    public static int Grouping<T1, T2>(this DbFunctions _, T1 first, T2 second)
        => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(Grouping)));

    /// <summary>
    ///     Returns a bit mask of <c>GROUPING</c> flags for three key members, with <paramref name="first" /> as the
    ///     most significant bit. Translates to DuckDB's <c>GROUPING(first, second, third)</c>.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
    /// <param name="first">The most significant key member.</param>
    /// <param name="second">The middle key member.</param>
    /// <param name="third">The least significant key member.</param>
    /// <returns>A value from 0 (grouped by all three) to 7 (grouped by none).</returns>
    public static int Grouping<T1, T2, T3>(this DbFunctions _, T1 first, T2 second, T3 third)
        => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(Grouping)));

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