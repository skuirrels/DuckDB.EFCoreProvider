# Security Policy

## Supported versions

`DuckDB.EFCoreProvider` targets a single Entity Framework Core / .NET line at a time. Security fixes are
provided only for the latest released minor version on the currently supported framework line.

| Provider line | EF Core | .NET | Supported |
|---|---|---|---|
| 1.0.x | 10.0.x | .NET 10 | ✅ |
| < 1.0 | — | — | ❌ |

Older EF Core / .NET lines are not maintained. Upgrading to the latest provider release is the supported
path for receiving security fixes.

## Reporting a vulnerability

**Please do not open a public issue for security vulnerabilities.**

Report privately using one of the following:

- **GitHub private vulnerability reporting** — use the repository's *Security → Report a vulnerability*
  page (GitHub Security Advisories), if enabled for the repository.
- **Email** — contact the maintainer directly at the address listed on the repository owner's profile.

Please include:

- a description of the issue and its impact;
- the affected version(s) and environment (EF Core / .NET / DuckDB.NET versions, OS);
- a minimal reproduction (model, LINQ/SQL, or migration) where possible.

## What to expect

This is a community, best-effort open-source project (see the README's *Support and Project Status*
section). There is **no commercial support contract or SLA.** Reports are triaged as maintainer time
allows. We aim to acknowledge a valid report and, where a fix is warranted, to release it in a patch
version and credit the reporter unless anonymity is requested.

## Scope notes

- Public APIs that wrap user input go through EF Core parameterisation and the provider's
  identifier/literal quoting helpers; do not bypass them with hand-built SQL.
- Internal APIs (those marked with the EF Core "internal API" warning, e.g. `EF1001`) are not part of the
  security-supported surface and may change without notice.
- DuckDB is an embedded, in-process database. Threats inherent to running an embedded analytical engine
  with access to the local filesystem and DuckDB extensions are the deploying application's responsibility.
