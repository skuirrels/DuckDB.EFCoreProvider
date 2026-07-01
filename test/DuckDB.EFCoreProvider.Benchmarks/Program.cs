using BenchmarkDotNet.Running;

// Run all benchmarks (or filter), e.g.:
//   dotnet run -c Release --project test/DuckDB.EFCoreProvider.Benchmarks -- --filter *Write*
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
