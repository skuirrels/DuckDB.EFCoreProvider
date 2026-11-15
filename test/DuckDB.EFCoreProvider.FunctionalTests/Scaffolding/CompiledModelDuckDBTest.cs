using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Scaffolding;

public partial class CompiledModelDuckDBTest : CompiledModelRelationalTestBase
{
    public CompiledModelDuckDBTest(NonSharedFixture fixture) : base(fixture)
    {
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task BigModel_with_JSON_columns()
    {
        return base.BigModel_with_JSON_columns();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task CheckConstraints()
    {
        return base.CheckConstraints();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task ComplexTypes()
    {
        return base.ComplexTypes();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Custom_function_parameter_type_mapping()
    {
        return base.Custom_function_parameter_type_mapping();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Custom_function_type_mapping()
    {
        return base.Custom_function_type_mapping();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task DbFunctions()
    {
        return base.DbFunctions();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Dynamic_schema()
    {
        return base.Dynamic_schema();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Tpc_Sprocs()
    {
        return base.Tpc_Sprocs();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Triggers()
    {
        return base.Triggers();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task BigModel()
    {
        return base.BigModel();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task No_NativeAOT()
    {
        return base.No_NativeAOT();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task SimpleModel()
    {
        return base.SimpleModel();
    }

#if NET11_0_OR_GREATER
    // The provider does not add EF's automatic foreign key indexes, so the scenario declares the index it needs.
    public override Task Owned_entity_sharing_table_with_owner_that_has_an_index()
        => Test(
            modelBuilder => modelBuilder.Entity<Post>(
                eb =>
                {
                    eb.HasOne<Blog>().WithMany(b => b.Posts).IsRequired();
                    eb.HasIndex("BlogId");
                    eb.OwnsOne(p => p.Author);
                }),
            model => Assert.Contains(model.GetRelationalModel().Tables, t => t.Indexes.Any()));

#endif
    protected override TestHelpers TestHelpers
        => DuckDBTestHelpers.Instance;

    protected override BuildSource AddReferences(BuildSource build, [CallerFilePath] string filePath = "")
    {
        base.AddReferences(build, filePath);
        build.References.Add(BuildReference.ByName("DuckDB.EFCoreProvider"));
        return build;
    }

#if NET11_0_OR_GREATER
    protected override ITestStoreFactory NonSharedTestStoreFactory
#else
    protected override ITestStoreFactory TestStoreFactory
#endif
        => DuckDBTestStoreFactory.Instance;
}