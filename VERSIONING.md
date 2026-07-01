# Versioning & Breaking-Change Policy

`DuckDB.EFCoreProvider` follows [Semantic Versioning 2.0.0](https://semver.org/) (`MAJOR.MINOR.PATCH`).

## What each part means

- **MAJOR** — incremented for a breaking change to the supported public API, or when the provider is
  retargeted to a new Entity Framework Core major version (see *EF Core alignment* below).
- **MINOR** — new, backward-compatible functionality (additional translations, type mappings, options,
  newly supported EF Core features).
- **PATCH** — backward-compatible bug fixes and documentation/build changes.

## What counts as "public API"

The supported surface is the API a consuming application uses directly:

- the `UseDuckDB(...)` configuration extension methods and their options;
- public extension methods under `DuckDB.EFCoreProvider.Extensions` (e.g. `UseAutoIncrement()`, `FromParquet`);
- the `[FromParquet]` attribute and other public metadata/builder APIs;
- the spatial entry point `UseNetTopologySuite()`.

**Not covered by SemVer** (may change in any release, including PATCH):

- Everything in an `Internal` namespace, and any API marked with the EF Core internal-API warnings
  (`EF1001`, `EF9100`). These exist to satisfy EF Core provider contracts and follow EF Core's own
  "internal API" stability policy — i.e. none.
- Generated SQL text. SQL shape may change between any versions as translations improve; only observable
  query *results* are treated as behaviour.

## EF Core alignment

- The provider tracks **one EF Core line at a time**; `1.0.x` targets **EF Core 10.0.x on .NET 10**.
- Microsoft EF Core package versions are kept aligned on the same patch across all projects via
  Central Package Management (`Directory.Packages.props`).
- Retargeting to a new EF Core **major** version (e.g. EF Core 11) is itself a **breaking change** and will
  ship as a new provider MAJOR version, only after the provider is rebuilt and passes the EF Core
  relational specification tests for that version. Support for a new EF Core major is not claimed until then.

## Deprecation

Where practical, public API that is being removed will first be marked `[Obsolete]` in a MINOR release with
a migration note, and removed no earlier than the next MAJOR release.

## Documented limitations are not bugs

Behaviours listed in [`docs/CAPABILITY-MAP.md`](docs/CAPABILITY-MAP.md) as DuckDB engine limitations are
intentional and are not treated as defects. Changing them depends on DuckDB itself, not the provider.

## Release notes

Each release documents notable changes in the package release notes / repository history. Review them
before upgrading, especially across MINOR bumps that may change generated SQL.
