using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query;

public class PrimitiveCollectionsQueryDuckDBTest : PrimitiveCollectionsQueryRelationalTestBase<PrimitiveCollectionsQueryDuckDBTest.PrimitiveCollectionsQueryDuckDBFixture>
{
    public PrimitiveCollectionsQueryDuckDBTest(PrimitiveCollectionsQueryDuckDBFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    public override async Task Column_collection_Any()
    {
        await base.Column_collection_Any();

        AssertSql(
            """
            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE array_length(p."Ints") > 0
            """);
    }

    public override async Task Column_collection_Contains_over_subquery()
    {
        await base.Column_collection_Contains_over_subquery();

        AssertSql(
            """
            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE 11 IN (
                SELECT i."value"
                FROM unnest(p."Ints") AS i("value")
                WHERE i."value" > 1
            )
            """);
    }

    public override async Task Column_collection_Count_method()
    {
        await base.Column_collection_Count_method();

        AssertSql(
            """
            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE array_length(p."Ints") = 2
            """);
    }

    public override async Task Column_collection_Count_with_predicate()
    {
        await base.Column_collection_Count_with_predicate();

        // TODO array_length(array_filter(p.Ints, i => i > 1))
        AssertSql(
            """
            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE (
                SELECT COUNT(*)
                FROM unnest(p."Ints") AS i("value")
                WHERE i."value" > 1) = 2
            """);
    }

    public override async Task Column_collection_Distinct()
    {
        await base.Column_collection_Distinct();

        AssertSql(
            """
            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE array_length(list_distinct(p."Ints")) = 3
            """);
    }

    public override async Task Column_collection_ElementAt()
    {
        await base.Column_collection_ElementAt();

        AssertSql(
            """
            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE p."Ints"[2] = 10
            """);
    }

    public override async Task Column_collection_First()
    {
        await base.Column_collection_First();

        AssertSql(
            """
            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE (
                SELECT i."value"
                FROM unnest(p."Ints") AS i("value")
                LIMIT 1) = 1
            """);
    }

    public override async Task Column_collection_FirstOrDefault()
    {
        await base.Column_collection_FirstOrDefault();

        AssertSql(
            """
            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE COALESCE((
                SELECT i."value"
                FROM unnest(p."Ints") AS i("value")
                LIMIT 1), 0) = 1
            """);
    }

    public override async Task Column_collection_index_beyond_end()
    {
        await base.Column_collection_index_beyond_end();

        AssertSql(
            """
            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE p."Ints"[1000] = 10
            """);
    }

    public override async Task Column_collection_index_datetime()
    {
        await base.Column_collection_index_datetime();

        AssertSql(
            """
            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE p."DateTimes"[2] = TIMESTAMP '2020-01-10 12:30:00.000000'
            """);
    }

    public override async Task Column_collection_index_int()
    {
        await base.Column_collection_index_int();

        AssertSql(
            """
            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE p."Ints"[2] = 10
            """);
    }

    public override async Task Column_collection_index_string()
    {
        await base.Column_collection_index_string();

        AssertSql(
            """
            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE p."Strings"[2] = '10'
            """);
    }

    public override async Task Column_collection_Intersect_inline_collection()
    {
        await base.Column_collection_Intersect_inline_collection();

        AssertSql(
            """
            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE (
                SELECT COUNT(*)
                FROM (
                    SELECT i."value"
                    FROM unnest(p."Ints") AS i("value")
                    INTERSECT
                    VALUES (CAST(11 AS INTEGER)), (111)
                ) AS i0) = 2
            """);
    }

    public override async Task Column_collection_Length()
    {
        await base.Column_collection_Length();

        AssertSql(
            """
            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE array_length(p."Ints") = 2
            """);
    }

    public override Task Column_collection_Join_parameter_collection()
    {
        return base.Column_collection_Join_parameter_collection();
    }

    public override async Task Column_collection_of_bools_Contains()
    {
        await base.Column_collection_of_bools_Contains();

        AssertSql(
            """
            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE array_contains(p."Bools", true)
            """);
    }

    public override async Task Column_collection_of_ints_Contains()
    {
        await base.Column_collection_of_ints_Contains();

        AssertSql(
            """
            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE array_contains(p."Ints", 10)
            """);
    }

    public override async Task Column_collection_of_nullable_ints_Contains()
    {
        await base.Column_collection_of_nullable_ints_Contains();

        // TODO array_contains
        AssertSql(
            """
            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE 10 IN (
                SELECT n."value"
                FROM unnest(p."NullableInts") AS n("value")
            )
            """);
    }

    public override async Task Column_collection_of_nullable_ints_Contains_null()
    {
        await base.Column_collection_of_nullable_ints_Contains_null();

        // TODO array_contains
        AssertSql(
            """
            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE EXISTS (
                SELECT 1
                FROM unnest(p."NullableInts") AS n("value")
                WHERE n."value" IS NULL)
            """);
    }

    public override async Task Column_collection_of_nullable_strings_contains_null()
    {
        await base.Column_collection_of_nullable_strings_contains_null();

        AssertSql(
            """
            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE EXISTS (
                SELECT 1
                FROM unnest(p."NullableStrings") AS n("value")
                WHERE n."value" IS NULL)
            """);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Column_collection_of_strings_contains_null()
    {
        await base.Column_collection_of_strings_contains_null();

        AssertSql(
            """
            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE array_position(p."Strings", NULL) IS NOT NULL
            """);
    }

    public override async Task Column_collection_OrderByDescending_ElementAt()
    {
        await base.Column_collection_OrderByDescending_ElementAt();

        AssertSql(
            """
            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE (
                SELECT i."value"
                FROM unnest(p."Ints") AS i("value")
                ORDER BY i."value" DESC NULLS LAST
                LIMIT 1 OFFSET 0) = 111
            """);
    }

    public override async Task Column_collection_SelectMany()
    {
        await base.Column_collection_SelectMany();

        AssertSql(
            """
            SELECT i."value"
            FROM "PrimitiveCollectionsEntity" AS p
            CROSS JOIN LATERAL unnest(p."Ints") AS i("value")
            """);
    }

    public override async Task Column_collection_SelectMany_with_filter()
    {
        await base.Column_collection_SelectMany_with_filter();

        AssertSql(
            """
            SELECT i0."value"
            FROM "PrimitiveCollectionsEntity" AS p
            CROSS JOIN LATERAL (
                SELECT i."value"
                FROM unnest(p."Ints") AS i("value")
                WHERE i."value" > 1
            ) AS i0
            """);
    }

    public override async Task Column_collection_SelectMany_with_Select_to_anonymous_type()
    {
        await base.Column_collection_SelectMany_with_Select_to_anonymous_type();

        AssertSql(
            """
            SELECT i."value" AS "Original", i."value" + 1 AS "Incremented"
            FROM "PrimitiveCollectionsEntity" AS p
            CROSS JOIN LATERAL unnest(p."Ints") AS i("value")
            """);
    }

    public override async Task Column_collection_Single()
    {
        await base.Column_collection_Single();

        AssertSql(
            """
            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE (
                SELECT i."value"
                FROM unnest(p."Ints") AS i("value")
                LIMIT 1) = 1
            """);
    }

    public override async Task Column_collection_SingleOrDefault()
    {
        await base.Column_collection_SingleOrDefault();

        AssertSql(
            """
            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE COALESCE((
                SELECT i."value"
                FROM unnest(p."Ints") AS i("value")
                LIMIT 1), 0) = 1
            """);
    }

    public override async Task Column_collection_Skip()
    {
        await base.Column_collection_Skip();

        AssertSql(
            """
            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE array_length(p."Ints"[2:]) = 2
            """);
    }

    public override async Task Column_collection_Skip_Take()
    {
        await base.Column_collection_Skip_Take();

        AssertSql(
            """
            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE array_contains(p."Ints"[2:3], 11)
            """);
    }

    public override async Task Column_collection_Take()
    {
        await base.Column_collection_Take();

        AssertSql(
            """
            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE array_contains(p."Ints"[:2], 11)
            """);
    }

    public override async Task Column_collection_Union_parameter_collection()
    {
        await base.Column_collection_Union_parameter_collection();

        AssertSql(
            """
            ints1='11'
            ints2='111'

            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE (
                SELECT COUNT(*)
                FROM (
                    SELECT i."value"
                    FROM unnest(p."Ints") AS i("value")
                    UNION
                    VALUES ($ints1), ($ints2)
                ) AS u) = 2
            """);
    }

    public override async Task Column_collection_Where_Count()
    {
        await base.Column_collection_Where_Count();

        AssertSql(
            """
            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE (
                SELECT COUNT(*)
                FROM unnest(p."Ints") AS i("value")
                WHERE i."value" > 1) = 2
            """);
    }

    public override async Task Column_collection_Where_ElementAt()
    {
        await base.Column_collection_Where_ElementAt();

        AssertSql(
            """
            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE (
                SELECT i."value"
                FROM unnest(p."Ints") AS i("value")
                WHERE i."value" > 1
                LIMIT 1 OFFSET 0) = 11
            """);
    }

    public override async Task Column_collection_Where_Skip()
    {
        await base.Column_collection_Where_Skip();

        AssertSql(
            """
            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE (
                SELECT COUNT(*)
                FROM (
                    SELECT 1
                    FROM unnest(p."Ints") AS i("value")
                    WHERE i."value" > 1
                    OFFSET 1
                ) AS i0) = 3
            """);
    }

    public override async Task Column_collection_Where_Skip_Take()
    {
        await base.Column_collection_Where_Skip_Take();

        AssertSql(
            """
            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE (
                SELECT COUNT(*)
                FROM (
                    SELECT 1
                    FROM unnest(p."Ints") AS i("value")
                    WHERE i."value" > 1
                    LIMIT 2 OFFSET 1
                ) AS i0) = 1
            """);
    }

    public override async Task Column_collection_Where_Take()
    {
        await base.Column_collection_Where_Take();

        AssertSql(
            """
            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE (
                SELECT COUNT(*)
                FROM (
                    SELECT 1
                    FROM unnest(p."Ints") AS i("value")
                    WHERE i."value" > 1
                    LIMIT 2
                ) AS i0) = 2
            """);
    }

    public override async Task Column_collection_Where_Union()
    {
        await base.Column_collection_Where_Union();

        AssertSql(
            """
            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE (
                SELECT COUNT(*)
                FROM (
                    SELECT i."value"
                    FROM unnest(p."Ints") AS i("value")
                    WHERE i."value" > 100
                    UNION
                    VALUES (CAST(50 AS INTEGER))
                ) AS u) = 2
            """);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Inline_collection_Contains_with_EF_Parameter()
    {
        await base.Inline_collection_Contains_with_EF_Parameter();

        AssertSql(
            """
            @p={ '2'
            '999'
            '1000' } (DbType = Object)

            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE p."Id" = ANY (@p)
            """);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Inline_collection_Contains_with_IEnumerable_EF_Parameter()
    {
        await base.Inline_collection_Contains_with_IEnumerable_EF_Parameter();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Inline_collection_Count_with_column_predicate_with_EF_Parameter()
    {
        await base.Inline_collection_Count_with_column_predicate_with_EF_Parameter();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Inline_collection_Except_column_collection()
    {
        await base.Inline_collection_Except_column_collection();
    }

    public override async Task Inline_collection_index_Column()
    {
        await base.Inline_collection_index_Column();

        AssertSql(
            """
            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE list_value(1, 2, 3)[p."Int" + 1] = 1
            """);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Inline_collection_index_Column_with_EF_Constant()
    {
        await base.Inline_collection_index_Column_with_EF_Constant();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Inline_collection_Join_ordered_column_collection()
    {
        await base.Inline_collection_Join_ordered_column_collection();
    }

    public override async Task Non_nullable_reference_column_collection_index_equals_nullable_column()
    {
        await base.Non_nullable_reference_column_collection_index_equals_nullable_column();

        AssertSql(
            """
            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE array_length(p."Strings") > 0 AND p."Strings"[2] = p."NullableString"
            """);
    }

    public override async Task Nullable_reference_column_collection_index_equals_nullable_column()
    {
        await base.Nullable_reference_column_collection_index_equals_nullable_column();

        AssertSql(
            """
            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE p."NullableStrings"[3] = p."NullableString" OR (p."NullableStrings"[3] IS NULL AND p."NullableString" IS NULL)
            """);
    }

    public override async Task Parameter_collection_Concat_column_collection()
    {
        await base.Parameter_collection_Concat_column_collection();

        AssertSql(
            """
            p1='11'
            p2='111'

            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE (
                SELECT COUNT(*)
                FROM (
                    SELECT 1
                    FROM (VALUES ($p1), ($p2)) AS p0("Value")
                    UNION ALL
                    SELECT 1
                    FROM unnest(p."Ints") AS i("value")
                ) AS u) = 2
            """);
    }

    public override async Task Parameter_collection_in_subquery_Union_column_collection()
    {
        await base.Parameter_collection_in_subquery_Union_column_collection();

        AssertSql(
            """
            Skip1='111'

            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE (
                SELECT COUNT(*)
                FROM (
                    VALUES ($Skip1)
                    UNION
                    SELECT i."value" AS "Value"
                    FROM unnest(p."Ints") AS i("value")
                ) AS u) = 3
            """);
    }

    public override async Task Parameter_collection_in_subquery_Union_column_collection_nested()
    {
        await base.Parameter_collection_in_subquery_Union_column_collection_nested();

        AssertSql(
            """
            Skip1='111'

            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE (
                SELECT COUNT(*)
                FROM (
                    VALUES ($Skip1)
                    UNION
                    SELECT i2."value" AS "Value"
                    FROM (
                        SELECT i1."value"
                        FROM (
                            SELECT DISTINCT i0."value"
                            FROM (
                                SELECT i."value"
                                FROM unnest(p."Ints") AS i("value")
                                ORDER BY i."value" NULLS FIRST
                                OFFSET 1
                            ) AS i0
                        ) AS i1
                        ORDER BY i1."value" DESC NULLS LAST
                        LIMIT 20
                    ) AS i2
                ) AS u) = 3
            """);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Parameter_collection_index_Column_equal_Column()
    {
        await base.Parameter_collection_index_Column_equal_Column();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Parameter_collection_index_Column_equal_constant()
    {
        await base.Parameter_collection_index_Column_equal_constant();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Parameter_collection_with_type_inference_for_JsonScalarExpression()
    {
        await base.Parameter_collection_with_type_inference_for_JsonScalarExpression();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Project_collection_of_datetimes_filtered()
    {
        await base.Project_collection_of_datetimes_filtered();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Project_collection_of_ints_ordered()
    {
        await base.Project_collection_of_ints_ordered();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Project_collection_of_nullable_ints_with_paging2()
    {
        await base.Project_collection_of_nullable_ints_with_paging2();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Project_empty_collection_of_nullables_and_collection_only_containing_nulls()
    {
        await base.Project_empty_collection_of_nullables_and_collection_only_containing_nulls();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Project_inline_collection_with_Union()
    {
        await base.Project_inline_collection_with_Union();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Project_multiple_collections()
    {
        await base.Project_multiple_collections();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Parameter_collection_of_nullable_structs_Contains_nullable_struct_with_nullable_comparer()
    {
        await base.Parameter_collection_of_nullable_structs_Contains_nullable_struct_with_nullable_comparer();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Parameter_collection_of_structs_Contains_nullable_struct_with_nullable_comparer()
    {
        await base.Parameter_collection_of_structs_Contains_nullable_struct_with_nullable_comparer();
    }

    public override async Task Parameter_collection_in_subquery_Union_column_collection_as_compiled_query()
    {
        await base.Parameter_collection_in_subquery_Union_column_collection_as_compiled_query();

        AssertSql(
            """
            ints1='10'
            ints2='111'

            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE (
                SELECT COUNT(*)
                FROM (
                    SELECT i1."Value"
                    FROM (
                        SELECT i."Value"
                        FROM (VALUES (0, $ints1), (1, $ints2)) AS i(_ord, "Value")
                        ORDER BY i._ord NULLS FIRST
                        OFFSET 1
                    ) AS i1
                    UNION
                    SELECT i0."value" AS "Value"
                    FROM unnest(p."Ints") AS i0("value")
                ) AS u) = 3
            """);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Project_collection_of_ints_with_ToList_and_FirstOrDefault()
    {
        return base.Project_collection_of_ints_with_ToList_and_FirstOrDefault();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Project_collection_of_nullable_ints_with_paging()
    {
        return base.Project_collection_of_nullable_ints_with_paging();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Project_collection_of_nullable_ints_with_paging3()
    {
        return base.Project_collection_of_nullable_ints_with_paging3();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Project_collection_of_ints_with_distinct()
    {
        return base.Project_collection_of_ints_with_distinct();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Parameter_collection_of_nullable_ints_Contains_nullable_int_with_EF_Parameter()
    {
        return base.Parameter_collection_of_nullable_ints_Contains_nullable_int_with_EF_Parameter();
    }

    [ConditionalFact]
    public virtual async Task Column_collection_Append()
    {
        await AssertQuery(
            ss => ss.Set<PrimitiveCollectionsEntity>().Where(c => c.Ints.Append(3).Count() == 3),
            ss => ss.Set<PrimitiveCollectionsEntity>().Where(c => c.Ints.Length == 2));

        AssertSql(
            """
            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE array_length(array_push_back(p."Ints", 3)) = 3
            """);
    }

    [ConditionalFact]
    public virtual async Task Column_collection_Prepend()
    {
        await AssertQuery(
            ss => ss.Set<PrimitiveCollectionsEntity>().Where(c => c.Ints.Prepend(3).Count() == 3),
            ss => ss.Set<PrimitiveCollectionsEntity>().Where(c => c.Ints.Length == 2));

        AssertSql(
            """
            SELECT p."Id", p."Bool", p."Bools", p."DateTime", p."DateTimes", p."Enum", p."Enums", p."Int", p."Ints", p."NullableInt", p."NullableInts", p."NullableString", p."NullableStrings", p."NullableWrappedId", p."NullableWrappedIdWithNullableComparer", p."String", p."Strings", p."WrappedId"
            FROM "PrimitiveCollectionsEntity" AS p
            WHERE array_length(array_push_front(p."Ints", 3)) = 3
            """);
    }

    private void AssertSql(params string[] expected)
        => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);

    public class PrimitiveCollectionsQueryDuckDBFixture : PrimitiveCollectionsQueryFixtureBase, ITestSqlLoggerFactory
    {
        public TestSqlLoggerFactory TestSqlLoggerFactory
            => (TestSqlLoggerFactory)ListLoggerFactory;

        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;
    }
}