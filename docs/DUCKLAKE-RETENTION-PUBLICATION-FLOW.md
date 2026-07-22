# Recommended DuckLake retention publication flow

> Design note for later follow-up. This flow is not implemented by the current provider. `ToTieredStore(...)`
> remains incompatible with the DuckLake backend because DuckLake owns its Parquet layout, snapshots, compaction,
> and data-file lifecycle.

## Objective

Provide the same policy-neutral retention contract as immutable Parquet generations while using DuckLake's native
snapshot transaction and maintenance model. The caller supplies an approved lifecycle cutoff and exact protected
technical scopes. The provider must not infer retention policy, ownership, legal holds, authorization, approval, or
cleanup timing.

## Recommended architecture

Keep one generic retention coordinator and separate physical publication strategies:

```text
Generic retention coordinator
├── Immutable Parquet generation backend
└── DuckLake snapshot backend
```

The coordinator should own:

- exact root binding and lifecycle metadata;
- cutoff alignment and protected technical scopes;
- aggregate graph traversal, including shared descendants;
- selected, retained, and excluded counts;
- stable-key, schema, and partition-contract validation;
- deterministic plan fingerprints and stale-plan rejection; and
- generic plan/result types and failure-stage reporting.

The DuckLake backend should own:

- snapshot identity and concurrency checks;
- transactional deletion from DuckLake tables;
- DuckLake snapshot evidence and time-travel verification;
- native compaction/rewrite recommendations; and
- separately authorized snapshot expiry and file-cleanup inventory.

Do not make the existing hot-DuckDB/cold-Parquet view implementation conditional on DuckLake. The two backends have
different physical state and publication semantics even though they can share the same retention intent.

## Planning flow

1. Reject a caller-owned transaction. Planning must observe a stable provider-controlled connection state.
2. Resolve the configured root, lifecycle property, month/day granularity, partition declarations, child graph, and
   stable match keys from provider metadata.
3. Read `catalog.current_snapshot()` and store it as the exact input version.
4. Align the caller-supplied cutoff to the configured technical lifecycle boundary.
5. Validate every protected scope against the declared partition contract without assigning business meaning.
6. Count input, retained, and excluded rows for every configured root and descendant table.
7. Verify that retained descendants have retained parents and that shared descendants remain valid for peer roots.
8. Capture the schema version and relevant table/column/partition metadata from the active snapshot.
9. Build a deterministic fingerprint from the binding, input snapshot, schema contract, effective cutoff, canonical
   scopes, stable keys, and per-table counts.
10. Return an immutable reviewable plan. Do not create a snapshot or write data during planning.

Suggested backend-neutral version fields:

```csharp
string BackendKind;             // "parquet-generation" or "ducklake-snapshot"
string InputVersion;            // generation id or DuckLake snapshot id
string ExpectedPublicationKey;  // deterministic plan key, not a predicted DuckLake snapshot id
```

DuckLake allocates the output snapshot at commit time, so the generic contract should not require a predictable
output version identifier before publication.

## Publication flow

1. Reject a caller-owned transaction and open a provider-owned DuckLake transaction.
2. Re-read `catalog.current_snapshot()` inside the operation and require it to equal the plan's input snapshot.
3. Re-resolve the binding and schema contract, rebuild the plan evidence, and require an identical fingerprint.
4. If the plan is a verified no-op, commit no mutation, repair no unrelated state, and return the current snapshot.
5. Set an optional provider-generated DuckLake commit message containing the plan fingerprint and root control key.
   Do not place caller secrets, business policy, or protected-scope values in diagnostic text.
6. Delete excluded descendant rows in leaf-to-root order using provider-generated predicates derived from the
   approved lifecycle boundary and exact protected scopes.
7. Delete excluded root rows last. DuckLake does not enforce EF foreign keys, so the provider must preserve graph
   consistency explicitly rather than relying on physical constraints.
