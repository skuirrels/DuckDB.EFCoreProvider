using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Diagnostics;
using System.Reflection;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public sealed class StructDesignTimeWorkflowTests
{
    [Fact]
    public void Generated_migration_and_compiled_model_workflows_compile_and_preserve_struct_guards()
    {
        var root = Path.Combine(Path.GetTempPath(), $"duckdb-struct-design-time-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "consumer.duckdb");

        try
        {
            File.WriteAllText(
                Path.Combine(root, "Consumer.csproj"),
                CreateProjectFile(FindProviderProject()));
            File.WriteAllText(Path.Combine(root, "Program.cs"), ConsumerSource);

            RunDotnet(root, "build");
            RunDotnet(root, "ef", "migrations", "add", "InitialStruct", "--no-build");
            Assert.NotEmpty(Directory.GetFiles(Path.Combine(root, "Migrations"), "*.cs"));

            RunDotnet(root, "build");
            RunDotnet(
                root,
                ["ef", "database", "update", "--no-build"],
                ("STRUCT_CONSUMER_DATABASE", databasePath));
            RunDotnet(
                root,
                "ef",
                "dbcontext",
                "optimize",
                "--no-build",
                "--output-dir",
                "GeneratedModel",
                "--namespace",
                "Consumer.Generated",
                "--context",
                "ConsumerContext");

            RunDotnet(root, "build");
            RunDotnet(
                root,
                ["run", "--no-build"],
                ("STRUCT_CONSUMER_DATABASE", databasePath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string FindProviderProject()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var project = Path.Combine(
                directory.FullName,
                "src",
                "DuckDB.EFCoreProvider",
                "DuckDB.EFCoreProvider.csproj");
            if (File.Exists(project))
            {
                return project;
            }
        }

        throw new InvalidOperationException("Could not locate the provider project from the functional-test output directory.");
    }

    private static string CreateProjectFile(string providerProject)
        => $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{providerProject.Replace("&", "&amp;")}" />
                <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.10">
                  <PrivateAssets>all</PrivateAssets>
                </PackageReference>
              </ItemGroup>
            </Project>
            """;

    private static void RunDotnet(
        string workingDirectory,
        params string[] arguments)
    {
        RunDotnet(workingDirectory, arguments, environment: null);
    }

    private static void RunDotnet(
        string workingDirectory,
        string[] arguments,
        (string Name, string Value)? environment)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (environment is { } variable)
        {
            process.StartInfo.Environment[variable.Name] = variable.Value;
        }

        process.Start();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var text = output.GetAwaiter().GetResult() + Environment.NewLine + error.GetAwaiter().GetResult();

        Assert.True(
            process.ExitCode == 0,
            $"dotnet {string.Join(" ", arguments)} failed with exit code {process.ExitCode}.{Environment.NewLine}{text}");
    }

    private const string ConsumerSource = """"
        using System.Reflection;
        using DuckDB.EFCoreProvider.Extensions;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.Design;
        using Microsoft.EntityFrameworkCore.Metadata;

        public sealed class ConsumerContext(DbContextOptions<ConsumerContext> options) : DbContext(options)
        {
            public DbSet<ConsumerEntity> Entities => Set<ConsumerEntity>();

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<ConsumerEntity>(entity =>
                {
                    entity.HasKey(value => value.Id);
                    entity.ComplexProperty(value => value.Billing, complex =>
                    {
                        complex.UseStructMapping("Billing Root");
                        complex.Property(value => value.City).HasStructFieldName("city \"name\"");
                        complex.ComplexProperty(value => value.Details, nested =>
                            nested.HasStructFieldName("detail field"));
                    });
                    entity.ComplexProperty(value => value.Shipping, complex =>
                        complex.UseStructMapping("Shipping Root"));
                });
            }
        }

        public sealed class ConsumerContextFactory : IDesignTimeDbContextFactory<ConsumerContext>
        {
            public ConsumerContext CreateDbContext(string[] args)
            {
                var databasePath = Environment.GetEnvironmentVariable("STRUCT_CONSUMER_DATABASE")
                    ?? "consumer.duckdb";
                var options = new DbContextOptionsBuilder<ConsumerContext>()
                    .UseDuckDB($"Data Source={databasePath}")
                    .Options;
                return new ConsumerContext(options);
            }
        }

        public sealed class ConsumerEntity
        {
            public int Id { get; set; }
            public Address Billing { get; set; } = new();
            public Address Shipping { get; set; } = new();
        }

        public sealed class Address
        {
            public string City { get; set; } = null!;
            public AddressDetails Details { get; set; } = new();
        }

        public sealed class AddressDetails
        {
            public string Code { get; set; } = null!;
        }

        public static class Program
        {
            public static void Main()
            {
                var databasePath = Environment.GetEnvironmentVariable("STRUCT_CONSUMER_DATABASE")
                    ?? throw new InvalidOperationException("The test database path was not configured.");
                var model = FindCompiledModel();
                var options = new DbContextOptionsBuilder<ConsumerContext>()
                    .UseDuckDB($"Data Source={databasePath}")
                    .UseModel(model)
                    .Options;

                using var context = new ConsumerContext(options);
                var runtimeEntityType = context.Model.FindEntityType(typeof(ConsumerEntity))
                    ?? throw new InvalidOperationException("The compiled model has no ConsumerEntity metadata.");
                Require(
                    runtimeEntityType.FindAnnotation("DuckDB:StructColumnMap")?.Value is not null,
                    "Compiled-model STRUCT column metadata was not restored.");
                Require(
                    runtimeEntityType.GetStructColumnMap() is { Count: > 0 },
                    "Compiled-model STRUCT column metadata could not be read by the provider.");
                var columns = context.Database
                    .SqlQueryRaw<string>("SELECT name AS \"Value\" FROM pragma_table_info('Entities')")
                    .ToList();
                Require(columns.Contains("Billing Root") && columns.Contains("Shipping Root"), "STRUCT roots were not created.");

                context.Entities.Add(
                    new ConsumerEntity
                    {
                        Id = 1,
                        Billing = new Address
                        {
                            City = "billing",
                            Details = new AddressDetails { Code = "billing-code" }
                        },
                        Shipping = new Address
                        {
                            City = "shipping",
                            Details = new AddressDetails { Code = "shipping-code" }
                        }
                    });
                context.SaveChanges();

                var row = context.Entities.Single();
                Require(row.Billing.City == "billing", "Compiled-model STRUCT query did not round-trip the billing field.");
                Require(row.Billing.Details.Code == "billing-code", "Compiled-model nested STRUCT query did not round-trip.");

                var bulkException = Capture(
                    () => context.BulkInsert(
                        new[]
                        {
                            new ConsumerEntity
                            {
                                Id = 2,
                                Billing = new Address { City = "bulk", Details = new AddressDetails { Code = "bulk" } },
                                Shipping = new Address { City = "bulk", Details = new AddressDetails { Code = "bulk" } }
                            }
                        }));
                Require(
                    bulkException is NotSupportedException
                    && bulkException.Message
                        == "Bulk insert into 'Entities' is not supported for entities with DuckDB STRUCT mappings. "
                        + "Use SaveChanges instead.",
                    "BulkInsert did not fail through the provider's STRUCT guard.");

                var upsertException = Capture(
                    () => context.Upsert(
                        new[]
                        {
                            new ConsumerEntity
                            {
                                Id = 3,
                                Billing = new Address { City = "upsert", Details = new AddressDetails { Code = "upsert" } },
                                Shipping = new Address { City = "upsert", Details = new AddressDetails { Code = "upsert" } }
                            }
                        }));
                Require(
                    upsertException is NotSupportedException
                    && upsertException.Message
                        == "Upsert does not support entity 'ConsumerEntity' because it contains struct-mapped complex properties. "
                        + "STRUCT columns are consolidated at the physical layer and cannot be staged via the DuckDB Appender API. "
                        + "Use SaveChanges instead.",
                    "Upsert did not fail through the provider's STRUCT guard.");
            }

            private static IModel FindCompiledModel()
            {
                var modelType = Assembly.GetExecutingAssembly()
                    .GetTypes()
                    .Single(type => type.Name.EndsWith("ContextModel", StringComparison.Ordinal));
                return (IModel?)modelType
                    .GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    ?.GetValue(null)
                    ?? throw new InvalidOperationException("The generated compiled model has no Instance property.");
            }

            private static Exception? Capture(Action action)
            {
                try
                {
                    action();
                    return null;
                }
                catch (Exception exception)
                {
                    return exception;
                }
            }

            private static void Require(bool condition, string message)
            {
                if (!condition)
                {
                    throw new InvalidOperationException(message);
                }
            }
        }
        """";
}