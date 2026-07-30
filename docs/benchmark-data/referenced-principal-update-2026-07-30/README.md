# Referenced-principal update benchmark artifacts

These are the raw BenchmarkDotNet artifacts supporting the `v1.15.0` versus
`v1.15.1` table in [`docs/PERFORMANCE.md`](../../PERFORMANCE.md).

## Provenance

| Artifact | SHA-256 |
|---|---|
| Published `DuckDB.EFCoreProvider.1.15.0.nupkg` | `7d58e7429e584d173f239aac4de74bdd3e1d441806e44202b791975be0df7bca` |
| Baseline Release `DuckDB.EFCoreProvider.dll` | `37923fcbe5334b7fab80121d4c27cd378f21b36d0dd78b6079f1e54192851898` |
| Final `DuckDB.EFCoreProvider.1.15.1.nupkg` | `ae267ed87b54097e3daad6ddd49b4d504528e0d2de08fdd0afaba8eb3b4df570` |
| Final Release `DuckDB.EFCoreProvider.dll` | `2a4826e6730761fc4fbf55f0efa02611d87309439973903c553e674c5679e4c4` |

The baseline source is tag `v1.15.0`, commit
`7ee363bd1e9f6181a67eb7dee5c552f7813f8f23`. Both source trees use the
same benchmark class. The final package passed .NET package compatibility
validation against `DuckDB.EFCoreProvider` 1.15.0.

## Method

The host ran macOS Tahoe 26.5.2, .NET SDK 10.0.300, .NET runtime 10.0.8,
and BenchmarkDotNet 0.15.8 on Arm64. Each benchmark invocation performs 25
tracked updates. Every measured case used one launch, five warmups, fifteen
measurement iterations, one invocation, and an unroll factor of one.

The final run command was:

```bash
dotnet run -c Release --no-build \
  --project test/DuckDB.EFCoreProvider.Benchmarks -- \
  --filter '*ReferencedPrincipalUpdateBenchmarks*' \
  --job short --inProcess \
  --warmupCount 5 --iterationCount 15 \
  --invocationCount 1 --unrollFactor 1
```

The in-process toolchain avoids rebuilding the provider after its Release DLL
hash has been recorded. BenchmarkDotNet could not elevate process priority in
the restricted test environment; this is recorded in the logs and applied
equally to the measured cases.

## Files

| File | Purpose | SHA-256 |
|---|---|---|
| `v1.15.0-referenced-no-child.csv` | Valid before measurement for the referenced table | `814cbae35f1b928b0c7b211bc479b06129fc709dc35b0c3f53558420d364c84c` |
| `v1.15.0-referenced-no-child.log` | Raw BenchmarkDotNet execution log | `595a3c11cd1d3153f17a78dd2f8a08756393175fe3f55b14bbb94858c78eb266` |
| `v1.15.0-unreferenced.csv` | Before measurement for the unaffected-path guard | `19848262f51a9bd61f066cbfb2e0e0013f4e39969fff8d6553059f5fbbdda83a` |
| `v1.15.0-unreferenced.log` | Raw BenchmarkDotNet execution log | `e14c91544f4ef80f1eba28ecbb5c011d16c8ff5dfe7733567cbb06024be70003` |
| `v1.15.0-referenced-with-child-failure.csv` | Empty result produced for the known failing case | `2281ca5939387efa1685511eacdbafc357fe69f366664fc55e3d11ed62b8512c` |
| `v1.15.0-referenced-with-child-failure.log` | Raw foreign-key failure and stack trace | `d83adcc2c4ce8d22b4ad5efe9810c70df94d55d5a1400f16617cc7ebee74d6b8` |
| `v1.15.1-final.csv` | Final three-case measurement | `c190626c1209ec0ef15ff0cb64a5c8a3d4fe9d7803abb0f7eb182df50842f4e2` |
| `v1.15.1-final.log` | Raw final BenchmarkDotNet execution log | `3de4e06bb72e054c361ded7b6266f1274665e748b173fad14829c1f1ee2cd37c` |
