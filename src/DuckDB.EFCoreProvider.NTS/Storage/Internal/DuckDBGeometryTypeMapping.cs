using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using DuckDB.EFCoreProvider.NTS.Storage.Json;
using DuckDB.EFCoreProvider.NTS.Storage.ValueConversion.Internal;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace DuckDB.EFCoreProvider.NTS.Storage.Internal;

/// <summary>
/// Type mapping for NTS geometry types in DuckDB.
/// <para>
/// DuckDB.NET cannot read the native GEOMETRY column type (type ID 40).
/// To work around this, geometries are stored as WKT strings in a <c>VARCHAR</c> column.
/// When a geometry column or parameter is used in a spatial function, the translators
/// wrap it with <c>ST_GeomFromText()</c> so DuckDB receives the correct GEOMETRY argument.
/// </para>
/// </summary>
public class DuckDBGeometryTypeMapping<TGeometry> : RelationalGeometryTypeMapping<TGeometry, string>, IDuckDBGeometryTypeMapping
    where TGeometry : Geometry
{
    private static readonly MethodInfo GetStringMethod
        = typeof(DbDataReader).GetRuntimeMethod(nameof(DbDataReader.GetString), [typeof(int)])!;

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public DuckDBGeometryTypeMapping(NtsGeometryServices geometryServices, string storeType)
        : base(
            new GeometryValueConverter<TGeometry>(CreateReader(geometryServices)),
            storeType,
            DuckDBJsonGeometryWktReaderWriter.Instance)
    {
    }

    protected DuckDBGeometryTypeMapping(
        RelationalTypeMappingParameters parameters,
        ValueConverter<TGeometry, string>? converter)
        : base(parameters, converter)
    {
    }

    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new DuckDBGeometryTypeMapping<TGeometry>(parameters, SpatialConverter);

    /// <summary>
    /// SQL literal for inline constants: <c>ST_GeomFromText('WKT'[, SRID])</c>.
    /// </summary>
    protected override string GenerateNonNullSqlLiteral(object value)
    {
        var geometry = (Geometry)value;
        var builder = new StringBuilder("ST_GeomFromText('");
        builder.Append(geometry.AsText()).Append('\'');
        if (geometry.SRID != 0)
            builder.Append(", ").Append(geometry.SRID);
        builder.Append(')');
        return builder.ToString();
    }

    /// <summary>
    /// DuckDB VARCHAR columns are read as plain strings via <see cref="DbDataReader.GetString"/>.
    /// </summary>
    public override MethodInfo GetDataReaderMethod() => GetStringMethod;

    /// <summary>
    /// Converts the WKT <see cref="string"/> returned by <c>GetString(i)</c>
    /// into <typeparamref name="TGeometry"/> using the spatial value converter.
    /// </summary>
    public override Expression CustomizeDataReaderExpression(Expression expression)
    {
        // expression is already of type string (WKT text from reader.GetString(ordinal))
        if (SpatialConverter == null)
            return expression;

        return ReplacingExpressionVisitor.Replace(
            SpatialConverter.ConvertFromProviderExpression.Parameters.Single(),
            expression,
            SpatialConverter.ConvertFromProviderExpression.Body);
    }

    protected override void ConfigureParameter(DbParameter parameter)
    {
        // DuckDB uses $name in SQL; the DuckDBParameter object must be registered without the '$'
        if (parameter is DuckDBParameter duckParam && duckParam.ParameterName.StartsWith('$'))
            duckParam.ParameterName = duckParam.ParameterName[1..];

        parameter.DbType = System.Data.DbType.String;
        base.ConfigureParameter(parameter);
    }

    protected override string AsText(object value)
        => ((Geometry)value).AsText();

    protected override int GetSrid(object value)
        => ((Geometry)value).SRID;

    protected override Type WktReaderType
        => typeof(WKTReader);

    private static WKTReader CreateReader(NtsGeometryServices geometryServices)
        => new(geometryServices);
}