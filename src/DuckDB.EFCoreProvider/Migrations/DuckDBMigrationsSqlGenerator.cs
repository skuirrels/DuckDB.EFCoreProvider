using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Metadata.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace DuckDB.EFCoreProvider.Migrations;

/// <summary>
///     DuckDB-specific implementation of <see cref="MigrationsSqlGenerator" />.
/// </summary>
/// <remarks>
///     <para>
///         The service lifetime is <see cref="ServiceLifetime.Scoped" />. This means that each
///         <see cref="DbContext" /> instance will use its own instance of this service.
///         The implementation may depend on other services registered with any lifetime.
///         The implementation does not need to be thread-safe.
///     </para>
///     <para>
///         See <see href="https://aka.ms/efcore-docs-migrations">Database migrations</see>.
///     </para>
/// </remarks>
public class DuckDBMigrationsSqlGenerator : MigrationsSqlGenerator
{
    /// <summary>
    ///     Creates a new instance of <see cref="DuckDBMigrationsSqlGenerator" />.
    /// </summary>
    /// <param name="dependencies">Parameter object containing dependencies for this service.</param>
    public DuckDBMigrationsSqlGenerator(MigrationsSqlGeneratorDependencies dependencies) : base(dependencies)
    {
    }

    /// <summary>
    ///     Ignored, DuckDB does not support adding foreign keys.
    /// </summary>
    /// <param name="operation">The operation.</param>
    /// <param name="model">The target model which may be <see langword="null" /> if the operations exist without a model.</param>
    /// <param name="builder">The command builder to use to build the commands.</param>
    /// <param name="terminate">Indicates whether or not to terminate the command after generating SQL for the operation.</param>
    protected override void Generate(
        AddForeignKeyOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
    }

    /// <summary>
    ///     Ignored, DuckDB does not support dropping foreign keys.
    /// </summary>
    /// <param name="operation">The operation.</param>
    /// <param name="model">The target model which may be <see langword="null" /> if the operations exist without a model.</param>
    /// <param name="builder">The command builder to use to build the commands.</param>
    /// <param name="terminate">Indicates whether or not to terminate the command after generating SQL for the operation.</param>
    protected override void Generate(
        DropForeignKeyOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
    }

    /// <summary>
    ///     Ignored due to functional tests issue.
    /// </summary>
    /// <param name="operation">The operation.</param>
    /// <param name="model">The target model which may be <see langword="null" /> if the operations exist without a model.</param>
    /// <param name="builder">The command builder to use to build the commands.</param>
    protected override void CreateTableForeignKeys(CreateTableOperation operation, IModel? model, MigrationCommandListBuilder builder)
    {
    }

