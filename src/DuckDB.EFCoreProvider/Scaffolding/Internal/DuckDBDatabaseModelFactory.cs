using DuckDB.EFCoreProvider.Infrastructure.Internal;
using DuckDB.EFCoreProvider.Storage.Internal;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Data.Common;
using System.Diagnostics;

namespace DuckDB.EFCoreProvider.Scaffolding.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBDatabaseModelFactory : DatabaseModelFactory
{
    private readonly IDiagnosticsLogger<DbLoggerCategory.Scaffolding> _logger;

    public DuckDBDatabaseModelFactory(IDiagnosticsLogger<DbLoggerCategory.Scaffolding> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public override DatabaseModel Create(string connectionString, DatabaseModelFactoryOptions options)
    {
        if (connectionString.StartsWith("ducklake:", StringComparison.OrdinalIgnoreCase))
        {
            return CreateDuckLake(connectionString["ducklake:".Length..], options);
        }

        using var connection = new DuckDBConnection(connectionString);
        return Create(connection, options);
    }

    private DatabaseModel CreateDuckLake(string metadataPath, DatabaseModelFactoryOptions options)
    {
        DuckLakeMetadataSourceValidator.ValidateLocalPath(metadataPath, nameof(metadataPath));
        using var connection = new DuckDBConnection("Data Source=:memory:");
        connection.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSTALL ducklake; LOAD ducklake; "
                + DuckLakeAttachCommandBuilder.Build(new DuckLakeOptions
                {
                    MetadataSource = metadataPath,
                    IsReadOnly = true,
                    CreateIfNotExists = false
                });
            command.ExecuteNonQuery();
        }

        return Create(connection, options);
    }

    /// <inheritdoc />
    public override DatabaseModel Create(DbConnection connection, DatabaseModelFactoryOptions options)
    {
        var connectionStartedOpen = connection.State == ConnectionState.Open;

        if (!connectionStartedOpen)
        {
            connection.Open();
        }

        try
        {
            var databaseModel = new DatabaseModel
            {
                DatabaseName = GetCurrentDatabase((DuckDBConnection)connection),
                DefaultSchema = "main"
            };

            FillTables((DuckDBConnection)connection, databaseModel, options.Tables, options.Schemas);
            FillColumns((DuckDBConnection)connection, databaseModel);
            FillPrimaryKeys((DuckDBConnection)connection, databaseModel);
            FillIndexes((DuckDBConnection)connection, databaseModel);
            FillSequences((DuckDBConnection)connection, databaseModel);

            foreach (var table in databaseModel.Tables)
            {
                GetForeignKeys(connection, table, databaseModel.Tables);
            }

            var nullableKeyColumns = databaseModel.Tables
                .SelectMany(t => t.PrimaryKey?.Columns ?? [])
                .Concat(databaseModel.Tables.SelectMany(t => t.ForeignKeys).SelectMany(fk => fk.PrincipalColumns))
                .Where(c => c.IsNullable)
                .Distinct();

            foreach (var column in nullableKeyColumns)
            {
                // DuckDB can report key / principal columns as nullable, but a column participating in a
                // primary or foreign key is effectively non-nullable for the scaffolded model, so coerce it
                // and surface a warning so the divergence from the database metadata is visible.
                _logger.Logger.LogWarning(
                    "Column '{Column}' on table '{Table}' participates in a primary or foreign key but was reported as nullable by DuckDB; it is scaffolded as non-nullable.",
                    column.Name, column.Table.Name);

                column.IsNullable = false;
            }

            return databaseModel;
        }
        finally
        {
            if (!connectionStartedOpen)
            {
                connection.Close();
            }
        }
    }

    private static string GetCurrentDatabase(DuckDBConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT current_database();";
        return (string)command.ExecuteScalar()!;
    }

