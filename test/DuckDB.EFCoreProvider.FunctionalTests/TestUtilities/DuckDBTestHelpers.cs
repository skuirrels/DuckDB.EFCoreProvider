using DuckDB.EFCoreProvider.Diagnostics.Internal;
using DuckDB.EFCoreProvider.Extensions;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.EntityFrameworkCore.TestUtilities;

public class DuckDBTestHelpers : RelationalTestHelpers
{
    protected DuckDBTestHelpers()
    {
    }

    public static DuckDBTestHelpers Instance { get; } = new();

    public override IServiceCollection AddProviderServices(IServiceCollection services)
        => services.AddEntityFrameworkDuckDB();

    public override DbContextOptionsBuilder UseProviderOptions(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseDuckDB(new DuckDBConnection(DuckDBConnectionStringBuilder.InMemoryConnectionString));

    public override LoggingDefinitions LoggingDefinitions { get; } = new DuckDBLoggingDefinitions();
}
