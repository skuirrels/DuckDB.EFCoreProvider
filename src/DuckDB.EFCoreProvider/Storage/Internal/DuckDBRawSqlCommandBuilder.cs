using DuckDB.EFCoreProvider.Extensions.Internal;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Internal;
using System.Data.Common;

namespace DuckDB.EFCoreProvider.Storage.Internal;

/// <summary>
///     Builds raw SQL commands with DuckDB-compatible parameter names, including untyped null parameters.
/// </summary>
internal sealed class DuckDBRawSqlCommandBuilder : RawSqlCommandBuilder
{
    private readonly IRelationalCommandBuilderFactory _relationalCommandBuilderFactory;
    private readonly ISqlGenerationHelper _sqlGenerationHelper;
    private readonly IParameterNameGeneratorFactory _parameterNameGeneratorFactory;
    private readonly IRelationalTypeMappingSource _typeMappingSource;

    public DuckDBRawSqlCommandBuilder(
        IRelationalCommandBuilderFactory relationalCommandBuilderFactory,
        ISqlGenerationHelper sqlGenerationHelper,
        IParameterNameGeneratorFactory parameterNameGeneratorFactory,
        IRelationalTypeMappingSource typeMappingSource)
        : base(relationalCommandBuilderFactory, sqlGenerationHelper, parameterNameGeneratorFactory, typeMappingSource)
    {
        _relationalCommandBuilderFactory = relationalCommandBuilderFactory;
        _sqlGenerationHelper = sqlGenerationHelper;
        _parameterNameGeneratorFactory = parameterNameGeneratorFactory;
        _typeMappingSource = typeMappingSource;
    }

    public override RawSqlCommand Build(string sql, IEnumerable<object?> parameters, IModel? model)
    {
        var commandBuilder = _relationalCommandBuilderFactory.Create();
        var substitutions = new List<string>();
        var nameGenerator = _parameterNameGeneratorFactory.Create();
        var parameterValues = new Dictionary<string, object?>();

        foreach (var parameter in parameters)
        {
            if (parameter is DbParameter dbParameter)
            {
                if (string.IsNullOrEmpty(dbParameter.ParameterName))
                {
                    dbParameter.ParameterName = _sqlGenerationHelper.GenerateParameterName(nameGenerator.GenerateNext());
                }

                if (dbParameter is DuckDBParameter duckDBParameter)
                {
                    duckDBParameter.RemoveDollarSign();
                }

                substitutions.Add(_sqlGenerationHelper.GenerateParameterName(dbParameter.ParameterName));
                commandBuilder.AddRawParameter(dbParameter.ParameterName, dbParameter);
                continue;
            }

            var invariantName = nameGenerator.GenerateNext();
            var parameterName = _sqlGenerationHelper.GenerateParameterName(invariantName);
            substitutions.Add(parameterName);

            var typeMapping = parameter is null or DBNull
                ? DuckDBNullTypeMapping.Default
                : model is null
                    ? _typeMappingSource.GetMapping(parameter.GetType())
                    : _typeMappingSource.GetMapping(parameter.GetType(), model);

            commandBuilder.AddParameter(invariantName, parameterName, typeMapping, nullable: true);
            parameterValues.Add(invariantName, parameter);
        }

        var commandText = string.Format(sql, substitutions.Cast<object>().ToArray());
        return new RawSqlCommand(commandBuilder.Append(commandText).Build(), parameterValues!);
    }
}