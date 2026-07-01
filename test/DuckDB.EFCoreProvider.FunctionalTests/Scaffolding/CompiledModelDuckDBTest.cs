using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Scaffolding;

public class CompiledModelDuckDBTest : CompiledModelRelationalTestBase
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

    protected override TestHelpers TestHelpers
        => DuckDBTestHelpers.Instance;

    protected override ITestStoreFactory TestStoreFactory
        => DuckDBTestStoreFactory.Instance;
}
