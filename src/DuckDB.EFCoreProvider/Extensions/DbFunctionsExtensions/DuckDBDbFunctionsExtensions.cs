using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Runtime.CompilerServices;

namespace DuckDB.EFCoreProvider.Extensions.DbFunctionsExtensions;

/// <summary>
///     Provides DuckDB-specific extension methods for <see cref="DbFunctions" />.
/// </summary>
public static class DuckDBDbFunctionsExtensions
{
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