    /// <summary>
    ///     Builds commands for the given <see cref="RenameTableOperation" />
    ///     by making calls on the given <see cref="MigrationCommandListBuilder" />.
    /// </summary>
    /// <param name="operation">The operation.</param>
    /// <param name="model">The target model which may be <see langword="null" /> if the operations exist without a model.</param>
    /// <param name="builder">The command builder to use to build the commands.</param>
    protected override void Generate(RenameTableOperation operation, IModel? model, MigrationCommandListBuilder builder)
    {
        if (operation.NewSchema != null && operation.Schema != operation.NewSchema)
        {
            builder.Append("ALTER TABLE ").AppendLine(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name, operation.Schema))
                .Append(" SET SCHEMA ").Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.NewSchema))
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder, true);
        }

        if (operation.NewName != null && operation.Name != operation.NewName)
        {
            builder.Append("ALTER TABLE ").AppendLine(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name, operation.Schema))
                .Append(" RENAME TO ").Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.NewName, operation.NewSchema));
            EndStatement(builder, true);
        }
    }

    /// <summary>
    ///     Builds commands for the given <see cref="DropIndexOperation" />
    ///     by making calls on the given <see cref="MigrationCommandListBuilder" />.
    /// </summary>
    /// <param name="operation">The operation.</param>
    /// <param name="model">The target model which may be <see langword="null" /> if the operations exist without a model.</param>
    /// <param name="builder">The command builder to use to build the commands.</param>
    /// <param name="terminate">Indicates whether or not to terminate the command after generating SQL for the operation.</param>
    protected override void Generate(DropIndexOperation operation, IModel? model, MigrationCommandListBuilder builder, bool terminate = true)
    {
        builder.Append("DROP INDEX ").Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name));

        if (terminate)
        {
            EndStatement(builder);
        }
    }

    /// <inheritdoc />
    protected override void Generate(CreateSequenceOperation operation, IModel? model, MigrationCommandListBuilder builder)
    {
        builder
            .Append("CREATE SEQUENCE IF NOT EXISTS ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name, operation.Schema));

        var typeMapping = Dependencies.TypeMappingSource.FindMapping(operation.ClrType)
            ?? throw new InvalidOperationException($"No type mapping found for sequence CLR type '{operation.ClrType}'.");

        builder
            .Append(" START WITH ")
            .Append(typeMapping.GenerateSqlLiteral(operation.StartValue));

        SequenceOptions(operation, model, builder);

        builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

        EndStatement(builder, true);
    }

    /// <inheritdoc />
    protected override void Generate(EnsureSchemaOperation operation, IModel? model, MigrationCommandListBuilder builder)
    {
        builder.Append("CREATE SCHEMA IF NOT EXISTS ").Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name));
        EndStatement(builder);
    }

    /// <inheritdoc />
    protected override void Generate(
        CreateTableOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
        foreach (var column in operation.Columns)
        {
            ConfigureAutoIncrementColumn(operation.Name, operation.Schema, column, model, builder);
        }

        base.Generate(operation, model, builder, terminate);

        if (!string.IsNullOrEmpty(operation.Comment))
        {
            Comment(builder, "TABLE", Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name, operation.Schema), operation.Comment);
        }

        foreach (var addColumnOperation in operation.Columns.Where(c => c.Comment != null))
        {
            Comment(
                builder,
                "COLUMN",
                Dependencies.SqlGenerationHelper.DelimitIdentifier(addColumnOperation.Name, operation.Name),
                addColumnOperation.Comment);
        }
    }

    /// <inheritdoc />
    protected override void Generate(AlterTableOperation operation, IModel? model, MigrationCommandListBuilder builder)
    {
        base.Generate(operation, model, builder);

        if (operation.OldTable.Comment != operation.Comment)
        {
            Comment(
                builder,
                "TABLE",
                Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name, operation.Schema),
                operation.Comment);
        }
    }

    /// <inheritdoc />
    protected override void Generate(AlterColumnOperation operation, IModel? model, MigrationCommandListBuilder builder)
    {
        if (operation.OldColumn.ColumnType != operation.ColumnType)
        {
            builder.Append("ALTER TABLE ").AppendLine(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema))
                .Append("ALTER ").Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
                .Append(" TYPE ").Append(RequireColumnType(operation))
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder);
        }

        if (operation.OldColumn.IsNullable && !operation.IsNullable)
        {
            builder.Append("ALTER TABLE ").AppendLine(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema))
                .Append("ALTER ").Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
                .Append(" SET NOT NULL")
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder);
        }

        if (!operation.OldColumn.IsNullable && operation.IsNullable)
        {
            builder.Append("ALTER TABLE ").AppendLine(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema))
                .Append("ALTER ").Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
                .Append(" DROP NOT NULL")
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder);
        }

        if (operation.OldColumn.ComputedColumnSql != operation.ComputedColumnSql)
        {
            Generate(new DropColumnOperation
            {
                Name = operation.Name,
                Schema = operation.Schema,
                Table = operation.Table,
            }, model, builder);

            Generate(
                new AddColumnOperation
                {
                    Name = operation.Name,
                    Schema = operation.Schema,
                    Table = operation.Table,
                    ColumnType = operation.ColumnType,
                    IsNullable = operation.IsNullable,
                    ComputedColumnSql = operation.ComputedColumnSql,
                    IsStored = operation.IsStored,
                    DefaultValueSql = operation.DefaultValueSql,
                    Comment = operation.Comment,
                    Collation = operation.Collation,
                    ClrType = operation.ClrType,
                    DefaultValue = operation.DefaultValue,
                    IsDestructiveChange = operation.IsDestructiveChange,
                    IsFixedLength = operation.IsFixedLength,
                    IsRowVersion = operation.IsRowVersion,
                    IsUnicode = operation.IsUnicode,
                    MaxLength = operation.MaxLength,
                    Precision = operation.Precision,
                    Scale = operation.Scale
                },
                model,
                builder);
        }

        if (operation.OldColumn.Name != operation.Name ||
            operation.OldColumn.Schema != operation.Schema ||
            operation.OldColumn.Table != operation.Table ||
            operation.OldColumn.ColumnType != operation.ColumnType ||
            operation.OldColumn.IsUnicode != operation.IsUnicode ||
            operation.OldColumn.IsFixedLength != operation.IsFixedLength ||
            operation.OldColumn.MaxLength != operation.MaxLength ||
            operation.OldColumn.Precision != operation.Precision ||
            operation.OldColumn.Scale != operation.Scale ||
            operation.OldColumn.IsRowVersion != operation.IsRowVersion ||
            operation.OldColumn.IsNullable != operation.IsNullable ||
            operation.OldColumn.DefaultValue != operation.DefaultValue ||
            operation.OldColumn.DefaultValueSql != operation.DefaultValueSql ||
            operation.OldColumn.ComputedColumnSql != operation.ComputedColumnSql ||
            operation.OldColumn.IsStored != operation.IsStored ||
            operation.OldColumn.Collation != operation.Collation)
        {
            // base.Generate(operation, model, builder);
        }

        if (operation.OldColumn.Comment != operation.Comment)
        {
            Comment(builder, "COLUMN", Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name, operation.Table), operation.Comment);
        }
    }

    /// <inheritdoc />
    protected override void Generate(AddColumnOperation operation, IModel? model, MigrationCommandListBuilder builder, bool terminate = true)
    {
        ConfigureAutoIncrementColumn(operation.Table, operation.Schema, operation, model, builder);

        base.Generate(operation, model, builder, terminate);

        if (operation.Comment != null)
        {
            Comment(builder, "COLUMN", Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name, operation.Table), operation.Comment);
        }
    }

    /// <inheritdoc />
    protected override void ComputedColumnDefinition(
        string? schema,
        string table,
        string name,
        ColumnOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder)
    {
        builder
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(name))
            .Append(" ")
            .Append(RequireColumnType(operation))
            .Append(" GENERATED ALWAYS AS (")
            .Append(operation.ComputedColumnSql
                    ?? throw new InvalidOperationException($"Computed column SQL for column '{table}.{name}' is required."))
            .Append(")");

        if (operation.IsStored == true)
        {
            builder.Append(" STORED");
        }
    }

    /// <inheritdoc />
    protected override void Generate(RenameColumnOperation operation, IModel? model, MigrationCommandListBuilder builder)
    {
        builder.Append("ALTER TABLE ").AppendLine(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema))
            .Append("RENAME ").Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
            .Append(" TO ").Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.NewName))
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        EndStatement(builder);
    }

    /// <summary>
    /// Appends a SQL command to add a comment on a specified database object.
    /// </summary>
    /// <param name="builder">The builder used to construct the list of migration commands.</param>
    /// <param name="objectType">The type of the object to comment on (e.g., "TABLE", "COLUMN").</param>
    /// <param name="objectName">The name of the object to comment on.</param>
    /// <param name="comment">The comment to be added to the object. Can be null.</param>
    protected virtual void Comment(
        MigrationCommandListBuilder builder,
        string objectType,
        string objectName,
        string? comment)
    {
        var stringTypeMapping = Dependencies.TypeMappingSource.FindMapping(typeof(string))!;

        builder.Append("COMMENT ON ").Append(objectType).Append(" ")
            .Append(objectName)
            .Append(" IS ")
            .AppendLine(stringTypeMapping.GenerateSqlLiteral(comment))
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

        EndStatement(builder);
    }

    private static string RequireColumnType(ColumnOperation operation)
        => operation.ColumnType
           ?? throw new InvalidOperationException($"Column type for column '{operation.Table}.{operation.Name}' is required.");

    private void ConfigureAutoIncrementColumn(
        string table,
        string? schema,
        ColumnOperation column,
        IModel? model,
        MigrationCommandListBuilder builder)
    {
        if (column[DuckDBAnnotationNames.ValueGenerationStrategy] is not DuckDBValueGenerationStrategy.AutoIncrement)
        {
            return;
        }

        var sequenceName = $"{table}_{column.Name}_seq";

        Generate(new CreateSequenceOperation
        {
            Name = sequenceName,
            Schema = schema,
            ClrType = column.ClrType
        }, model, builder);

        if (string.IsNullOrEmpty(column.DefaultValueSql) && column.DefaultValue == null)
        {
            column.DefaultValueSql =
                $"nextval({DuckDBSequenceNameHelper.GenerateSequenceNameLiteral(sequenceName, schema)})";
        }
    }

    /// <inheritdoc />
    protected override void Generate(DropSequenceOperation operation, IModel? model, MigrationCommandListBuilder builder)
    {
        builder
            .Append("DROP SEQUENCE IF EXISTS ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name, operation.Schema))
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

        EndStatement(builder);
    }

    protected override void Generate(RenameSequenceOperation operation, IModel? model, MigrationCommandListBuilder builder)
    {
        if (operation.NewSchema != null && operation.Schema != operation.NewSchema)
        {
            builder.Append("ALTER SEQUENCE ").AppendLine(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name, operation.Schema))
                .Append(" SET SCHEMA ").Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.NewSchema))
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder, true);
        }

        if (operation.NewName != null && operation.Name != operation.NewName)
        {
            builder.Append("ALTER SEQUENCE ").AppendLine(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name, operation.Schema))
                .Append(" RENAME TO ").Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.NewName, operation.NewSchema))
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder, true);
        }
    }
}
