using AwesomeAssertions;
using Microsoft.EntityFrameworkCore.TestModels.BasicTypesModel;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Query.Translations;

public class BasicTypesQueryDuckDBFixture : BasicTypesQueryFixtureBase, ITestSqlLoggerFactory
{
    public BasicTypesQueryDuckDBFixture()
    {
        var entityAsserters = (Dictionary<Type, object>)EntityAsserters;
        entityAsserters.Clear();

        entityAsserters.Add(typeof(BasicTypesEntity), (object? e, object? a) =>
        {
            Assert.Equal(e == null, a == null);

            if (a != null)
            {
                var ee = (BasicTypesEntity)e!;
                var aa = (BasicTypesEntity)a;

                Assert.Equal(ee.Id, aa.Id);

                Assert.Equal(ee.Byte, aa.Byte);
                Assert.Equal(ee.Short, aa.Short);
                Assert.Equal(ee.Int, aa.Int);
                Assert.Equal(ee.Long, aa.Long);
                Assert.Equal(ee.Float, aa.Float);
                Assert.Equal(ee.Double, aa.Double);
                Assert.Equal(ee.Decimal, aa.Decimal);

                Assert.Equal(ee.String, aa.String);

                aa.DateTime.Should().BeCloseTo(ee.DateTime, TimeSpan.FromMilliseconds(100));
                Assert.Equal(ee.DateOnly, aa.DateOnly);
                ee.TimeOnly.Should().BeCloseTo(aa.TimeOnly, TimeSpan.FromMicroseconds(1)); // TODO Wait for TIME_NS support!
                // Assert.Equal(ee.DateTimeOffset, aa.DateTimeOffset);
                // Assert.Equal(ee.TimeSpan, aa.TimeSpan);
                //
                // Assert.Equal(ee.Bool, aa.Bool);
                // Assert.Equal(ee.Guid, aa.Guid);
                // Assert.Equivalent(ee.ByteArray, aa.ByteArray);
                //
                // Assert.Equal(ee.Enum, aa.Enum);
                // Assert.Equal(ee.FlagsEnum, aa.FlagsEnum);
            }
        });

        entityAsserters.Add(typeof(NullableBasicTypesEntity), (object? e, object? a) =>
        {
            Assert.Equal(e == null, a == null);

            if (a != null)
            {
                var ee = (NullableBasicTypesEntity)e!;
                var aa = (NullableBasicTypesEntity)a;

                Assert.Equal(ee.Id, aa.Id);

                Assert.Equal(ee.Byte, aa.Byte);
                Assert.Equal(ee.Short, aa.Short);
                Assert.Equal(ee.Int, aa.Int);
                Assert.Equal(ee.Long, aa.Long);
                Assert.Equal(ee.Float, aa.Float);
                Assert.Equal(ee.Double, aa.Double);
                Assert.Equal(ee.Decimal, aa.Decimal);

                Assert.Equal(ee.String, aa.String);

                if (ee.DateTime.HasValue)
                {
                    aa.DateTime.Should().BeCloseTo(ee.DateTime.Value, TimeSpan.FromMilliseconds(100));
                }
                else
                {
                    aa.DateTime.Should().BeNull();
                }

                Assert.Equal(ee.DateOnly, aa.DateOnly);

                if (ee.TimeOnly.HasValue)
                {
                    // TODO Wait for TIME_NS support!
                    aa.TimeOnly!.Value.Should().BeCloseTo(ee.TimeOnly.Value, TimeSpan.FromTicks(4));
                }
                else
                {
                    aa.TimeOnly.Should().BeNull();
                }

                if (ee.DateTimeOffset.HasValue)
                {
                    aa.DateTimeOffset.Should().NotBeNull();
                    aa.DateTimeOffset.Value.Should().BeCloseTo(ee.DateTimeOffset.Value, TimeSpan.FromMicroseconds(100));
                }
                else
                {
                    aa.DateTimeOffset.Should().BeNull();
                }

                if (ee.TimeSpan.HasValue)
                {
                    aa.TimeSpan.Should().NotBeNull();
                    aa.TimeSpan.Value.Should().BeCloseTo(ee.TimeSpan.Value, TimeSpan.FromMilliseconds(1));
                }
                else
                {
                    aa.TimeSpan.Should().BeNull();
                }

                Assert.Equal(ee.Bool, aa.Bool);
                Assert.Equal(ee.Guid, aa.Guid);
                Assert.Equivalent(ee.ByteArray, aa.ByteArray);

                Assert.Equal(ee.Enum, aa.Enum);
                Assert.Equal(ee.FlagsEnum, aa.FlagsEnum);
            }
        });
    }

    protected override ITestStoreFactory TestStoreFactory
        => DuckDBTestStoreFactory.Instance;

    public TestSqlLoggerFactory TestSqlLoggerFactory
        => (TestSqlLoggerFactory)ListLoggerFactory;
}
