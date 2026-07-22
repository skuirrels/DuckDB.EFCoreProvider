# Engineering guidance

## Architecture

- Keep provider capabilities in the central immutable `IDuckDBEngineCapabilities` model. Translate backend options into capabilities at the composition boundary; consumers must branch on capabilities rather than profile checks such as `IsDuckLake`.
- Separate decisions from rendering. Planners resolve EF metadata, validate supported shapes, and produce immutable plans; SQL generators only render those plans through `ISqlGenerationHelper`.
- Represent query features as typed `SqlExpression` nodes before final SQL generation. Do not infer feature semantics from strings in the renderer.
- Keep public extension methods thin. Put orchestration and policy in focused provider services, with one clear owner for each decision.
- Validate unsupported model/configuration combinations as early as possible, preferably during model validation rather than during command execution.
- Route identifiers, parameter names, and literals through EF/provider helpers. Do not concatenate user- or model-derived SQL fragments directly.
- Prefer incremental, behaviour-preserving refactors. Preserve public API and package compatibility unless a breaking change is explicitly approved.
- Avoid speculative abstractions. Introduce an interface when it represents a genuine service boundary or has multiple consumers, not merely to wrap one pure helper.

## Testing requirements

- Characterise existing behaviour before moving responsibilities between components.
- Add focused tests for planners and capability matrices, plus SQL-shape regression coverage where rendering is affected.
- Exercise both native DuckDB and DuckLake profiles whenever capability-dependent code changes.
- Keep end-to-end SaveChanges/query/migration tests for affected paths; unit tests alone are not sufficient for provider behaviour.
- Run the targeted test class while iterating, then the full solution build and the repository's production test gate before completing the change.
- Treat formatting, package/API validation, and a final diff review as part of regression prevention.
