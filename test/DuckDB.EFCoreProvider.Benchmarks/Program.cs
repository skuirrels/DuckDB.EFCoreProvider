using BenchmarkDotNet.Running;

if (args is ["--cold-sql-probe"])
{
    DuckDB.EFCoreProvider.Benchmarks.SqlGenerationColdStartProbe.Run();
    return;
}

if (args is ["--model-startup-probe"])
{
    DuckDB.EFCoreProvider.Benchmarks.ModelStartupProbe.Run();
    return;
}

// Run all benchmarks (or filter), e.g.:
//   dotnet run -c Release --project test/DuckDB.EFCoreProvider.Benchmarks -- --filter *Write*
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);