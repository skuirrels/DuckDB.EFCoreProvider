using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Update;

public class DuckDBUpdateSqlGeneratorTest : UpdateSqlGeneratorTestBase
{
    [ConditionalFact]
    public override void AppendDeleteOperation_creates_full_delete_command_text()
    {
        base.AppendDeleteOperation_creates_full_delete_command_text();
    }

    [ConditionalFact]
    public override void AppendDeleteOperation_creates_full_delete_command_text_with_concurrency_check()
    {
        base.AppendDeleteOperation_creates_full_delete_command_text_with_concurrency_check();
    }

    [ConditionalFact]
    public override void AppendInsertOperation_appends_insert_and_select_rowcount_if_no_store_generated_columns_exist_or_conditions_exist()
    {
        var stringBuilder = new StringBuilder();
        var command = CreateInsertCommand(false, false);

        CreateSqlGenerator().AppendInsertOperation(stringBuilder, command, 0);

        AssertSql(
            """
            INSERT INTO dbo."Ducks" ("Id", "Name", "Quacks", "ConcurrencyToken")
            VALUES ($p0, $p1, $p2, $p3);

            """,
            stringBuilder);
    }

    [ConditionalFact]
    public override void AppendInsertOperation_for_all_store_generated_columns()
    {
        base.AppendInsertOperation_for_all_store_generated_columns();
    }

    [ConditionalFact]
    public override void AppendInsertOperation_for_only_identity()
    {
        base.AppendInsertOperation_for_only_identity();
    }

    [ConditionalFact]
    public override void AppendInsertOperation_for_only_single_identity_columns()
    {
        base.AppendInsertOperation_for_only_single_identity_columns();
    }

    [ConditionalFact]
    public override void AppendInsertOperation_for_store_generated_columns_but_no_identity()
    {
        base.AppendInsertOperation_for_store_generated_columns_but_no_identity();
    }

    [ConditionalFact]
    public override void AppendInsertOperation_insert_if_store_generated_columns_exist()
    {
        base.AppendInsertOperation_insert_if_store_generated_columns_exist();
    }

    [ConditionalFact]
    public override void AppendUpdateOperation_appends_where_for_concurrency_token()
    {
        base.AppendUpdateOperation_appends_where_for_concurrency_token();
    }

    [ConditionalFact]
    public override void AppendUpdateOperation_for_computed_property()
    {
        base.AppendUpdateOperation_for_computed_property();
    }

    [ConditionalFact]
    public override void AppendUpdateOperation_if_store_generated_columns_dont_exist()
    {
        base.AppendUpdateOperation_if_store_generated_columns_dont_exist();
    }

    [ConditionalFact]
    public override void AppendUpdateOperation_if_store_generated_columns_exist()
    {
        base.AppendUpdateOperation_if_store_generated_columns_exist();
    }

    [ConditionalFact]
    public override void GenerateNextSequenceValueOperation_correctly_handles_schemas()
    {
        var statement = CreateSqlGenerator().GenerateNextSequenceValueOperation("mysequence", "dbo");

        Assert.Equal("SELECT nextval('\"dbo\".\"mysequence\"')", statement);
    }

    [ConditionalFact]
    public override void GenerateNextSequenceValueOperation_returns_statement_with_sanitized_sequence()
    {
        var statement = CreateSqlGenerator().GenerateNextSequenceValueOperation("sequence'; --", null);

        Assert.Equal("SELECT nextval('\"sequence''; --\"')", statement);
    }

    protected override void AppendDeleteOperation_creates_full_delete_command_text_verification(StringBuilder stringBuilder)
    {
        AssertSql(
            """
            DELETE FROM dbo."Ducks"
            WHERE "Id" = $p0
            RETURNING 1;

            """,
            stringBuilder);
    }

    protected override void AppendDeleteOperation_creates_full_delete_command_text_with_concurrency_check_verification(
        StringBuilder stringBuilder)
    {
        AssertSql(
            """
            DELETE FROM dbo."Ducks"
            WHERE "Id" = $p0 AND "ConcurrencyToken" IS NULL
            RETURNING 1;

            """,
            stringBuilder);
    }

