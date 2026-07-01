using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore.Storage;
using System.Text;

namespace DuckDB.EFCoreProvider.Storage.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBSqlGenerationHelper : RelationalSqlGenerationHelper
{
    // DuckDB's reserved keywords and function names are read from the engine itself (so the set stays correct
    // across DuckDB versions). It is loaded lazily on first identifier delimiting rather than in a static
    // constructor, so the database I/O is deferred and a failure to load degrades gracefully instead of
    // surfacing as a TypeInitializationException.
    private static readonly Lazy<IReadOnlySet<string>> ReservedWords = new(LoadReservedWords);

    private static IReadOnlySet<string> LoadReservedWords()
    {
        var reservedWords = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);

        try
        {
            using var connection = new DuckDBConnection(DuckDBConnectionStringBuilder.InMemorySharedConnectionString);
            using var command = connection.CreateCommand();
            command.CommandText = """
                                  SELECT keyword_name FROM duckdb_keywords()
                                  UNION
                                  SELECT function_name FROM duckdb_functions()
                                  """;

            connection.Open();

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                reservedWords.Add(reader.GetString(0));
            }
        }
        catch (DuckDBException)
        {
            // If the keyword/function list cannot be loaded, fall back to quoting based purely on the
            // character rules in RequiresQuoting rather than failing. Identifiers that happen to collide with
            // a reserved word simply will not be force-quoted in this (unexpected) degraded state.
        }

        return reservedWords;
    }

    public DuckDBSqlGenerationHelper(RelationalSqlGenerationHelperDependencies dependencies) : base(dependencies)
    {
    }

    /// <inheritdoc />
    public override string GenerateParameterName(string name)
    {
        return name.StartsWith('$') ? name : '$' + name;
    }

    /// <inheritdoc />
    public override void GenerateParameterName(StringBuilder builder, string name)
    {
        builder.Append('$').Append(name);
    }

    /// <inheritdoc />
    public override string DelimitIdentifier(string identifier)
    {
        return RequiresQuoting(identifier) ? base.DelimitIdentifier(identifier) : identifier;
    }

    /// <inheritdoc />
    public override void DelimitIdentifier(StringBuilder builder, string identifier)
    {
        if (RequiresQuoting(identifier))
        {
            base.DelimitIdentifier(builder, identifier);
        }
        else
        {
            builder.Append(identifier);
        }
    }

    private static bool RequiresQuoting(string identifier)
    {
        var first = identifier[0];

        if (!char.IsLower(first) && first != '_')
        {
            return true;
        }

        for (var i = 1; i < identifier.Length; i++)
        {
            var c = identifier[i];

            if (char.IsLower(c) || char.IsDigit(c) || c == '_')
            {
                continue;
            }

            return true;
        }

        return ReservedWords.Value.Contains(identifier);
    }
}
