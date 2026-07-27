using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Metadata.Internal;
using DuckDB.EFCoreProvider.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public sealed class StructSchemaPlannerTests
{
    [Fact]
    public void Plans_ordered_nested_required_struct_column()
    {
        var operation = new CreateTableOperation { Name = "orders" };
        operation.Columns.Add(CreateField(
            "shipping_method",
            "VARCHAR",
            new DuckDBStructFieldInfo("Shipping", [], "method")));
        operation.Columns.Add(CreateField(
            "shipping_address_street",
            "VARCHAR",
            new DuckDBStructFieldInfo("Shipping", ["address"], "street")));

        var plan = DuckDBStructSchemaPlanner.PlanCreateTable(operation);

        Assert.True(plan.HasStructColumns);
        Assert.True(plan.TryGetReplacement(0, out var column));
        Assert.Equal("Shipping", column.Name);
        Assert.False(column.IsNullable);
        Assert.Equal(["method", "address"], column.Root.Children.Select(field => field.FieldName));
        Assert.Equal("street", Assert.Single(column.Root.Children[1].Children).FieldName);
        Assert.True(plan.IsSuppressed(1));
    }

    [Fact]
    public void Rejects_default_on_struct_leaf_before_rendering()
    {
        var operation = new CreateTableOperation { Name = "customers" };
        var column = CreateField(
            "location_city",
            "VARCHAR",
            new DuckDBStructFieldInfo("Location", [], "city"));
        column.DefaultValue = "unknown";
        operation.Columns.Add(column);

        var exception = Assert.Throws<NotSupportedException>(
            () => DuckDBStructSchemaPlanner.PlanCreateTable(operation));
        Assert.Contains("does not support defaults", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Migrations_annotation_provider_propagates_struct_field_for_remove_and_rename()
    {
        var options = new DbContextOptionsBuilder<AnnotationContext>()
            .UseDuckDB("DataSource=:memory:")
            .Options;
        using var context = new AnnotationContext(options);
        var column = context.Model.GetRelationalModel()
            .Tables.Single()
            .Columns.Single(value => value.Name == "location_city");
        var provider = context.GetService<IMigrationsAnnotationProvider>();

        Assert.Contains(
            provider.ForRemove(column),
            annotation => annotation.Name == DuckDBAnnotationNames.StructField);
        Assert.Contains(
            provider.ForRename(column),
            annotation => annotation.Name == DuckDBAnnotationNames.StructField);
    }

    [Fact]
    public void Alter_column_rejects_struct_annotation_on_old_column()
    {
        var options = new DbContextOptionsBuilder<AnnotationContext>()
            .UseDuckDB("DataSource=:memory:")
            .Options;
        using var context = new AnnotationContext(options);
        var operation = new AlterColumnOperation
        {
            Name = "location_city",
            Table = "AnnotationEntity",
            ClrType = typeof(string),
            ColumnType = "VARCHAR",
            IsNullable = false
        };
        operation.OldColumn.ClrType = typeof(string);
        operation.OldColumn.ColumnType = "VARCHAR";
        operation.OldColumn.IsNullable = false;
        operation.OldColumn.AddAnnotation(
            DuckDBAnnotationNames.StructField,
            new DuckDBStructFieldInfo("Location", [], "city"));

        var generator = context.GetService<IMigrationsSqlGenerator>();
        var exception = Assert.Throws<NotSupportedException>(() => generator.Generate([operation]));
        Assert.Contains("Cannot alter column", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_prefix_collision_in_struct_fields()
    {
        var operation = new CreateTableOperation { Name = "items" };
        operation.Columns.Add(CreateField(
            "location_address",
            "VARCHAR",
            new DuckDBStructFieldInfo("Location", [], "address")));
        operation.Columns.Add(CreateField(
            "location_address_country",
            "VARCHAR",
            new DuckDBStructFieldInfo("Location", ["address"], "country")));

        var exception = Assert.Throws<InvalidOperationException>(
            () => DuckDBStructSchemaPlanner.PlanCreateTable(operation));
        Assert.Contains("conflicting paths", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Location", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Location.address", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Location.address.country", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_case_insensitive_prefix_collision_in_struct_fields()
    {
        var operation = new CreateTableOperation { Name = "items" };
        operation.Columns.Add(CreateField(
            "location_address",
            "VARCHAR",
            new DuckDBStructFieldInfo("Location", [], "address")));
        operation.Columns.Add(CreateField(
            "location_address_country",
            "VARCHAR",
            new DuckDBStructFieldInfo("Location", ["Address"], "country")));

        var exception = Assert.Throws<InvalidOperationException>(
            () => DuckDBStructSchemaPlanner.PlanCreateTable(operation));
        Assert.Contains("conflicting paths", exception.Message, StringComparison.Ordinal);
    }

    private static AddColumnOperation CreateField(
        string name,
        string storeType,
        DuckDBStructFieldInfo field)
    {
        var column = new AddColumnOperation
        {
            Name = name,
            Table = "items",
            ClrType = typeof(string),
            ColumnType = storeType,
            IsNullable = false
        };
        column.AddAnnotation(DuckDBAnnotationNames.StructField, field);
        return column;
    }

    private sealed class AnnotationContext(DbContextOptions<AnnotationContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<AnnotationEntity>(entity =>
            {
                entity.Property(value => value.Id).ValueGeneratedNever();
                entity.ComplexProperty(value => value.Location).UseStructMapping();
            });
    }

    private sealed class AnnotationEntity
    {
        public int Id { get; set; }

        public required AnnotationAddress Location { get; set; }
    }

    private sealed class AnnotationAddress
    {
        public string City { get; set; } = null!;
    }
}
