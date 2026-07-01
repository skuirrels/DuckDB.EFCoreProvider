using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore.TestUtilities;
using System.Data.Common;

namespace Microsoft.EntityFrameworkCore.Query;

public class NorthwindSqlQueryDuckDBTest : NorthwindSqlQueryTestBase<NorthwindQueryDuckDBFixture<NoopModelCustomizer>>
{
    public NorthwindSqlQueryDuckDBTest(NorthwindQueryDuckDBFixture<NoopModelCustomizer> fixture) : base(fixture)
    {
    }

    protected override DbParameter CreateDbParameter(string name, object value)
    {
        return new DuckDBParameter
        {
            ParameterName = name.StartsWith('$') || name.StartsWith('@')
                ? name.Substring(1)
                : name,
            Value = value
        };
    }
}
