using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query;

public class NorthwindWhereQueryDuckDBTest : NorthwindWhereQueryRelationalTestBase<NorthwindQueryDuckDBFixture<NoopModelCustomizer>>
{
    public NorthwindWhereQueryDuckDBTest(NorthwindQueryDuckDBFixture<NoopModelCustomizer> fixture, ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    public override async Task Where_compare_constructed_equal(bool async)
    {
        await AssertTranslationFailed(() => base.Where_compare_constructed_equal(async));

        AssertSql();
    }

    public override async Task Where_compare_constructed_multi_value_equal(bool async)
    {
        await AssertTranslationFailed(() => base.Where_compare_constructed_multi_value_equal(async));

        AssertSql();
    }

    public override async Task Where_compare_constructed_multi_value_not_equal(bool async)
    {
        await AssertTranslationFailed(() => base.Where_compare_constructed_multi_value_not_equal(async));

        AssertSql();
    }

    public override async Task Where_compare_tuple_constructed_equal(bool async)
    {
        await base.Where_compare_tuple_constructed_equal(async);

        AssertSql(
            """
            SELECT c."CustomerID", c."Address", c."City", c."CompanyName", c."ContactName", c."ContactTitle", c."Country", c."Fax", c."Phone", c."PostalCode", c."Region"
            FROM "Customers" AS c
            WHERE (c."City") = ('London')
            """);
    }

    public override async Task Where_compare_tuple_constructed_multi_value_equal(bool async)
    {
        await base.Where_compare_tuple_constructed_multi_value_equal(async);

        AssertSql(
            """
            SELECT c."CustomerID", c."Address", c."City", c."CompanyName", c."ContactName", c."ContactTitle", c."Country", c."Fax", c."Phone", c."PostalCode", c."Region"
            FROM "Customers" AS c
            WHERE (c."City", c."Country") = ('London', 'UK')
            """);
    }

    public override async Task Where_compare_tuple_constructed_multi_value_not_equal(bool async)
    {
        await base.Where_compare_tuple_constructed_multi_value_not_equal(async);

        AssertSql(
            """
            SELECT c."CustomerID", c."Address", c."City", c."CompanyName", c."ContactName", c."ContactTitle", c."Country", c."Fax", c."Phone", c."PostalCode", c."Region"
            FROM "Customers" AS c
            WHERE c."City" <> 'London' OR c."City" IS NULL OR c."Country" <> 'UK' OR c."Country" IS NULL
            """);
    }

    public override async Task Where_compare_tuple_create_constructed_equal(bool async)
    {
        await AssertTranslationFailed(() => base.Where_compare_tuple_create_constructed_equal(async));

        AssertSql();
    }

    public override async Task Where_compare_tuple_create_constructed_multi_value_equal(bool async)
    {
        await AssertTranslationFailed(() => base.Where_compare_tuple_create_constructed_multi_value_equal(async));

        AssertSql();
    }

    public override async Task Where_compare_tuple_create_constructed_multi_value_not_equal(bool async)
    {
        await AssertTranslationFailed(() => base.Where_compare_tuple_create_constructed_multi_value_not_equal(async));

        AssertSql();
    }

    private void AssertSql(params string[] expected)
        => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);
}