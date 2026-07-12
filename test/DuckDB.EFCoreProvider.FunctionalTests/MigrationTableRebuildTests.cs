using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public sealed class MigrationTableRebuildTests : DuckDBTestBase
{
    [ConditionalFact]
    public void Constraint_alter_fails_fast_when_rebuilds_are_disabled()
    {
        using var context = new RebuildContext(FileOptions<RebuildContext>());
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = ForeignKeyOperation();

        var exception = Assert.Throws<NotSupportedException>(() => generator.Generate([operation], context.Model));

        Assert.Contains("EnableMigrationTableRebuilds", exception.Message);
    }

    [ConditionalFact]
    public void Foreign_key_change_rebuilds_table_and_enforces_constraint_when_enabled()
    {
        using var context = new RebuildContext(FileOptions<RebuildContext>(options => options.EnableMigrationTableRebuilds()));
        context.Database.OpenConnection();
        context.Database.ExecuteSqlRaw(
            "CREATE TABLE \"Parents\" (\"Id\" INTEGER PRIMARY KEY); "
            + "CREATE TABLE \"Children\" (\"Id\" INTEGER PRIMARY KEY, \"ParentId\" INTEGER NOT NULL); "
            + "INSERT INTO \"Parents\" VALUES (1); INSERT INTO \"Children\" VALUES (10, 1);");

        var generator = context.GetService<IMigrationsSqlGenerator>();
        var commands = generator.Generate([ForeignKeyOperation()], context.GetService<IDesignTimeModel>().Model);
        foreach (var command in commands)
        {
            context.Database.ExecuteSqlRaw(command.CommandText);
        }

        Assert.Equal(1, context.Children.Count());
        Assert.ThrowsAny<Exception>(() => context.Database.ExecuteSqlRaw("INSERT INTO \"Children\" VALUES (11, 999)"));
    }

    [ConditionalFact]
    public void EnableMigrationTableRebuilds_sets_the_option_and_is_disabled_by_default()
    {
        using var enabled = new RebuildContext(FileOptions<RebuildContext>(options => options.EnableMigrationTableRebuilds()));
        using var disabled = new RebuildContext(FileOptions<RebuildContext>());

        Assert.True(enabled.GetService<IDbContextOptions>().FindExtension<DuckDBOptionsExtension>()!.MigrationTableRebuilds);
        Assert.False(disabled.GetService<IDbContextOptions>().FindExtension<DuckDBOptionsExtension>()!.MigrationTableRebuilds);
    }

    [ConditionalFact]
    public void Rebuild_rejects_mixed_column_shape_changes()
    {
        using var context = new RebuildContext(FileOptions<RebuildContext>(options => options.EnableMigrationTableRebuilds()));
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var addColumn = new AddColumnOperation
        {
            Table = "Children",
            Name = "Extra",
            ClrType = typeof(string),
            IsNullable = true
        };

        var exception = Assert.Throws<NotSupportedException>(() => generator.Generate(
            [addColumn, ForeignKeyOperation()],
            context.GetService<IDesignTimeModel>().Model));

        Assert.Contains("separate migrations", exception.Message);
    }

    [ConditionalFact]
    public void Rebuild_emits_final_indexes_once()
    {
        using var context = new RebuildContext(FileOptions<RebuildContext>(options => options.EnableMigrationTableRebuilds()));
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var createIndex = new CreateIndexOperation
        {
            Name = "IX_Children_ParentId",
            Table = "Children",
            Columns = ["ParentId"]
        };

        var commands = generator.Generate(
            [ForeignKeyOperation(), createIndex],
            context.GetService<IDesignTimeModel>().Model);
        var sql = string.Join(Environment.NewLine, commands.Select(command => command.CommandText));

        Assert.Equal(1, sql.Split("CREATE INDEX \"IX_Children_ParentId\"", StringSplitOptions.None).Length - 1);
    }

    private static AddForeignKeyOperation ForeignKeyOperation()
        => new()
        {
            Name = "FK_Children_Parents_ParentId",
            Table = "Children",
            Columns = ["ParentId"],
            PrincipalTable = "Parents",
            PrincipalColumns = ["Id"]
        };

    private sealed class RebuildContext(DbContextOptions<RebuildContext> options) : DbContext(options)
    {
        public DbSet<Parent> Parents => Set<Parent>();
        public DbSet<Child> Children => Set<Child>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Parent>().Property(entity => entity.Id).ValueGeneratedNever();
            modelBuilder.Entity<Child>().Property(entity => entity.Id).ValueGeneratedNever();
            modelBuilder.Entity<Child>()
                .HasOne<Parent>()
                .WithMany()
                .HasForeignKey(entity => entity.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<Child>().HasIndex(entity => entity.ParentId);
        }
    }

    private sealed class Parent
    {
        public int Id { get; set; }
    }

    private sealed class Child
    {
        public int Id { get; set; }
        public int ParentId { get; set; }
    }
}