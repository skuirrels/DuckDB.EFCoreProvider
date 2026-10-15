using DuckDB.EFCoreProvider.Design.Internal;
using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using DuckDB.EFCoreProvider.Internal;
using DuckDB.EFCoreProvider.Migrations;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
///     Covers the capabilities that depend on the loaded native engine generation: the capability matrix for
///     DuckDB 1.5 and 2.0, the SQL each capability selects, and end-to-end behaviour on whichever engine is loaded.
/// </summary>
public sealed class EngineVersionCapabilityTests : DuckDBTestBase
{
    private static readonly Version DuckDB15 = new(1, 5, 6);
    private static readonly Version DuckDB20 = new(2, 0, 0);

    [Theory]
    [InlineData("v1.5.6", 1, 5, 6)]
    [InlineData("v2.0.0-0-g9a97642349", 2, 0, 0)]
    [InlineData("v2.1.0-dev1234", 2, 1, 0)]
    [InlineData("1.4.3", 1, 4, 3)]
    public void Native_library_versions_are_parsed(string libraryVersion, int major, int minor, int build)
        => Assert.Equal(new Version(major, minor, build), DuckDBNativeEngine.Parse(libraryVersion));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    public void Unreadable_library_versions_resolve_to_the_baseline(string? libraryVersion)
        => Assert.Equal(DuckDBNativeEngine.Baseline, DuckDBNativeEngine.Parse(libraryVersion));

    [Fact]
    public void DuckDB_1_5_keeps_the_baseline_schema_capabilities()
    {
        var capabilities = new DuckDBEngineCapabilities(isDuckLake: false, isQuack: false, DuckDB15);

        Assert.False(capabilities.SupportsAddColumnConstraints);
        Assert.False(capabilities.SupportsAddUniqueConstraint);
        Assert.False(capabilities.SupportsSequenceSetValue);
    }

