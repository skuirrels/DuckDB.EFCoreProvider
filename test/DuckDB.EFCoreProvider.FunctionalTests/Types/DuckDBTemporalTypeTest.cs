using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Types.Temporal;

public class DateTimeTypeTest : RelationalTypeTestBase<DateTime, DateTimeTypeTest.DateTimeTypeFixture>
{
    public DateTimeTypeTest(DateTimeTypeFixture fixture) : base(fixture)
    {
    }

    public override async Task ExecuteUpdate_within_json_to_nonjson_column()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => base.ExecuteUpdate_within_json_to_nonjson_column());
        Assert.Equal(RelationalStrings.ExecuteUpdateCannotSetJsonPropertyToNonJsonColumn, exception.Message);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task ExecuteUpdate_within_json_to_another_json_property()
    {
        return base.ExecuteUpdate_within_json_to_another_json_property();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task ExecuteUpdate_within_json_to_constant()
    {
        return base.ExecuteUpdate_within_json_to_constant();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task ExecuteUpdate_within_json_to_parameter()
    {
        return base.ExecuteUpdate_within_json_to_parameter();
    }

    public class DateTimeTypeFixture : RelationalTypeFixtureBase<DateTime>
    {
        public override DateTime Value { get; } = new DateTime(2020, 1, 5, 12, 30, 45, DateTimeKind.Unspecified);
        public override DateTime OtherValue { get; } = new DateTime(2022, 5, 3, 0, 0, 0, DateTimeKind.Unspecified);

        protected override ITestStoreFactory TestStoreFactory => DuckDBTestStoreFactory.Instance;
    }
}

public class DateTimeOffsetTypeTest : RelationalTypeTestBase<DateTimeOffset, DateTimeOffsetTypeTest.DateTimeOffsetTypeFixture>
{
    public DateTimeOffsetTypeTest(DateTimeOffsetTypeFixture fixture) : base(fixture)
    {
    }

    public override async Task ExecuteUpdate_within_json_to_nonjson_column()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => base.ExecuteUpdate_within_json_to_nonjson_column());
        Assert.Equal(RelationalStrings.ExecuteUpdateCannotSetJsonPropertyToNonJsonColumn, exception.Message);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task ExecuteUpdate_within_json_to_another_json_property()
    {
        return base.ExecuteUpdate_within_json_to_another_json_property();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task ExecuteUpdate_within_json_to_constant()
    {
        return base.ExecuteUpdate_within_json_to_constant();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task ExecuteUpdate_within_json_to_parameter()
    {
        return base.ExecuteUpdate_within_json_to_parameter();
    }

    public class DateTimeOffsetTypeFixture : RelationalTypeFixtureBase<DateTimeOffset>
    {
        public override DateTimeOffset Value { get; } = new DateTimeOffset(2020, 1, 5, 12, 30, 45, TimeSpan.FromHours(2));
        public override DateTimeOffset OtherValue { get; } = new DateTimeOffset(2020, 1, 5, 12, 30, 45, TimeSpan.FromHours(3));

        protected override ITestStoreFactory TestStoreFactory => DuckDBTestStoreFactory.Instance;
    }
}

public class DateOnlyTypeTest : RelationalTypeTestBase<DateOnly, DateOnlyTypeTest.DateTypeFixture>
{
    public DateOnlyTypeTest(DateTypeFixture fixture) : base(fixture)
    {
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task ExecuteUpdate_within_json_to_another_json_property()
    {
        return base.ExecuteUpdate_within_json_to_another_json_property();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task ExecuteUpdate_within_json_to_constant()
    {
        return base.ExecuteUpdate_within_json_to_constant();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task ExecuteUpdate_within_json_to_parameter()
    {
        return base.ExecuteUpdate_within_json_to_parameter();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task ExecuteUpdate_within_json_to_nonjson_column()
    {
        return base.ExecuteUpdate_within_json_to_nonjson_column();
    }

    public class DateTypeFixture : RelationalTypeFixtureBase<DateOnly>
    {
        public override DateOnly Value { get; } = new DateOnly(2020, 1, 5);
        public override DateOnly OtherValue { get; } = new DateOnly(2022, 5, 3);

        protected override ITestStoreFactory TestStoreFactory => DuckDBTestStoreFactory.Instance;
    }
}

public class TimeOnlyTypeTest : RelationalTypeTestBase<TimeOnly, TimeOnlyTypeTest.TimeTypeFixture>
{
    public TimeOnlyTypeTest(TimeTypeFixture fixture) : base(fixture)
    {
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Query_property_within_json()
    {
        return base.Query_property_within_json();
    }

    public override async Task ExecuteUpdate_within_json_to_nonjson_column()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => base.ExecuteUpdate_within_json_to_nonjson_column());
        Assert.Equal(RelationalStrings.ExecuteUpdateCannotSetJsonPropertyToNonJsonColumn, exception.Message);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task ExecuteUpdate_within_json_to_another_json_property()
    {
        return base.ExecuteUpdate_within_json_to_another_json_property();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task ExecuteUpdate_within_json_to_constant()
    {
        return base.ExecuteUpdate_within_json_to_constant();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task ExecuteUpdate_within_json_to_parameter()
    {
        return base.ExecuteUpdate_within_json_to_parameter();
    }

    public class TimeTypeFixture : RelationalTypeFixtureBase<TimeOnly>
    {
        public override TimeOnly Value { get; } = new TimeOnly(12, 30, 45);
        public override TimeOnly OtherValue { get; } = new TimeOnly(14, 0, 0);

        protected override ITestStoreFactory TestStoreFactory => DuckDBTestStoreFactory.Instance;
    }
}

public class TimeSpanTypeTest : RelationalTypeTestBase<TimeSpan, TimeSpanTypeTest.TimeSpanTypeFixture>
{
    public TimeSpanTypeTest(TimeSpanTypeFixture fixture) : base(fixture)
    {
    }

    public override async Task ExecuteUpdate_within_json_to_nonjson_column()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => base.ExecuteUpdate_within_json_to_nonjson_column());
        Assert.Equal(RelationalStrings.ExecuteUpdateCannotSetJsonPropertyToNonJsonColumn, exception.Message);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task ExecuteUpdate_within_json_to_another_json_property()
    {
        return base.ExecuteUpdate_within_json_to_another_json_property();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task ExecuteUpdate_within_json_to_constant()
    {
        return base.ExecuteUpdate_within_json_to_constant();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task ExecuteUpdate_within_json_to_parameter()
    {
        return base.ExecuteUpdate_within_json_to_parameter();
    }

    public class TimeSpanTypeFixture : RelationalTypeFixtureBase<TimeSpan>
    {
        public override TimeSpan Value { get; } = new TimeSpan(12, 30, 45);
        public override TimeSpan OtherValue { get; } = new TimeSpan(14, 0, 0);

        protected override ITestStoreFactory TestStoreFactory => DuckDBTestStoreFactory.Instance;
    }
}
