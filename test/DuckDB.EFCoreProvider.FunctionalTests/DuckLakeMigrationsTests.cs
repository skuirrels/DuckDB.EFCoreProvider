using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
///     End-to-end EF Core migrations against a DuckLake catalog: the claimed migrations lock, the keyless history
///     table, in-place schema evolution, rollback, capability-gated rejections and opt-in table rebuilds.
/// </summary>
public sealed class DuckLakeMigrationsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ducklake-migrations-{Guid.NewGuid():N}");

    public DuckLakeMigrationsTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "metadata"));
        Directory.CreateDirectory(Path.Combine(_root, "data"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Migrate_creates_keyless_history_and_lock_tables_and_applies_every_migration()
    {
        using var context = new EvolvingContext(Options<EvolvingContext>());

        context.Database.Migrate();

        Assert.Equal(
            [CreateBlogs.Id, AddBlogColumns.Id, RenameAndWidenBlogColumns.Id],
            AppliedMigrations(context));
        Assert.Empty(context.Database.GetPendingMigrations());
        Assert.Equal(["Holder:VARCHAR", "Id:INTEGER", "Timestamp:VARCHAR"], Columns(context, "__EFMigrationsLock"));
        Assert.Equal(
            ["Id:INTEGER", "Name:VARCHAR", "Rating:BIGINT", "Url:VARCHAR", "Score:INTEGER", "Archived:BOOLEAN"],
            Columns(context, "Blogs", ordered: true));
        Assert.Contains("BlogId:INTEGER", Columns(context, "Articles"));
        Assert.False(TableExists(context, "Posts"));

        // The lock is free once Migrate returns, and the second run is a no-op.
        Assert.Equal("free", LockState(context));
        context.Database.Migrate();
        Assert.Equal(3, AppliedMigrations(context).Length);
    }

    [Fact]
    public async Task Migrations_evolve_existing_rows_with_literal_defaults_renames_and_widening()
    {
        await using (var context = new EvolvingContext(Options<EvolvingContext>()))
        {
            await context.GetService<IMigrator>().MigrateAsync(CreateBlogs.Id);
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO \"Blogs\" (\"Id\", \"Title\", \"Rating\", \"Obsolete\") VALUES (1, 'first', 3, 'x')");
        }

        await using (var context = new EvolvingContext(Options<EvolvingContext>()))
        {
            await context.Database.MigrateAsync();
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO \"Blogs\" (\"Id\", \"Name\", \"Rating\") VALUES (2, 'second', 9223372036854775807)");

            Assert.Equal(
                ["1|first|3|5|false", "2|second|9223372036854775807|5|false"],
                await context.Database.SqlQueryRaw<string>(
                        """
                        SELECT concat_ws('|', "Id", "Name", "Rating", "Score", "Archived") AS "Value"
                        FROM "Blogs" ORDER BY "Id"
                        """)
                    .ToListAsync());
        }
    }

    [Fact]
    public void Migrate_down_reverts_added_columns_and_history()
    {
        using var context = new EvolvingContext(Options<EvolvingContext>());
        var migrator = context.GetService<IMigrator>();
        migrator.Migrate(AddBlogColumns.Id);

        migrator.Migrate(CreateBlogs.Id);

        Assert.Equal([CreateBlogs.Id], AppliedMigrations(context));
        Assert.Equal(
            ["Id:INTEGER", "Title:VARCHAR", "Rating:INTEGER", "Obsolete:VARCHAR"],
            Columns(context, "Blogs", ordered: true));
    }

    [Fact]
    public void Failed_migration_rolls_back_its_schema_changes_and_history_row()
    {
        using (var context = new FailingContext(Options<FailingContext>()))
        {
            context.GetService<IMigrator>().Migrate(CreateLedger.Id);

            Assert.ThrowsAny<Exception>(() => context.Database.Migrate());
        }

        using (var context = new FailingContext(Options<FailingContext>()))
        {
            Assert.Equal([CreateLedger.Id], AppliedMigrations(context));
            Assert.False(TableExists(context, "Audit"));
            Assert.Equal(["Id:INTEGER"], Columns(context, "Ledger"));
            Assert.Equal("free", LockState(context));
        }
    }

    [Fact]
    public void Held_lock_times_out_with_DuckLake_release_guidance_and_is_reacquired_after_release()
    {
        using var holder = new EvolvingContext(Options<EvolvingContext>());
        var heldLock = holder.GetService<IHistoryRepository>().AcquireDatabaseLock();
        Assert.Equal("held", LockState(holder));

        using var waiter = new EvolvingContext(
            Options<EvolvingContext>(duckdb => duckdb.MigrationLockTimeout(TimeSpan.FromSeconds(2))));
        var exception = Assert.Throws<TimeoutException>(() => waiter.GetService<IHistoryRepository>().AcquireDatabaseLock());

        Assert.Contains("__EFMigrationsLock", exception.Message);
        Assert.Contains("held since", exception.Message);
        Assert.Contains("UPDATE \"__EFMigrationsLock\" SET \"Holder\" = NULL", exception.Message);
        Assert.DoesNotContain("DELETE FROM", exception.Message);

        heldLock.Dispose();
        Assert.Equal("free", LockState(holder));
        using var reacquired = waiter.GetService<IHistoryRepository>().AcquireDatabaseLock();
        Assert.Equal("held", LockState(waiter));
    }

    [Fact]
    public async Task Waiter_acquires_the_lock_when_the_holder_releases_it_within_the_timeout()
    {
        await using var holder = new EvolvingContext(Options<EvolvingContext>());
        var heldLock = await holder.GetService<IHistoryRepository>().AcquireDatabaseLockAsync();

        var release = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            await heldLock.DisposeAsync();
        });

        await using var waiter = new EvolvingContext(
            Options<EvolvingContext>(duckdb => duckdb.MigrationLockTimeout(TimeSpan.FromSeconds(30))));
        await using var acquired = await waiter.GetService<IHistoryRepository>().AcquireDatabaseLockAsync();

        Assert.NotNull(acquired);
        await release;
    }

    [Fact]
    public async Task Concurrent_migrators_serialise_on_the_lock_and_apply_each_migration_once()
    {
        var contexts = Enumerable.Range(0, 3)
            .Select(_ => new EvolvingContext(
                Options<EvolvingContext>(duckdb => duckdb.MigrationLockTimeout(TimeSpan.FromMinutes(2)))))
            .ToArray();
        try
        {
            // DuckLake's first attach of a new catalog is not safe to run concurrently, so create it before the race.
            contexts[0].Database.OpenConnection();
            contexts[0].Database.CloseConnection();

            using var start = new Barrier(contexts.Length);
            await Task.WhenAll(contexts.Select(context => Task.Run(() =>
            {
                start.SignalAndWait();
                context.Database.Migrate();
            })));

            Assert.Equal(
                [CreateBlogs.Id, AddBlogColumns.Id, RenameAndWidenBlogColumns.Id],
                AppliedMigrations(contexts[0]));
            Assert.Equal(1, contexts[0].Database.SqlQueryRaw<int>("SELECT count(*)::INTEGER AS \"Value\" FROM \"__EFMigrationsLock\"").Single());
            Assert.Equal("free", LockState(contexts[0]));
        }
        finally
        {
            foreach (var context in contexts)
            {
                await context.DisposeAsync();
            }
        }
    }

    [Fact]
    public void Script_generation_renders_DuckLake_safe_history_and_default_SQL()
    {
        using var context = new EvolvingContext(Options<EvolvingContext>());

        var script = context.GetService<IMigrator>().GenerateScript();

        Assert.Contains("CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\"", script);
        Assert.DoesNotContain("PRIMARY KEY", script);
        Assert.DoesNotContain("CREATE INDEX", script);
        Assert.DoesNotContain("FOREIGN KEY", script);
        Assert.Contains("\"Score\" INTEGER NULL DEFAULT 5", script);
        Assert.Contains("\"Archived\" BOOLEAN NULL DEFAULT 'false'", script);
        Assert.Contains("ALTER \"Rating\" TYPE BIGINT", script);
    }

    [Fact]
    public void Unsupported_in_place_changes_fail_during_script_generation_with_guidance()
    {
        using var context = new EvolvingContext(Options<EvolvingContext>());
        var generator = context.GetService<IMigrationsSqlGenerator>();

        var narrowing = Assert.Throws<NotSupportedException>(() => generator.Generate(
        [
            new AlterColumnOperation
            {
                Table = "Blogs", Name = "Rating", ClrType = typeof(int), ColumnType = "INTEGER",
                OldColumn = new AddColumnOperation { Table = "Blogs", Name = "Rating", ClrType = typeof(long), ColumnType = "BIGINT" }
            }
        ]));
        Assert.Contains("only widen column types", narrowing.Message);
        Assert.Contains("EnableMigrationTableRebuilds", narrowing.Message);

        var requiredColumn = Assert.Throws<NotSupportedException>(() => generator.Generate(
        [
            new AddColumnOperation
            {
                Table = "Blogs", Name = "Required", ClrType = typeof(int), ColumnType = "INTEGER",
                IsNullable = false, DefaultValue = 0
            }
        ]));
        Assert.Contains("'Blogs.Required'", requiredColumn.Message);
        Assert.Contains("EnableMigrationTableRebuilds", requiredColumn.Message);

        var schemaTransfer = Assert.Throws<NotSupportedException>(() => generator.Generate(
            [new RenameTableOperation { Name = "Blogs", NewName = "Blogs", NewSchema = "archive" }]));
        Assert.Contains("SET SCHEMA", schemaTransfer.Message);

        var unsupportedDefault = Assert.Throws<NotSupportedException>(() => generator.Generate(
        [
            new AddColumnOperation
            {
                Table = "Blogs", Name = "Tags", ClrType = typeof(int[]), ColumnType = "INTEGER[]",
                IsNullable = true, DefaultValue = new[] { 1 }
            }
        ]));
        Assert.Contains("'Blogs.Tags'", unsupportedDefault.Message);
    }

    [Fact]
    public void Widening_and_typed_defaults_are_applied_in_place()
    {
        using var context = new EvolvingContext(Options<EvolvingContext>());
        var generator = context.GetService<IMigrationsSqlGenerator>();

        var sql = Sql(generator.Generate(
        [
            new AlterColumnOperation
            {
                Table = "Blogs", Name = "Rating", ClrType = typeof(double), ColumnType = "DOUBLE",
                OldColumn = new AddColumnOperation { Table = "Blogs", Name = "Rating", ClrType = typeof(int), ColumnType = "INTEGER" }
            },
            new AddColumnOperation
            {
                Table = "Blogs", Name = "Published", ClrType = typeof(DateTime), ColumnType = "TIMESTAMP",
                IsNullable = true, DefaultValue = new DateTime(2026, 1, 2, 3, 4, 5)
            },
            new AddColumnOperation
            {
                Table = "Blogs", Name = "Key", ClrType = typeof(Guid), ColumnType = "UUID",
                IsNullable = true, DefaultValue = Guid.Empty
            }
        ]));

        Assert.Contains("ALTER \"Rating\" TYPE DOUBLE", sql);
        Assert.Contains("\"Published\" TIMESTAMP NULL DEFAULT '2026-01-02 03:04:05'", sql);
        Assert.Contains("\"Key\" UUID NULL DEFAULT '00000000-0000-0000-0000-000000000000'", sql);

        // CREATE TABLE accepts typed default literals on DuckLake, so table creation keeps its normal rendering.
        var createTable = Sql(generator.Generate(
        [
            new CreateTableOperation
            {
                Name = "Flags",
                Columns =
                {
                    new AddColumnOperation
                    {
                        Table = "Flags", Name = "Active", ClrType = typeof(bool), ColumnType = "BOOLEAN",
                        IsNullable = false, DefaultValue = true
                    }
                }
            }
        ]));
        Assert.Contains("\"Active\" BOOLEAN NOT NULL DEFAULT", createTable);
        Assert.DoesNotContain("'true'", createTable);
    }

    [Fact]
    public void Table_rebuild_converts_types_and_adds_required_columns_on_DuckLake()
    {
        using (var context = new RebuildingContext(Options<RebuildingContext>(duckdb => duckdb.EnableMigrationTableRebuilds())))
        {
            context.GetService<IMigrator>().Migrate(CreateItems.Id);
            context.Database.ExecuteSqlRaw("INSERT INTO \"Items\" (\"Id\", \"Code\") VALUES (1, '12'), (2, '34')");

            context.Database.Migrate();

            Assert.Equal([CreateItems.Id, ConvertItemCode.Id], AppliedMigrations(context));
            Assert.Equal(["Id:INTEGER", "Code:INTEGER", "Quantity:INTEGER"], Columns(context, "Items", ordered: true));
            Assert.Equal(
                ["1|12|0", "2|34|0"],
                context.Database.SqlQueryRaw<string>(
                        "SELECT concat_ws('|', \"Id\", \"Code\", \"Quantity\") AS \"Value\" FROM \"Items\" ORDER BY \"Id\"")
                    .ToList());
            Assert.False(TableExists(context, "__ef_rebuild_Items"));

            // The rebuilt table carries the required column's NOT NULL constraint and literal default.
            Assert.ThrowsAny<Exception>(() => context.Database.ExecuteSqlRaw(
                "INSERT INTO \"Items\" (\"Id\", \"Code\", \"Quantity\") VALUES (3, 56, NULL)"));
            context.Database.ExecuteSqlRaw("INSERT INTO \"Items\" (\"Id\", \"Code\") VALUES (4, 78)");
            Assert.Equal(0, context.Database.SqlQueryRaw<int>("SELECT \"Quantity\" AS \"Value\" FROM \"Items\" WHERE \"Id\" = 4").Single());
        }
    }

    [Fact]
    public void Table_rebuild_script_omits_indexes_and_constraints_on_DuckLake()
    {
        using var context = new RebuildingContext(Options<RebuildingContext>(duckdb => duckdb.EnableMigrationTableRebuilds()));

        var script = context.GetService<IMigrator>().GenerateScript(CreateItems.Id, ConvertItemCode.Id);

        Assert.Contains("CREATE TABLE \"__ef_rebuild_Items\" AS SELECT * FROM \"Items\"", script);
        Assert.Contains("INSERT INTO \"Items\" (\"Id\", \"Code\") SELECT \"Id\", \"Code\" FROM \"__ef_rebuild_Items\"", script);
        Assert.Contains("\"Quantity\" INTEGER NOT NULL DEFAULT 0", script);
        Assert.DoesNotContain("PRIMARY KEY", script);
        Assert.DoesNotContain("CREATE INDEX", script);
    }

    private DbContextOptions<TContext> Options<TContext>(Action<DuckDBDbContextOptionsBuilder>? duckdb = null)
        where TContext : DbContext
        => new DbContextOptionsBuilder<TContext>()
            .UseDuckLake(
                Path.Combine(_root, "metadata", "catalog.ducklake"),
                duckLake => duckLake.DataPath(Path.Combine(_root, "data")),
                duckdb)
            // The hand-written test migrations have no model snapshot.
            .ConfigureWarnings(warnings => warnings.Ignore(
                RelationalEventId.PendingModelChangesWarning,
                CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

    private static string[] AppliedMigrations(DbContext context)
        => context.Database.SqlQueryRaw<string>(
                "SELECT \"MigrationId\" AS \"Value\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\"")
            .ToArray();

    private static string[] Columns(DbContext context, string table, bool ordered = false)
    {
        var columns = context.Database.SqlQueryRaw<string>(
                """
                SELECT column_name || ':' || data_type AS "Value" FROM duckdb_columns()
                WHERE database_name = current_database() AND table_name = {0}
                ORDER BY column_index
                """,
                table)
            .ToArray();
        return ordered ? columns : columns.Order(StringComparer.Ordinal).ToArray();
    }

    private static bool TableExists(DbContext context, string table)
        => context.Database.SqlQueryRaw<bool>(
                """
                SELECT count(*) > 0 AS "Value" FROM duckdb_tables()
                WHERE database_name = current_database() AND table_name = {0}
                """,
                table)
            .Single();

    private static string LockState(DbContext context)
        => context.Database.SqlQueryRaw<string>(
                "SELECT CASE WHEN \"Holder\" IS NULL THEN 'free' ELSE 'held' END AS \"Value\" FROM \"__EFMigrationsLock\"")
            .Single();

    private static string Sql(IEnumerable<MigrationCommand> commands)
        => string.Join(Environment.NewLine, commands.Select(command => command.CommandText));

    public sealed class EvolvingContext(DbContextOptions<EvolvingContext> options) : DbContext(options);

    public sealed class FailingContext(DbContextOptions<FailingContext> options) : DbContext(options);

    public sealed class RebuildingContext(DbContextOptions<RebuildingContext> options) : DbContext(options);

    [DbContext(typeof(EvolvingContext))]
    [Migration(Id)]
    public sealed class CreateBlogs : Migration
    {
        public const string Id = "20260101000000_CreateBlogs";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Blogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "VARCHAR", nullable: true),
                    Rating = table.Column<int>(type: "INTEGER", nullable: true),
                    Obsolete = table.Column<string>(type: "VARCHAR", nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_Blogs", blog => blog.Id));

            migrationBuilder.CreateTable(
                name: "Posts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    BlogId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Posts", post => post.Id);
                    table.ForeignKey("FK_Posts_Blogs_BlogId", post => post.BlogId, "Blogs", "Id");
                });

            migrationBuilder.CreateIndex(name: "IX_Posts_BlogId", table: "Posts", column: "BlogId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Posts");
            migrationBuilder.DropTable(name: "Blogs");
        }
    }

    [DbContext(typeof(EvolvingContext))]
    [Migration(Id)]
    public sealed class AddBlogColumns : Migration
    {
        public const string Id = "20260101000001_AddBlogColumns";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "Url", table: "Blogs", type: "VARCHAR", nullable: true);
            migrationBuilder.AddColumn<int>(name: "Score", table: "Blogs", type: "INTEGER", nullable: true, defaultValue: 5);
            migrationBuilder.AddColumn<bool>(
                name: "Archived", table: "Blogs", type: "BOOLEAN", nullable: true, defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Archived", table: "Blogs");
            migrationBuilder.DropColumn(name: "Score", table: "Blogs");
            migrationBuilder.DropColumn(name: "Url", table: "Blogs");
        }
    }

    [DbContext(typeof(EvolvingContext))]
    [Migration(Id)]
    public sealed class RenameAndWidenBlogColumns : Migration
    {
        public const string Id = "20260101000002_RenameAndWidenBlogColumns";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Obsolete", table: "Blogs");
            migrationBuilder.RenameColumn(name: "Title", table: "Blogs", newName: "Name");
            migrationBuilder.AlterColumn<long>(
                name: "Rating", table: "Blogs", type: "BIGINT", nullable: true,
                oldClrType: typeof(int), oldType: "INTEGER", oldNullable: true);
            migrationBuilder.RenameTable(name: "Posts", newName: "Articles");
        }
    }

    [DbContext(typeof(FailingContext))]
    [Migration(Id)]
    public sealed class CreateLedger : Migration
    {
        public const string Id = "20260102000000_CreateLedger";

        protected override void Up(MigrationBuilder migrationBuilder)
            => migrationBuilder.CreateTable(
                name: "Ledger",
                columns: table => new { Id = table.Column<int>(type: "INTEGER", nullable: false) });
    }

    [DbContext(typeof(FailingContext))]
    [Migration(Id)]
    public sealed class FailAfterSchemaChanges : Migration
    {
        public const string Id = "20260102000001_FailAfterSchemaChanges";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Audit",
                columns: table => new { Id = table.Column<int>(type: "INTEGER", nullable: false) });
            migrationBuilder.AddColumn<string>(name: "Memo", table: "Ledger", type: "VARCHAR", nullable: true);
            migrationBuilder.Sql("SELECT error('migration failed on purpose');");
        }
    }

    [DbContext(typeof(RebuildingContext))]
    [Migration(Id)]
    public sealed class CreateItems : Migration
    {
        public const string Id = "20260103000000_CreateItems";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Items",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Code = table.Column<string>(type: "VARCHAR", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_Items", item => item.Id));
        }
    }

    [DbContext(typeof(RebuildingContext))]
    [Migration(Id)]
    public sealed class ConvertItemCode : Migration
    {
        public const string Id = "20260103000001_ConvertItemCode";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "Code", table: "Items", type: "INTEGER", nullable: false,
                oldClrType: typeof(string), oldType: "VARCHAR");
            migrationBuilder.AddColumn<int>(
                name: "Quantity", table: "Items", type: "INTEGER", nullable: false, defaultValue: 0);
            migrationBuilder.CreateIndex(name: "IX_Items_Code", table: "Items", column: "Code");
        }

        protected override void BuildTargetModel(ModelBuilder modelBuilder)
            => modelBuilder.Entity("Items", entity =>
            {
                entity.Property<int>("Id").HasColumnType("INTEGER");
                entity.Property<int>("Code").HasColumnType("INTEGER");
                entity.Property<int>("Quantity").HasColumnType("INTEGER").HasDefaultValue(0);
                entity.HasKey("Id");
                entity.HasIndex("Code");
                entity.ToTable("Items");
            });
    }
}