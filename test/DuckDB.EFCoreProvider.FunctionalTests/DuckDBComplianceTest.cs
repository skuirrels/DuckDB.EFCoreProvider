using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Update;
using System.Reflection;

namespace Microsoft.EntityFrameworkCore;

public class DuckDBComplianceTest : RelationalComplianceTestBase
{
    protected override ICollection<Type> IgnoredTestBases { get; } = new HashSet<Type>
    {
        typeof(FromSqlSprocQueryTestBase<>),
        typeof(SqlExecutorTestBase<>),
        typeof(UdfDbFunctionTestBase<>),
        typeof(StoredProcedureUpdateTestBase), // DuckDB does not support stored procedures
        typeof(AdHocMiscellaneousQueryRelationalTestBase), // TODO  Some tests are non-virtual, could not override
        typeof(ManyToManyTrackingRelationalTestBase<>), // TODO  Some tests are non-virtual, could not override
        typeof(MonsterFixupTestBase<>), // TODO  Some tests are non-virtual, could not override
        typeof(PropertyValuesTestBase<>), // TODO Some tests are non-virtual, could not override
        typeof(AdHocMiscellaneousQueryTestBase), // TODO  Some tests are non-virtual, could not override
        typeof(StoreGeneratedTestBase<>), // TODO  Some tests are non-virtual, could not override
        typeof(UpdatesTestBase<>), // TODO  Some tests are non-virtual, could not override
        typeof(PropertyValuesRelationalTestBase<>), // TODO  Some tests are non-virtual, could not override
        typeof(ManyToManyTrackingTestBase<>), // TODO  Some tests are non-virtual, could not override
    };

    protected override Assembly TargetAssembly { get; } = typeof(DuckDBComplianceTest).Assembly;
}
