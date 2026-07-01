using DuckDB.EFCoreProvider.Extensions.Internal;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace DuckDB.EFCoreProvider.Storage.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBJsonTypeMapping : JsonTypeMapping
{
    public DuckDBJsonTypeMapping(Type clrType)
        : base("JSON", clrType, System.Data.DbType.String)
    {
    }

    protected DuckDBJsonTypeMapping(RelationalTypeMappingParameters parameters) : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
    {
        return new DuckDBJsonTypeMapping(parameters);
    }

    protected virtual string EscapeSqlLiteral(string literal)
    {
        return literal.Replace("'", "''");
    }

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value)
    {
        switch (value)
        {
            case JsonDocument _:
            case JsonElement _:
                {
                    using var stream = new MemoryStream();
                    using var writer = new Utf8JsonWriter(stream);
                    if (value is JsonDocument doc)
                    {
                        doc.WriteTo(writer);
                    }
                    else
                    {
                        ((JsonElement)value).WriteTo(writer);
                    }

                    writer.Flush();
                    return $"'{EscapeSqlLiteral(Encoding.UTF8.GetString(stream.ToArray()))}'";
                }
            case string s:
                return $"'{EscapeSqlLiteral(s)}'";
            default:
                return $"'{EscapeSqlLiteral(JsonSerializer.Serialize(value))}'";
        }
    }

    /// <inheritdoc />
    public override Expression GenerateCodeLiteral(object value)
        => value switch
        {
            JsonDocument document => Expression.Call(
                ParseMethod, Expression.Constant(document.RootElement.ToString()), DefaultJsonDocumentOptions),
            JsonElement element => Expression.Property(
                Expression.Call(ParseMethod, Expression.Constant(element.ToString()), DefaultJsonDocumentOptions),
                nameof(JsonDocument.RootElement)),
            string s => Expression.Constant(s),
            _ => throw new NotSupportedException("Cannot generate code literals for JSON POCOs")
        };

    private static readonly Expression DefaultJsonDocumentOptions = Expression.New(typeof(JsonDocumentOptions));

    private static readonly MethodInfo ParseMethod =
        typeof(JsonDocument).GetMethod(nameof(JsonDocument.Parse), [typeof(string), typeof(JsonDocumentOptions)])!;

    /// <inheritdoc />
    public override MethodInfo GetDataReaderMethod()
    {
        return GetDataReaderMethod(typeof(string));
    }

    /// <inheritdoc />
    protected override void ConfigureParameter(DbParameter parameter)
    {
        ((DuckDBParameter)parameter).RemoveDollarSign();
        base.ConfigureParameter(parameter);
    }
}
