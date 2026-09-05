
namespace Microsoft.EntityFrameworkCore.Query.Associations.ComplexJson;

public class ComplexJsonMiscellaneousDuckDBTest : ComplexJsonMiscellaneousRelationalTestBase<ComplexJsonDuckDBFixture>
{
#if NET11_0_OR_GREATER
    [ConditionalFact(Skip = "EF11 upstream #34627: FromSql does not support JSON complex properties.")]
    public override Task FromSql_on_root()
        => base.FromSql_on_root();
#endif

    public ComplexJsonMiscellaneousDuckDBTest(ComplexJsonDuckDBFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture, testOutputHelper)
    {
    }
}