    private void FillTables(
        DuckDBConnection connection,
        DatabaseModel databaseModel,
        IEnumerable<string> tables,
        IEnumerable<string> schemas)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT *
                                FROM information_schema.tables
                               WHERE table_catalog = $database_name
                                 AND table_name != $default_table_name
                                 AND (array_length(CAST($schemas AS VARCHAR[])) = 0 OR table_schema = ANY(CAST($schemas AS VARCHAR[])))
                                 AND (array_length(CAST($tables AS VARCHAR[])) = 0
                                      OR table_name = ANY(CAST($tables AS VARCHAR[]))
                                      OR concat(table_schema, '.', table_name) = ANY(CAST($tables AS VARCHAR[])));
                              """;

        command.Parameters.Add(new DuckDBParameter("database_name", databaseModel.DatabaseName));

        var defaultTableNameParameter = (DuckDBParameter)command.CreateParameter();
        defaultTableNameParameter.ParameterName = "default_table_name";
        defaultTableNameParameter.Value = HistoryRepository.DefaultTableName;
        defaultTableNameParameter.DbType = DbType.String;

        var tablesParameter = (DuckDBParameter)command.CreateParameter();
        tablesParameter.ParameterName = "tables";
        tablesParameter.Value = tables.ToList();
        tablesParameter.DbType = DbType.Object;

        command.Parameters.Add(defaultTableNameParameter);
        command.Parameters.Add(tablesParameter);
        command.Parameters.Add(new DuckDBParameter("schemas", schemas.ToList()) { DbType = DbType.Object });

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.GetString("table_name");
            var schema = reader.GetString("table_schema");
            var tableComment = reader.IsDBNull("TABLE_COMMENT") ? null : reader.GetString("TABLE_COMMENT");

            var type = reader.GetString("table_type");
            var table = type == "BASE TABLE"
                ? new DatabaseTable { Database = databaseModel, Name = name, Schema = schema, Comment = tableComment }
                : new DatabaseView { Database = databaseModel, Name = name, Schema = schema, Comment = tableComment };

            databaseModel.Tables.Add(table);
        }
    }

    private void FillColumns(DuckDBConnection connection, DatabaseModel database)
    {
        foreach (var table in database.Tables)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                                  SELECT *
                                    FROM duckdb_columns
                                   WHERE database_name = $database_name
                                     AND table_name = $table_name
                                     AND schema_name = $table_schema
                                  """;

            command.Parameters.Add(new DuckDBParameter("database_name", database.DatabaseName));
            command.Parameters.Add(new DuckDBParameter("table_name", table.Name));
            command.Parameters.Add(new DuckDBParameter("table_schema", table.Schema));

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                var columnName = reader.GetString("column_name");
                var dataType = reader.GetString("data_type");
                var isNullable = reader.GetBoolean("is_nullable");
                var defaultValueSql = !reader.IsDBNull("column_default") ? reader.GetString("column_default") : null;
                var columnComment = reader.IsDBNull("comment") ? null : reader.GetString("comment");
                var characterMaximumLength = reader.IsDBNull("character_maximum_length") ? (int?)null : reader.GetInt32("character_maximum_length");
                var numericPrecision = reader.IsDBNull("numeric_precision") ? (int?)null : reader.GetInt32("numeric_precision");
                var numericScale = reader.IsDBNull("numeric_scale") ? (int?)null : reader.GetInt32("numeric_scale");

                var column = new DatabaseColumn
                {
                    Table = table,
                    Name = columnName,
                    StoreType = dataType,
                    IsNullable = isNullable,
                    DefaultValueSql = defaultValueSql,
                    ValueGenerated = !string.IsNullOrWhiteSpace(defaultValueSql) && defaultValueSql.StartsWith("nextval(") && defaultValueSql.EndsWith(")")
                        ? ValueGenerated.OnAdd
                        : null,
                    Comment = columnComment
                    // Computed columns are intentionally not reverse-engineered: DuckDB does not
                    // populate information_schema.columns.is_generated / generation_expression, and a
                    // generated column's expression is reported through column_default with no flag to
                    // distinguish it from a literal default. DuckDB also supports only VIRTUAL generated
                    // columns (never STORED), so ComputedColumnSql/IsStored cannot be resolved reliably.
                    // The expression is therefore surfaced as DefaultValueSql above (best effort).
                };

                if (characterMaximumLength.HasValue)
                {
                    column.SetAnnotation(CoreAnnotationNames.MaxLength, characterMaximumLength.Value);
                }

                if (numericPrecision.HasValue)
                {
                    column.SetAnnotation(CoreAnnotationNames.Precision, numericPrecision.Value);
                }

                if (numericScale.HasValue)
                {
                    column.SetAnnotation(CoreAnnotationNames.Scale, numericScale.Value);
                }