    [Fact]
    public void DuckDB_2_0_enables_alter_table_and_sequence_capabilities_without_changing_the_rest()
    {
        var baseline = new DuckDBEngineCapabilities(isDuckLake: false, isQuack: false, DuckDB15);
        var capabilities = new DuckDBEngineCapabilities(isDuckLake: false, isQuack: false, DuckDB20);

        Assert.True(capabilities.SupportsAddColumnConstraints);
        Assert.True(capabilities.SupportsAddUniqueConstraint);
        Assert.True(capabilities.SupportsSequenceSetValue);
        Assert.Equal(baseline.SupportsReturning, capabilities.SupportsReturning);
        Assert.Equal(baseline.SupportsSequences, capabilities.SupportsSequences);
        Assert.Equal(baseline.SupportsSchemaConstraints, capabilities.SupportsSchemaConstraints);
        Assert.Equal(baseline.SupportsEfMigrations, capabilities.SupportsEfMigrations);
        Assert.Equal(baseline.UpsertStrategy, capabilities.UpsertStrategy);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void DuckLake_and_Quack_never_enable_engine_version_capabilities(bool isDuckLake, bool isQuack)
    {
        var capabilities = new DuckDBEngineCapabilities(isDuckLake, isQuack, DuckDB20);

        Assert.False(capabilities.SupportsAddColumnConstraints);
        Assert.False(capabilities.SupportsAddUniqueConstraint);
        Assert.False(capabilities.SupportsSequenceSetValue);
    }

    [ConditionalFact]
    public void Composed_capabilities_follow_the_loaded_native_engine()
    {
        using var context = new ItemContext(FileOptions<ItemContext>());
        var libraryVersion = context.Database
            .SqlQueryRaw<string>("SELECT library_version AS \"Value\" FROM pragma_version()")
            .AsEnumerable()
            .Single();
        var loadedVersion = DuckDBNativeEngine.Parse(libraryVersion);
        var capabilities = context.GetService<IDuckDBEngineCapabilities>();
        var expected = loadedVersion >= DuckDB20;

        Assert.Equal(loadedVersion, DuckDBNativeEngine.Version);
        Assert.Equal(expected, capabilities.SupportsAddColumnConstraints);
        Assert.Equal(expected, capabilities.SupportsAddUniqueConstraint);
        Assert.Equal(expected, capabilities.SupportsSequenceSetValue);
    }

    [ConditionalTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Sequence_restart_uses_setval_only_when_supported(bool supported)
    {
        using var context = new ItemContext(FileOptions<ItemContext>());
        var generator = CreateGenerator(context, supported);

        var sql = Sql(generator.Generate([new RestartSequenceOperation { Name = "Numbers", StartValue = 3 }]));

        if (supported)
        {
            Assert.Equal("SELECT setval('\"Numbers\"', 3, false);", sql.Trim());
        }
        else
        {
            Assert.Equal("ALTER SEQUENCE \"Numbers\" RESTART WITH 3;", sql.Trim());
        }
    }

    [ConditionalFact]
    public void Sequence_restart_quotes_schema_and_falls_back_to_the_model_start_value()
    {
        using var context = new ItemContext(FileOptions<ItemContext>());
        var generator = CreateGenerator(context, supported: true);

        var schemaSql = Sql(generator.Generate(
            [new RestartSequenceOperation { Name = "O'Brien", Schema = "audit", StartValue = 10 }]));
        var modelSql = Sql(generator.Generate(
            [new RestartSequenceOperation { Name = "Numbers" }],
            context.GetService<IDesignTimeModel>().Model));

        Assert.Equal("SELECT setval('\"audit\".\"O''Brien\"', 10, false);", schemaSql.Trim());
        Assert.Equal("SELECT setval('\"Numbers\"', 7, false);", modelSql.Trim());
    }

    [ConditionalFact]
    public void Sequence_restart_on_the_loaded_engine_follows_the_capability()
    {
        using var context = new ItemContext(FileOptions<ItemContext>());
        context.Database.ExecuteSqlRaw("CREATE SEQUENCE \"Numbers\" START WITH 1;");
        var commands = context.GetService<IMigrationsSqlGenerator>()
            .Generate([new RestartSequenceOperation { Name = "Numbers", StartValue = 40 }]);

        if (!context.GetService<IDuckDBEngineCapabilities>().SupportsSequenceSetValue)
        {
            Assert.Throws<DuckDBException>(() => Execute(context, commands));
            return;
        }

        Execute(context, commands);
        Assert.Equal(
            40L,
            context.Database.SqlQueryRaw<long>("SELECT nextval('\"Numbers\"') AS \"Value\"").AsEnumerable().Single());
    }

    [ConditionalTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unique_constraint_is_added_in_place_only_when_supported(bool supported)
    {
        using var context = new ItemContext(FileOptions<ItemContext>(options => options.EnableMigrationTableRebuilds()));
        var generator = CreateGenerator(context, supported);

        var sql = Sql(generator.Generate([AddCodeConstraint()], context.GetService<IDesignTimeModel>().Model));

        if (supported)
        {
            Assert.Contains("ADD CONSTRAINT \"AK_Items_Code\" UNIQUE (\"Code\")", sql);
            Assert.DoesNotContain("__ef_rebuild_", sql);
        }
        else
        {
            Assert.Contains("__ef_rebuild_Items", sql);
            Assert.DoesNotContain("ADD CONSTRAINT", sql);
        }
    }

    [ConditionalFact]
    public void Table_rebuild_absorbs_an_in_place_unique_constraint_for_the_same_table()
    {
        using var context = new ItemContext(FileOptions<ItemContext>(options => options.EnableMigrationTableRebuilds()));
        var generator = CreateGenerator(context, supported: true);
        var check = new AddCheckConstraintOperation { Table = "Items", Name = "CK_Items_Name", Sql = "length(\"Name\") > 0" };

        var sql = Sql(generator.Generate([check, AddCodeConstraint()], context.GetService<IDesignTimeModel>().Model));

        Assert.Equal(1, sql.Split("CREATE TABLE \"__ef_rebuild_Items\"", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("ADD CONSTRAINT", sql);
        Assert.Contains("UNIQUE (\"Code\")", sql);
    }

    [ConditionalFact]
    public void Unique_constraint_migration_enforces_uniqueness_on_the_loaded_engine()
    {
        using var context = new ItemContext(FileOptions<ItemContext>(options => options.EnableMigrationTableRebuilds()));
        context.Database.ExecuteSqlRaw(
            "CREATE TABLE \"Items\" (\"Id\" INTEGER PRIMARY KEY, \"Code\" VARCHAR NOT NULL, \"Name\" VARCHAR NOT NULL); "
            + "INSERT INTO \"Items\" VALUES (1, 'a', 'first');");
        var commands = context.GetService<IMigrationsSqlGenerator>()
            .Generate([AddCodeConstraint()], context.GetService<IDesignTimeModel>().Model);

        Execute(context, commands);

        Assert.Equal(
            context.GetService<IDuckDBEngineCapabilities>().SupportsAddUniqueConstraint,
            !Sql(commands).Contains("__ef_rebuild_", StringComparison.Ordinal));
        Assert.Equal("first", context.Items.Single().Name);
        Assert.Throws<DuckDBException>(
            () => context.Database.ExecuteSqlRaw("INSERT INTO \"Items\" VALUES (2, 'a', 'second');"));
    }

    [ConditionalFact]
    public void Scaffolding_reads_unique_constraints_on_the_loaded_engine()
    {
        using var connection = new DuckDBConnection($"DataSource={DbPath}");
        connection.Open();
        Execute(
            connection,
            """
            CREATE TABLE "Codes" (
                "Id" INTEGER PRIMARY KEY,
                "Code" VARCHAR CONSTRAINT "AK_Codes_Code" UNIQUE,
                "Region" INTEGER,
                "Number" INTEGER,
                UNIQUE ("Region", "Number"));
            """);

        using var services = CreateDesignTimeServices();
        var table = Assert.Single(services.GetRequiredService<IDatabaseModelFactory>()
            .Create(connection, new DatabaseModelFactoryOptions()).Tables);

        Assert.Collection(
            table.UniqueConstraints.OrderBy(constraint => constraint.Columns.Count),
            constraint => Assert.Equal(["Code"], constraint.Columns.Select(column => column.Name)),
            constraint => Assert.Equal(["Region", "Number"], constraint.Columns.Select(column => column.Name)));
        Assert.All(table.UniqueConstraints, constraint => Assert.Same(table, constraint.Table));

        // duckdb_constraints() reports generated names ("Codes_Code_key"), never the declared one, so the name is
        // left unset and the scaffolded model uses EF's default alternate-key name.
        Assert.All(table.UniqueConstraints, constraint => Assert.Null(constraint.Name));
        Assert.Equal(["Id"], table.PrimaryKey!.Columns.Select(column => column.Name));
    }

    [ConditionalFact]
    public void Scaffolding_reads_virtual_computed_columns_when_the_engine_reports_them()
    {
        using var connection = new DuckDBConnection($"DataSource={DbPath}");
        connection.Open();
        Execute(
            connection,
            """
            CREATE TABLE "Totals" (
                "Id" INTEGER PRIMARY KEY,
                "Net" INTEGER,
                "Gross" INTEGER GENERATED ALWAYS AS ("Net" * 2) VIRTUAL,
                "Status" VARCHAR DEFAULT 'open');
            """);
        bool enginePopulatesGeneratedMetadata;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                                  SELECT is_generated IS NOT NULL
                                    FROM information_schema.columns
                                   WHERE table_name = 'Totals' AND column_name = 'Gross'
                                  """;
            enginePopulatesGeneratedMetadata = (bool)command.ExecuteScalar()!;
        }

        using var services = CreateDesignTimeServices();
        var table = Assert.Single(services.GetRequiredService<IDatabaseModelFactory>()
            .Create(connection, new DatabaseModelFactoryOptions()).Tables);
        var gross = table.Columns.Single(column => column.Name == "Gross");
        var status = table.Columns.Single(column => column.Name == "Status");

        Assert.Equal("'open'", status.DefaultValueSql);
        Assert.Null(status.ComputedColumnSql);
        if (enginePopulatesGeneratedMetadata)
        {
            // DuckDB normalises the expression, for example to CAST((Net * 2) AS INTEGER).
            Assert.Contains("Net", gross.ComputedColumnSql);
            Assert.Contains("* 2", gross.ComputedColumnSql);
            Assert.False(gross.IsStored);
            Assert.Null(gross.DefaultValueSql);
        }
        else
        {
            // Without the flag the expression is indistinguishable from a default and stays DefaultValueSql.
            Assert.Null(gross.ComputedColumnSql);
            Assert.NotNull(gross.DefaultValueSql);
        }
    }

    [ConditionalTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Alterations_on_a_referenced_table_fail_early_only_when_the_engine_would_otherwise_attempt_them(bool supported)
    {
        using var context = new ReferenceContext(FileOptions<ReferenceContext>());
        var generator = CreateGenerator(context, supported);
        var model = context.GetService<IDesignTimeModel>().Model;
        var addUnique = new AddUniqueConstraintOperation { Table = "Parents", Name = "AK_Parents_Code", Columns = ["Code"] };
        var addRequired = new AddColumnOperation
        {
            Table = "Parents",
            Name = "Rank",
            ClrType = typeof(int),
            ColumnType = "INTEGER",
            IsNullable = false,
            DefaultValue = 0
        };

        if (!supported)
        {
            // DuckDB 1.5 rejects both forms for every table, so the provider keeps emitting them unchanged.
            Assert.Contains("ADD CONSTRAINT \"AK_Parents_Code\" UNIQUE", Sql(generator.Generate([addUnique], model)));
            Assert.Contains("ADD \"Rank\" INTEGER NOT NULL", Sql(generator.Generate([addRequired], model)));
            return;
        }

        foreach (var operation in new MigrationOperation[] { addUnique, addRequired })
        {
            var exception = Assert.Throws<NotSupportedException>(() => generator.Generate([operation], model));

            Assert.Contains("table 'Children' references it through foreign key 'FK_Children_Parents_ParentId'", exception.Message);
        }
    }

    [ConditionalFact]
    public void Alterations_on_unreferenced_self_referencing_or_later_dependent_tables_are_emitted()
    {
        using var context = new ReferenceContext(FileOptions<ReferenceContext>());
        var generator = CreateGenerator(context, supported: true);
        var model = context.GetService<IDesignTimeModel>().Model;
        var addUniqueOnChild = new AddUniqueConstraintOperation { Table = "Children", Name = "AK_Children_Tag", Columns = ["Tag"] };
        var addUniqueOnSelfReference = new AddUniqueConstraintOperation { Table = "Nodes", Name = "AK_Nodes_Code", Columns = ["Code"] };
        var addUniqueOnParent = new AddUniqueConstraintOperation { Table = "Parents", Name = "AK_Parents_Code", Columns = ["Code"] };
        var createChildren = new CreateTableOperation
        {
            Name = "Children",
            Columns = { new AddColumnOperation { Table = "Children", Name = "Id", ClrType = typeof(int), ColumnType = "INTEGER" } }
        };

        var sql = Sql(generator.Generate([addUniqueOnChild, addUniqueOnSelfReference, addUniqueOnParent, createChildren], model));

        Assert.Contains("ADD CONSTRAINT \"AK_Children_Tag\" UNIQUE", sql);
        Assert.Contains("ADD CONSTRAINT \"AK_Nodes_Code\" UNIQUE", sql);
        Assert.Contains("ADD CONSTRAINT \"AK_Parents_Code\" UNIQUE", sql);
    }

    [ConditionalFact]
    public void Unique_constraint_on_a_referenced_principal_fails_clearly_on_the_loaded_engine()
    {
        using var context = new ReferenceContext(FileOptions<ReferenceContext>());
        context.Database.ExecuteSqlRaw(
            "CREATE TABLE \"Parents\" (\"Id\" INTEGER PRIMARY KEY, \"Code\" VARCHAR NOT NULL); "
            + "CREATE TABLE \"Children\" (\"Id\" INTEGER PRIMARY KEY, \"ParentId\" INTEGER NOT NULL REFERENCES \"Parents\" (\"Id\"), \"Tag\" VARCHAR NOT NULL); "
            + "INSERT INTO \"Parents\" VALUES (1, 'a'); INSERT INTO \"Children\" VALUES (10, 1, 'x');");
        var addUnique = new AddUniqueConstraintOperation { Table = "Parents", Name = "AK_Parents_Code", Columns = ["Code"] };
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var model = context.GetService<IDesignTimeModel>().Model;

        if (context.GetService<IDuckDBEngineCapabilities>().SupportsAddUniqueConstraint)
        {
            // Without the early check DuckDB 2.0 fails later with "Dependency Error: Cannot alter entry".
            var exception = Assert.Throws<NotSupportedException>(() => generator.Generate([addUnique], model));
            Assert.Contains("'Children'", exception.Message);
        }
        else
        {
            var commands = generator.Generate([addUnique], model);
            Assert.Throws<DuckDBException>(() => Execute(context, commands));
        }

        Assert.Equal(1, context.Parents.Count());
        Assert.Equal(1, context.Children.Count());
    }

    private static DuckDBMigrationsSqlGenerator CreateGenerator(DbContext context, bool supported)
        => new(
            context.GetService<MigrationsSqlGeneratorDependencies>(),
            new DuckDBEngineCapabilities(isDuckLake: false, isQuack: false, supported ? DuckDB20 : DuckDB15));

    private static AddUniqueConstraintOperation AddCodeConstraint()
        => new() { Table = "Items", Name = "AK_Items_Code", Columns = ["Code"] };

    private static string Sql(IEnumerable<MigrationCommand> commands)
        => string.Join(Environment.NewLine, commands.Select(command => command.CommandText));

    private static void Execute(DbContext context, IEnumerable<MigrationCommand> commands)
    {
        foreach (var command in commands)
        {
            context.Database.ExecuteSqlRaw(command.CommandText);
        }
    }

    private static void Execute(DuckDBConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static ServiceProvider CreateDesignTimeServices()
    {
        var services = new ServiceCollection();
        new DuckDBDesignTimeServices().ConfigureDesignTimeServices(services);
        return services.BuildServiceProvider();
    }

    private sealed class ItemContext(DbContextOptions<ItemContext> options) : DbContext(options)
    {
        public DbSet<Item> Items => Set<Item>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasSequence<long>("Numbers").StartsAt(7);
            modelBuilder.Entity<Item>(entity =>
            {
                entity.ToTable("Items");
                entity.Property(item => item.Id).ValueGeneratedNever();
                entity.HasAlternateKey(item => item.Code).HasName("AK_Items_Code");
            });
        }
    }

    private sealed class ReferenceContext(DbContextOptions<ReferenceContext> options) : DbContext(options)
    {
        public DbSet<Parent> Parents => Set<Parent>();
        public DbSet<Child> Children => Set<Child>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Parent>(entity =>
            {
                entity.ToTable("Parents");
                entity.Property(parent => parent.Id).ValueGeneratedNever();
            });
            modelBuilder.Entity<Child>(entity =>
            {
                entity.ToTable("Children");
                entity.Property(child => child.Id).ValueGeneratedNever();
                entity.HasOne<Parent>().WithMany().HasForeignKey(child => child.ParentId).OnDelete(DeleteBehavior.Restrict);
            });
            modelBuilder.Entity<Node>(entity =>
            {
                entity.ToTable("Nodes");
                entity.Property(node => node.Id).ValueGeneratedNever();
                entity.HasOne<Node>().WithMany().HasForeignKey(node => node.ParentId).OnDelete(DeleteBehavior.Restrict);
            });
        }
    }

    private sealed class Parent
    {
        public int Id { get; set; }
        public string Code { get; set; } = "";
    }

    private sealed class Child
    {
        public int Id { get; set; }
        public int ParentId { get; set; }
        public string Tag { get; set; } = "";
    }

    private sealed class Node
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }
        public string Code { get; set; } = "";
    }

    private sealed class Item
    {
        public int Id { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
    }
}
