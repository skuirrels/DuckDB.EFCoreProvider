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

        return DuckDBIdentifierMetadata.ReservedIdentifiers.Contains(identifier);
    }
}