                table.Columns.Add(column);
            }
        }
    }

    private void FillPrimaryKeys(DuckDBConnection connection, DatabaseModel database)
    {
        foreach (DatabaseTable table in database.Tables)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                                  SELECT constraint_name
                                    FROM information_schema.table_constraints
                                   WHERE table_catalog = $database_name
                                     AND table_name = $table_name
                                     AND table_schema = $table_schema
                                     AND constraint_type = 'PRIMARY KEY';
                                  """;

            command.Parameters.Add(new DuckDBParameter("database_name", database.DatabaseName));
            command.Parameters.Add(new DuckDBParameter("table_name", table.Name));
            command.Parameters.Add(new DuckDBParameter("table_schema", table.Schema));

            var name = (string?)command.ExecuteScalar();

            if (name == null)
            {
                continue;
            }

            var primaryKey = new DatabasePrimaryKey
            {
                Table = table,
                Name = name
            };

            command.CommandText =
                """
                SELECT k.column_name
                  FROM information_schema.table_constraints t
                  JOIN information_schema.key_column_usage k
                    ON t.constraint_catalog = k.constraint_catalog
                   AND t.constraint_name = k.constraint_name
                   AND t.table_name = k.table_name
                   AND t.table_schema = k.table_schema
                 WHERE t.constraint_type = 'PRIMARY KEY'
                   AND t.table_catalog = $database_name
                   AND t.table_name = $table_name
                   AND t.table_schema = $schema
                 ORDER BY k.ordinal_position;
                """;

            command.Parameters.Clear();
            command.Parameters.Add(new DuckDBParameter("database_name", database.DatabaseName));
            command.Parameters.Add(new DuckDBParameter("table_name", table.Name));
            command.Parameters.Add(new DuckDBParameter("schema", table.Schema));

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var columnName = reader.GetString(0);
                var column = table.Columns.FirstOrDefault(c => c.Name == columnName)
                             ?? table.Columns.FirstOrDefault(c => c.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase));
                Debug.Assert(column != null, "column is null.");

                primaryKey.Columns.Add(column);
            }

            table.PrimaryKey = primaryKey;
        }
    }

    private void GetForeignKeys(DbConnection connection, DatabaseTable table, IList<DatabaseTable> tables)
    {
        var databaseName = table.Database?.DatabaseName
            ?? throw new InvalidOperationException("The scaffolded table is not associated with a database model.");
        using var command1 = connection.CreateCommand();
        command1.CommandText = """
                               SELECT tc.constraint_name as foreign_key_name,
                                     rc.delete_rule,
                                     pc.table_name AS referenced_table_name,
                                     pc.table_schema AS referenced_table_schema,
                                FROM information_schema.table_constraints AS tc
                                JOIN information_schema.referential_constraints AS rc
                                  ON tc.constraint_catalog = rc.constraint_catalog
                                 AND tc.constraint_name = rc.constraint_name
                                 AND tc.table_schema = rc.constraint_schema
                                JOIN information_schema.table_constraints pc
                                  ON pc.constraint_catalog = rc.unique_constraint_catalog
                                 AND pc.constraint_name = rc.unique_constraint_name
                                 AND pc.table_schema = rc.unique_constraint_schema
                               WHERE tc.constraint_type = 'FOREIGN KEY'
                                 AND tc.table_catalog = ?
                                 AND tc.table_name = ?
                                 AND tc.table_schema = ?;
                               """;

        command1.Parameters.Add(new DuckDBParameter(databaseName));
        command1.Parameters.Add(new DuckDBParameter(table.Name));
        command1.Parameters.Add(new DuckDBParameter(table.Schema ?? string.Empty));

        using var reader1 = command1.ExecuteReader();
        while (reader1.Read())
        {
            var constraintName = reader1.GetString("foreign_key_name");
            var principalTableName = reader1.GetString("referenced_table_name");
            var principalTableSchema = reader1.GetString("referenced_table_schema");
            var onDelete = reader1.GetString("delete_rule");
            var principalTable =
                tables.FirstOrDefault(t => t.Name == principalTableName && t.Schema == principalTableSchema)
                ?? tables.FirstOrDefault(t => t.Name.Equals(principalTableName, StringComparison.OrdinalIgnoreCase) &&
                                              StringComparer.OrdinalIgnoreCase.Equals(t.Schema, principalTableSchema));

            _logger.Logger.LogDebug(
                "Found foreign key '{ForeignKey}' on table '{Table}' referencing table '{PrincipalTable}' (on delete: {OnDelete}).",
                constraintName, table.Name, principalTableName, onDelete);

            if (principalTable == null)
            {
                _logger.Logger.LogWarning(
                    "Foreign key '{ForeignKey}' on table '{Table}' references unknown principal table '{PrincipalTable}' and was skipped.",
                    constraintName, table.Name, principalTableName);
                continue;
            }

            var foreignKey = new DatabaseForeignKey
            {
                Table = table,
                Name = constraintName,
                PrincipalTable = principalTable,
                OnDelete = ConvertToReferentialAction(onDelete)
            };

            using var command2 = connection.CreateCommand();
            command2.CommandText =
                """
SELECT child.column_name  AS child_column,
       parent.column_name  AS parent_column,
       child.ordinal_position
  FROM information_schema.referential_constraints AS rc
  JOIN information_schema.key_column_usage AS child
    ON rc.constraint_catalog = child.constraint_catalog
   AND rc.constraint_name = child.constraint_name
   AND rc.constraint_schema = child.table_schema
  JOIN information_schema.key_column_usage AS parent
    ON rc.unique_constraint_catalog = parent.constraint_catalog
   AND rc.unique_constraint_name = parent.constraint_name
   AND rc.unique_constraint_schema = parent.table_schema
   AND child.ordinal_position = parent.ordinal_position
 WHERE rc.constraint_catalog = ?
   AND rc.constraint_name = ?
   AND rc.constraint_schema = ?
 ORDER BY child.ordinal_position;
""";

            command2.Parameters.Add(new DuckDBParameter(databaseName));
            command2.Parameters.Add(new DuckDBParameter(foreignKey.Name));
            command2.Parameters.Add(new DuckDBParameter(table.Schema ?? string.Empty));

            var invalid = false;

            using (var reader2 = command2.ExecuteReader())
            {
                while (reader2.Read())
                {
                    var columnName = reader2.GetString("child_column");
                    var column = table.Columns.FirstOrDefault(c => c.Name == columnName)
                        ?? table.Columns.FirstOrDefault(
                            c => c.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase));
                    Debug.Assert(column != null, "column is null.");

                    var principalColumnName = reader2.GetString("parent_column");
                    DatabaseColumn? principalColumn = null;
                    if (principalColumnName != null)
                    {
                        principalColumn =
                            foreignKey.PrincipalTable.Columns.FirstOrDefault(c => c.Name == principalColumnName)
                            ?? foreignKey.PrincipalTable.Columns.FirstOrDefault(
                                c => c.Name.Equals(principalColumnName, StringComparison.OrdinalIgnoreCase));
                    }
                    else if (principalTable?.PrimaryKey != null)
                    {
                        var seq = reader2.GetInt32(0);
                        principalColumn = principalTable.PrimaryKey.Columns[seq];
                    }

                    if (principalColumn == null)
                    {
                        invalid = true;
                        _logger.Logger.LogWarning(
                            "Foreign key '{ForeignKey}' on table '{Table}' references principal column '{PrincipalColumn}' on table '{PrincipalTable}', which was not found. The foreign key was skipped.",
                            foreignKey.Name, table.Name, principalColumnName, principalTableName);
                        break;
                    }

                    foreignKey.Columns.Add(column);
                    foreignKey.PrincipalColumns.Add(principalColumn);
                }
            }

            if (!invalid)
            {
                table.ForeignKeys.Add(foreignKey);
            }
        }
    }

    private void FillIndexes(DuckDBConnection connection, DatabaseModel database)
    {
        foreach (var table in database.Tables)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                                  SELECT *
                                    FROM duckdb_indexes
                                    WHERE database_name = $database_name
                                      AND schema_name = $table_schema
                                      AND table_name = $table_name
                                  """;

            command.Parameters.Add(new DuckDBParameter("database_name", database.DatabaseName));
            command.Parameters.Add(new DuckDBParameter("table_schema", table.Schema));
            command.Parameters.Add(new DuckDBParameter("table_name", table.Name));

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                var index = new DatabaseIndex
                {
                    Name = reader.GetString("index_name"),
                    Table = table,
                    IsUnique = reader.GetBoolean("is_unique")
                };

                var expressions = reader.GetFieldValue<string>("expressions");
                var columns = Array.ConvertAll(expressions.TrimStart('[').TrimEnd(']').Split(','), e => e.Trim().Trim(['\'', '\"']));

                foreach (var column in columns)
                {
                    var tableColumn = table.Columns.Single(c => c.Name == column);

                    index.Columns.Add(tableColumn);

                    // DuckDB does not retain a per-column sort direction for indexes (a persisted
                    // "CREATE INDEX ... (col DESC)" reads back without the DESC), so every scaffolded column
                    // is reported as ascending.
                    index.IsDescending.Add(false);
                }

                table.Indexes.Add(index);
            }
        }
    }

    private void FillSequences(DuckDBConnection connection, DatabaseModel database)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM duckdb_sequences() WHERE database_name = $database_name";
        command.Parameters.Add(new DuckDBParameter("database_name", database.DatabaseName));
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var sequence = new DatabaseSequence
            {
                Name = reader.GetString("sequence_name"),
                Schema = reader.GetString("schema_name"),
                Database = database,
                IncrementBy = reader.GetInt32("increment_by"),
                StartValue = reader.GetInt64("start_value"),
                MinValue = reader.GetInt64("min_value"),
                MaxValue = reader.GetInt64("max_value"),
                IsCyclic = reader.GetBoolean("cycle"),
                StoreType = "BIGINT"
            };

            database.Sequences.Add(sequence);
        }
    }

    private static ReferentialAction? ConvertToReferentialAction(string value)
        => value switch
        {
            "RESTRICT" => ReferentialAction.Restrict,
            "CASCADE" => ReferentialAction.Cascade,
            "SET NULL" => ReferentialAction.SetNull,
            "SET DEFAULT" => ReferentialAction.SetDefault,
            "NO ACTION" => ReferentialAction.NoAction,
            _ => null
        };
}