8. Recount retained rows and verify stable keys and root/descendant consistency within the same transaction.
9. Commit once. The commit is the atomic publication of the new active DuckLake snapshot.
10. Read `catalog.last_committed_snapshot()` and verify that it is now the active snapshot and contains the expected
    retained rows.
11. Return the input snapshot, committed output snapshot, per-table evidence, and cleanup candidates. Do not expire
    snapshots or delete files as part of publication.

DuckLake may represent deletions through inlined metadata, positional delete files, or later file rewrites. These are
DuckLake implementation details and must not become provider retention policy.

## Retry and failure semantics

- Before commit, rollback leaves the input snapshot active and readable.
- After commit, retry first checks the current/last committed snapshot and the provider publication key.
- A retry must return the already-published result only when the committed snapshot can be tied to the exact plan.
- A different active snapshot makes the plan stale even if row counts happen to match.
- Cancellation before commit rolls back; cancellation observed after commit returns committed-state evidence rather
  than implying rollback.
- Failure injection should cover before/after revalidation, each table deletion, verification, commit, result
  reconstruction, restart, and cleanup planning.

Persisting the plan fingerprint in DuckLake commit metadata is the preferred idempotency correlation mechanism. If
the deployed DuckLake version cannot provide sufficiently queryable commit metadata, use a provider-owned catalogue
table written in the same DuckLake transaction. Never use node-local state for retry correlation.

## Rollback and cleanup

Publication leaves the input snapshot intact for time travel and rollback. Cleanup is a separately authorized flow:

1. List snapshots and exclude the active snapshot plus every explicitly protected rollback version.
2. Use `ducklake_expire_snapshots(..., dry_run => true)` to produce reviewable expiry inventory.
3. After separate authorization, expire only the approved snapshots.
4. Use `ducklake_cleanup_old_files(..., dry_run => true)` to produce physical cleanup inventory.
5. Apply an independently approved minimum age that exceeds the maximum supported reader transaction duration.
6. Execute physical cleanup separately and return exact scheduled/deleted-file evidence.

Never enumerate or delete object-store files behind DuckLake directly. DuckLake's catalogue is the authority for
snapshot reachability and file lifecycle.

## Acceptance matrix

Minimum DuckLake coverage should include:

- local DuckLake metadata and PostgreSQL metadata with disposable MinIO data;
- month and day lifecycle boundaries;
- root, nested children, and shared descendants;
- exact protected partition scopes below the cutoff;
- empty, complete-period, and partial-scope retention;
- concurrent snapshot change between plan and publication;
- retry before and after commit, including process restart;
- schema or partition-contract change after planning;
- input-snapshot time travel after publication;
- dry-run snapshot expiry and file cleanup without automatic deletion; and
- delete-heavy scale behavior followed by DuckLake-native rewrite/compaction measurement.

## Follow-up decisions

Before implementation, decide:

1. whether the existing retention plan/result types become backend-neutral or gain DuckLake-specific companion types;
2. which model-builder API declares lifecycle metadata without enabling `ToTieredStore(...)` views;
3. how the plan fingerprint is recorded atomically in DuckLake snapshot metadata;
4. which DuckLake extension versions provide the required snapshot and maintenance functions; and
5. whether rollback is exposed as a provider operation or remains an operational time-travel procedure.

Reference documentation:

- [DuckLake transactions](https://ducklake.select/docs/stable/duckdb/advanced_features/transactions)
- [DuckLake snapshots](https://ducklake.select/docs/stable/duckdb/usage/snapshots)
- [DuckLake time travel](https://ducklake.select/docs/stable/duckdb/usage/time_travel)
- [Expire snapshots](https://ducklake.select/docs/stable/duckdb/maintenance/expire_snapshots)
- [Cleanup of files](https://ducklake.select/docs/stable/duckdb/maintenance/cleanup_of_files)
- [Rewrite heavily deleted files](https://ducklake.select/docs/stable/duckdb/maintenance/rewrite_data_files)
