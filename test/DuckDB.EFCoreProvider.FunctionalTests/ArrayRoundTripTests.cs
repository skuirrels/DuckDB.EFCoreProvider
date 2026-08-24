using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
///     Coverage for array/list column mappings, including the empty-array case (which exercises element-type
///     inference where no element value is available) and nullable elements.
/// </summary>
public class ArrayRoundTripTests : DuckDBTestBase
{
    private ArrayContext CreateContext()
        => new(FileOptions<ArrayContext>());

    [ConditionalFact]
    public void List_round_trips()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.Add(new Bag { Id = 1, Numbers = [1, 2, 3, 42] });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Equal([1, 2, 3, 42], context.Set<Bag>().Single(x => x.Id == 1).Numbers);
        }
    }

    [ConditionalFact]
    public void Array_round_trips()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.Add(new Bag { Id = 1, Words = ["alpha", "beta", "gamma"] });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Equal(["alpha", "beta", "gamma"], context.Set<Bag>().Single(x => x.Id == 1).Words);
        }
    }

    [ConditionalFact]
    public void Empty_collections_round_trip()
    {
        // No element value is present to infer the element type from — confirms the mapping carries enough
        // type information on its own (i.e. an explicit parameter DataTypeName is not required here).
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.Add(new Bag { Id = 1, Numbers = [], Words = [] });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var bag = context.Set<Bag>().Single(x => x.Id == 1);
            Assert.Empty(bag.Numbers);
            Assert.Empty(bag.Words);
        }
    }

    [ConditionalFact]
    public void Large_list_round_trips()
    {
        var values = Enumerable.Range(1, 1000).ToList();

        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.Add(new Bag { Id = 1, Numbers = values });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Equal(values, context.Set<Bag>().Single(x => x.Id == 1).Numbers);
        }
    }

    [ConditionalFact]
    public void Driver_supported_read_only_lists_bind_without_a_defensive_copy()
    {
        using var context = CreateContext();
        context.Database.EnsureCreated();
        context.Database.OpenConnection();

        var property = context.Model.FindEntityType(typeof(Bag))!
            .FindProperty(nameof(Bag.Numbers))!;
        var mapping = property.GetRelationalTypeMapping();
        IReadOnlyList<int> readOnly = new ReadOnlyCollection<int>([1, 2, 3, 4]);

        using (var command = context.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = "SELECT CAST(list_sum($values) AS BIGINT);";
            var parameter = mapping.CreateParameter(command, "values", readOnly);
            Assert.Same(readOnly, parameter.Value);
            command.Parameters.Add(parameter);
            Assert.Equal(10L, Convert.ToInt64(command.ExecuteScalar()));
        }

        Assert.Equal(10L, ExecuteListSum(context, mapping, new[] { 1, 2, 3, 4 }));
        Assert.Equal(10L, ExecuteListSum(context, mapping, new List<int> { 1, 2, 3, 4 }));
        Assert.Equal(10L, ExecuteListSum(context, mapping, ImmutableArray.Create(1, 2, 3, 4)));

        var wrapped = new WrappedReadOnlyList<int>([1, 2, 3, 4]);
        using var wrappedCommand = context.Database.GetDbConnection().CreateCommand();
        wrappedCommand.CommandText = "SELECT CAST(list_sum($values) AS BIGINT);";
        var wrappedParameter = mapping.CreateParameter(wrappedCommand, "values", wrapped);
        Assert.NotSame(wrapped, wrappedParameter.Value);
        Assert.IsType<List<int>>(wrappedParameter.Value);
        wrappedCommand.Parameters.Add(wrappedParameter);
        Assert.Equal(10L, Convert.ToInt64(wrappedCommand.ExecuteScalar()));
    }

    [ConditionalFact]
    public void Read_only_list_parameter_preserves_nullable_elements()
    {
        using var context = CreateContext();
        context.Database.EnsureCreated();
        context.Database.OpenConnection();

        var mapping = context.Model.FindEntityType(typeof(Bag))!
            .FindProperty(nameof(Bag.OptionalNumbers))!
            .GetRelationalTypeMapping();
        IReadOnlyList<int?> values = new ReadOnlyCollection<int?>([1, null, 3]);
        using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT len($values), $values[1], $values[2] IS NULL, $values[3];";
        var parameter = mapping.CreateParameter(command, "values", values);
        Assert.Same(values, parameter.Value);
        command.Parameters.Add(parameter);

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(3L, reader.GetInt64(0));
        Assert.Equal(1, reader.GetInt32(1));
        Assert.True(reader.GetBoolean(2));
        Assert.Equal(3, reader.GetInt32(3));
    }

    private static long ExecuteListSum(
        DbContext context,
        Microsoft.EntityFrameworkCore.Storage.RelationalTypeMapping mapping,
        object values)
    {
        using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT CAST(list_sum($values) AS BIGINT);";
        command.Parameters.Add(mapping.CreateParameter(command, "values", values));
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private sealed class ArrayContext(DbContextOptions<ArrayContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Bag>(entity =>
            {
                entity.Property(e => e.Id).ValueGeneratedNever();
                entity.Property(e => e.Numbers).HasColumnType("INTEGER[]");
                entity.Property(e => e.OptionalNumbers).HasColumnType("INTEGER[]");
                entity.Property(e => e.Words).HasColumnType("VARCHAR[]");
            });
        }
    }

    private sealed class Bag
    {
        public int Id { get; set; }
        public List<int> Numbers { get; set; } = [];
        public List<int?> OptionalNumbers { get; set; } = [];
        public string[] Words { get; set; } = [];
    }

    private sealed class WrappedReadOnlyList<T>(IReadOnlyList<T> values) : IReadOnlyList<T>
    {
        public int Count => values.Count;

        public T this[int index] => values[index];

        public IEnumerator<T> GetEnumerator() => values.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}