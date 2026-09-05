using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Types.Numeric;

public class ByteTypeTest : RelationalTypeTestBase<byte, ByteTypeTest.ByteTypeFixture>
{
#if NET11_0_OR_GREATER
    public ByteTypeTest(ByteTypeFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture, testOutputHelper)
#else
    public ByteTypeTest(ByteTypeFixture fixture) : base(fixture)
#endif
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
    public override Task ExecuteUpdate_within_json_to_nonjson_column()
    {
        return base.ExecuteUpdate_within_json_to_nonjson_column();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task ExecuteUpdate_within_json_to_parameter()
    {
        return base.ExecuteUpdate_within_json_to_parameter();
    }

    public class ByteTypeFixture : RelationalTypeFixtureBase<byte>
    {
        public override byte Value { get; } = byte.MinValue;
        public override byte OtherValue { get; } = byte.MaxValue;

        protected override ITestStoreFactory TestStoreFactory => DuckDBTestStoreFactory.Instance;
    }
}

public class ShortTypeTest : RelationalTypeTestBase<short, ShortTypeTest.ShortTypeFixture>
{
#if NET11_0_OR_GREATER
    public ShortTypeTest(ShortTypeFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture, testOutputHelper)
#else
    public ShortTypeTest(ShortTypeFixture fixture) : base(fixture)
#endif
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
    public override Task ExecuteUpdate_within_json_to_nonjson_column()
    {
        return base.ExecuteUpdate_within_json_to_nonjson_column();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task ExecuteUpdate_within_json_to_parameter()
    {
        return base.ExecuteUpdate_within_json_to_parameter();
    }

    public class ShortTypeFixture : RelationalTypeFixtureBase<short>
    {
        public override short Value { get; } = short.MinValue;
        public override short OtherValue { get; } = short.MaxValue;

        protected override ITestStoreFactory TestStoreFactory => DuckDBTestStoreFactory.Instance;
    }
}

public class IntTypeTest : RelationalTypeTestBase<int, IntTypeTest.IntTypeFixture>
{
#if NET11_0_OR_GREATER
    public IntTypeTest(IntTypeFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture, testOutputHelper)
#else
    public IntTypeTest(IntTypeFixture fixture) : base(fixture)
#endif
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
    public override Task ExecuteUpdate_within_json_to_nonjson_column()
    {
        return base.ExecuteUpdate_within_json_to_nonjson_column();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task ExecuteUpdate_within_json_to_parameter()
    {
        return base.ExecuteUpdate_within_json_to_parameter();
    }

    public class IntTypeFixture : RelationalTypeFixtureBase<int>
    {
        public override int Value { get; } = int.MinValue;
        public override int OtherValue { get; } = int.MaxValue;

        protected override ITestStoreFactory TestStoreFactory => DuckDBTestStoreFactory.Instance;
    }
}

public class LongTypeTest : RelationalTypeTestBase<long, LongTypeTest.LongTypeFixture>
{
#if NET11_0_OR_GREATER
    public LongTypeTest(LongTypeFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture, testOutputHelper)
#else
    public LongTypeTest(LongTypeFixture fixture) : base(fixture)
#endif
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
    public override Task ExecuteUpdate_within_json_to_nonjson_column()
    {
        return base.ExecuteUpdate_within_json_to_nonjson_column();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task ExecuteUpdate_within_json_to_parameter()
    {
        return base.ExecuteUpdate_within_json_to_parameter();
    }

    public class LongTypeFixture : RelationalTypeFixtureBase<long>
    {
        public override long Value { get; } = long.MinValue;
        public override long OtherValue { get; } = long.MaxValue;

        protected override ITestStoreFactory TestStoreFactory => DuckDBTestStoreFactory.Instance;
    }
}

public class DecimalTypeTest : RelationalTypeTestBase<decimal, DecimalTypeTest.DecimalTypeFixture>
{
#if NET11_0_OR_GREATER
    public DecimalTypeTest(DecimalTypeFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture, testOutputHelper)
#else
    public DecimalTypeTest(DecimalTypeFixture fixture) : base(fixture)
#endif
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
    public override Task ExecuteUpdate_within_json_to_nonjson_column()
    {
        return base.ExecuteUpdate_within_json_to_nonjson_column();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task ExecuteUpdate_within_json_to_parameter()
    {
        return base.ExecuteUpdate_within_json_to_parameter();
    }

    public class DecimalTypeFixture : RelationalTypeFixtureBase<decimal>
    {
        public override decimal Value { get; } = 30.5m;
        public override decimal OtherValue { get; } = 30m;

        protected override ITestStoreFactory TestStoreFactory => DuckDBTestStoreFactory.Instance;
    }
}

public class DoubleTypeTest : RelationalTypeTestBase<double, DoubleTypeTest.DoubleTypeFixture>
{
#if NET11_0_OR_GREATER
    public DoubleTypeTest(DoubleTypeFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture, testOutputHelper)
#else
    public DoubleTypeTest(DoubleTypeFixture fixture) : base(fixture)
#endif
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
    public override Task ExecuteUpdate_within_json_to_nonjson_column()
    {
        return base.ExecuteUpdate_within_json_to_nonjson_column();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task ExecuteUpdate_within_json_to_parameter()
    {
        return base.ExecuteUpdate_within_json_to_parameter();
    }

    public class DoubleTypeFixture : RelationalTypeFixtureBase<double>
    {
        public override double Value { get; } = 30.5d;
        public override double OtherValue { get; } = 30d;

        protected override ITestStoreFactory TestStoreFactory => DuckDBTestStoreFactory.Instance;
    }
}

public class FloatTypeTest : RelationalTypeTestBase<float, FloatTypeTest.FloatTypeFixture>
{
#if NET11_0_OR_GREATER
    public FloatTypeTest(FloatTypeFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture, testOutputHelper)
#else
    public FloatTypeTest(FloatTypeFixture fixture) : base(fixture)
#endif
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
    public override Task ExecuteUpdate_within_json_to_nonjson_column()
    {
        return base.ExecuteUpdate_within_json_to_nonjson_column();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task ExecuteUpdate_within_json_to_parameter()
    {
        return base.ExecuteUpdate_within_json_to_parameter();
    }

    public class FloatTypeFixture : RelationalTypeFixtureBase<float>
    {
        public override float Value { get; } = 30.5f;
        public override float OtherValue { get; } = 30f;

        protected override ITestStoreFactory TestStoreFactory => DuckDBTestStoreFactory.Instance;
    }
}