    protected override void AppendInsertOperation_insert_if_store_generated_columns_exist_verification(StringBuilder stringBuilder)
    {
        AssertSql(
            """
            INSERT INTO dbo."Ducks" ("Name", "Quacks", "ConcurrencyToken")
            VALUES ($p0, $p1, $p2)
            RETURNING "Id", "Computed";

            """,
            stringBuilder);
    }

    protected override void AppendInsertOperation_for_store_generated_columns_but_no_identity_verification(StringBuilder stringBuilder)
    {
        AssertSql(
            """
            INSERT INTO dbo."Ducks" ("Id", "Name", "Quacks", "ConcurrencyToken")
            VALUES ($p0, $p1, $p2, $p3)
            RETURNING "Computed";

            """,
            stringBuilder);
    }

    protected override void AppendInsertOperation_for_only_identity_verification(StringBuilder stringBuilder)
    {
        AssertSql(
            """
            INSERT INTO dbo."Ducks" ("Name", "Quacks", "ConcurrencyToken")
            VALUES ($p0, $p1, $p2)
            RETURNING "Id";

            """,
            stringBuilder);
    }

    protected override void AppendInsertOperation_for_all_store_generated_columns_verification(StringBuilder stringBuilder)
    {
        AssertSql(
            """
            INSERT INTO dbo."Ducks"
            DEFAULT VALUES
            RETURNING "Id", "Computed";

            """,
            stringBuilder);
    }

    protected override void AppendInsertOperation_for_only_single_identity_columns_verification(StringBuilder stringBuilder)
    {
        AssertSql(
            """
            INSERT INTO dbo."Ducks"
            DEFAULT VALUES
            RETURNING "Id";

            """,
            stringBuilder);
    }

    protected override void AppendUpdateOperation_if_store_generated_columns_exist_verification(StringBuilder stringBuilder)
    {
        AssertSql(
            """
            UPDATE dbo."Ducks" SET "Name" = $p0, "Quacks" = $p1, "ConcurrencyToken" = $p2
            WHERE "Id" = $p3 AND "ConcurrencyToken" IS NULL
            RETURNING "Computed";

            """,
            stringBuilder);
    }

    protected override void AppendUpdateOperation_if_store_generated_columns_dont_exist_verification(StringBuilder stringBuilder)
    {
        AssertSql(
            """
            UPDATE dbo."Ducks" SET "Name" = $p0, "Quacks" = $p1, "ConcurrencyToken" = $p2
            WHERE "Id" = $p3
            RETURNING 1;

            """,
            stringBuilder);
    }

    protected override void AppendUpdateOperation_appends_where_for_concurrency_token_verification(StringBuilder stringBuilder)
    {
        AssertSql(
            """
            UPDATE dbo."Ducks" SET "Name" = $p0, "Quacks" = $p1, "ConcurrencyToken" = $p2
            WHERE "Id" = $p3 AND "ConcurrencyToken" IS NULL
            RETURNING 1;

            """,
            stringBuilder);
    }

    protected override void AppendUpdateOperation_for_computed_property_verification(StringBuilder stringBuilder)
    {
        AssertSql(
            """
            UPDATE dbo."Ducks" SET "Name" = $p0, "Quacks" = $p1, "ConcurrencyToken" = $p2
            WHERE "Id" = $p3
            RETURNING "Computed";

            """,
            stringBuilder);
    }

    protected override IUpdateSqlGenerator CreateSqlGenerator()
        => TestHelpers.CreateContextServices().GetRequiredService<IUpdateSqlGenerator>();

    protected override string RowsAffected { get; } = "1";

    protected override TestHelpers TestHelpers => DuckDBTestHelpers.Instance;

    private static void AssertSql(string expected, StringBuilder actual)
        => Assert.Equal(
            expected.Replace("\r\n", "\n", StringComparison.Ordinal),
            actual.ToString().Replace("\r\n", "\n", StringComparison.Ordinal));
}
