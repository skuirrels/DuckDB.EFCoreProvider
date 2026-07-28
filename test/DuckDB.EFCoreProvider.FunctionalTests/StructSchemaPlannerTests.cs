using DuckDB.EFCoreProvider.Design.Internal;
using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Metadata.Internal;
using DuckDB.EFCoreProvider.Migrations;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public sealed class StructSchemaPlannerTests
{
    [Fact]
    public void Struct_field_info_uses_value_equality_for_nested_paths()
    {
        var first = new DuckDBStructFieldInfo("Billing Root", ["detail field"], "code");
        var second = new DuckDBStructFieldInfo("Billing Root", ["detail field"], "code");

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

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

    [Fact]
    public void Snapshot_annotation_generator_renders_struct_fluent_api()
    {
        var options = new DbContextOptionsBuilder<AnnotationContext>()
            .UseDuckDB("DataSource=:memory:")
            .Options;
        using var context = new AnnotationContext(options);
        var generator = new DuckDBAnnotationCodeGenerator(
            new AnnotationCodeGeneratorDependencies(context.GetService<IRelationalTypeMappingSource>()));
        var property = context.Model
            .FindEntityType(typeof(AnnotationEntity))!
            .FindComplexProperty(nameof(AnnotationEntity.Location))!
            .ComplexType
            .FindProperty(nameof(AnnotationAddress.City))!;
        var annotations = property.GetAnnotations().ToDictionary(annotation => annotation.Name);

        var calls = generator.GenerateFluentApiCalls(property, annotations);

        var structFieldCall = Assert.Single(
            calls.Where(call => call.Method == nameof(DuckDBStructPropertyBuilderExtensions.HasStructField)));
        Assert.Equal(
            typeof(DuckDBStructPropertyBuilderExtensions),
            structFieldCall.MethodInfo?.DeclaringType);
        Assert.Equal(
            "DuckDB.EFCoreProvider.Extensions",
            structFieldCall.MethodInfo?.DeclaringType?.Namespace);

        var fieldNameCall = Assert.Single(
            calls.Where(call => call.Method == nameof(DuckDBStructPropertyBuilderExtensions.HasStructFieldName)));
        Assert.Equal(
            typeof(DuckDBStructPropertyBuilderExtensions),
            fieldNameCall.MethodInfo?.DeclaringType);
        Assert.DoesNotContain(DuckDBAnnotationNames.StructField, annotations.Keys);
        Assert.DoesNotContain(
            DuckDBAnnotationNames.StructMetadata,
            generator.FilterIgnoredAnnotations(
                    context.Model.FindEntityType(typeof(AnnotationEntity))!.GetAnnotations())
                .Select(annotation => annotation.Name));
    }

    [Fact]
    public void Migration_operation_generator_renders_struct_field_literal()
    {
        var reporter = new OperationReporter(
            new OperationReportHandler(
                _ => { },
                _ => { },
                _ => { },
                _ => { }));
        using var context = new AnnotationContext(
            new DbContextOptionsBuilder<AnnotationContext>()
                .UseDuckDB("DataSource=:memory:")
                .Options);
        var serviceProvider = new DesignTimeServicesBuilder(
            typeof(StructSchemaPlannerTests).Assembly,
            typeof(StructSchemaPlannerTests).Assembly,
            reporter,
            [])
            .Build(context);
        var generator = serviceProvider.GetRequiredService<ICSharpMigrationOperationGenerator>();
        var operation = new AlterColumnOperation
        {
            Name = "location_city",
            Table = "customers",
            ClrType = typeof(string),
            ColumnType = "VARCHAR",
            IsNullable = false
        };
        operation.AddAnnotation(
            DuckDBAnnotationNames.StructField,
            new DuckDBStructFieldInfo("Location", [], "city"));
        operation.OldColumn.ClrType = typeof(string);
        operation.OldColumn.ColumnType = "VARCHAR";
        operation.OldColumn.IsNullable = false;
        operation.OldColumn.AddAnnotation(
            DuckDBAnnotationNames.StructField,
            new DuckDBStructFieldInfo("Location", [], "old_city"));
        var builder = new IndentedStringBuilder();

        generator.Generate("migrationBuilder", [operation], builder);

        var code = builder.ToString();
        Assert.Contains(
            "DuckDB.EFCoreProvider.Metadata.DuckDBStructFieldInfo",
            code,
            StringComparison.Ordinal);
        Assert.Contains(
            "new DuckDB.EFCoreProvider.Metadata.DuckDBStructFieldInfo",
            code,
            StringComparison.Ordinal);
        Assert.Contains(".OldAnnotation(", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Snapshot_generator_renders_struct_mapping_fluent_api()
    {
        var reporter = new OperationReporter(
            new OperationReportHandler(
                _ => { },
                _ => { },
                _ => { },
                _ => { }));
        using var context = new AnnotationContext(
            new DbContextOptionsBuilder<AnnotationContext>()
                .UseDuckDB("DataSource=:memory:")
                .Options);
        var serviceProvider = new DesignTimeServicesBuilder(
            typeof(StructSchemaPlannerTests).Assembly,
            typeof(StructSchemaPlannerTests).Assembly,
            reporter,
            [])
            .Build(context);
        var builder = new IndentedStringBuilder();

        serviceProvider
            .GetRequiredService<ICSharpSnapshotGenerator>()
            .Generate("BuildModel", context.GetService<IDesignTimeModel>().Model, builder);

        var code = builder.ToString();
        Assert.Contains("DuckDBStructPropertyBuilderExtensions.UseStructMapping(", code, StringComparison.Ordinal);
        Assert.Contains("DuckDBStructPropertyBuilderExtensions.HasStructField(", code, StringComparison.Ordinal);
        Assert.Contains("DuckDBStructPropertyBuilderExtensions.HasStructFieldName(", code, StringComparison.Ordinal);
        Assert.DoesNotContain(DuckDBAnnotationNames.StructMapping, code, StringComparison.Ordinal);
    }

    [Fact]
    public void Compiled_model_generator_omits_opaque_struct_metadata()
    {
        var reporter = new OperationReporter(
            new OperationReportHandler(
                _ => { },
                _ => { },
                _ => { },
                _ => { }));
        using var context = new AnnotationContext(
            new DbContextOptionsBuilder<AnnotationContext>()
                .UseDuckDB("DataSource=:memory:")
                .Options);
        var serviceProvider = new DesignTimeServicesBuilder(
            typeof(StructSchemaPlannerTests).Assembly,
            typeof(StructSchemaPlannerTests).Assembly,
            reporter,
            [])
            .Build(context);
        var files = serviceProvider
            .GetRequiredService<ICompiledModelCodeGenerator>()
            .GenerateModel(
                context.GetService<IDesignTimeModel>().Model,
                new CompiledModelCodeGenerationOptions
                {
                    ModelNamespace = "Generated",
                    ContextType = typeof(AnnotationContext)
                });
        var code = string.Join(Environment.NewLine, files.Select(file => file.Code));

        Assert.DoesNotContain(nameof(DuckDBStructEntityMetadata), code, StringComparison.Ordinal);
        Assert.Contains(DuckDBAnnotationNames.StructColumnMap, code, StringComparison.Ordinal);
        Assert.Contains(nameof(DuckDBStructFieldInfo), code, StringComparison.Ordinal);
        Assert.Contains(nameof(DuckDBStructMapping), code, StringComparison.Ordinal);
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
                entity.ComplexProperty(value => value.Location, complex =>
                {
                    complex.UseStructMapping();
                    complex.Property(value => value.City).HasStructFieldName("city_name");
                });
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
