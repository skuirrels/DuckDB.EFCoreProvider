#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SOLUTION="$ROOT_DIR/DuckDB.EFCoreProvider.slnx"
TEST_PROJECT="$ROOT_DIR/test/DuckDB.EFCoreProvider.FunctionalTests/DuckDB.EFCoreProvider.FunctionalTests.csproj"

usage() {
    cat <<'USAGE'
Usage: scripts/test-suite.sh [suite] [-- extra dotnet test args]

Suites:
  write-critical   Core write-provider contract: SQL generation, generated keys, concurrency, migrations, model validation.
  write-broad      write-critical plus store-generated values, transactions, and broader update coverage.
  migrations       Migration SQL and migration infrastructure coverage.
  updates          EF update, graph update, and bulk update coverage.
  all              Complete production write-provider gate.
  full-project     Complete functional test project; CI runs this after the production gate.
  compatibility    Shared query, mapping, write and backend compatibility coverage.

Both target frameworks run by default. Set DUCKDB_TEST_FRAMEWORK=net10.0 or net11.0
to select one target (the solution build still checks both).

Examples:
  scripts/test-suite.sh write-critical
  scripts/test-suite.sh write-broad -- --logger:"console;verbosity=detailed"
  scripts/test-suite.sh all -- /p:CollectCoverage=true /p:CoverletOutputFormat=opencover
  scripts/test-suite.sh full-project
USAGE
}

suite="${1:-write-critical}"
if [[ "${suite}" == "-h" || "${suite}" == "--help" ]]; then
    usage
    exit 0
fi

if [[ $# -gt 0 ]]; then
    shift
fi

extra_args=()
if [[ $# -gt 0 ]]; then
    if [[ "$1" != "--" ]]; then
        echo "Unexpected argument '$1'. Use -- before additional dotnet test arguments." >&2
        usage >&2
        exit 2
    fi

    shift
    extra_args=("$@")
fi

# Each entry is matched as FullyQualifiedName~<pattern>; one pattern per line keeps diffs reviewable.
write_critical_patterns=(
    ProductionWriteDuckDBTest
    DuckDBUpdateSqlGeneratorTest
    DuckDBMigrationsSqlGeneratorTest.AddColumnOperation_with_auto_increment_creates_sequence_and_default
    DuckDBMigrationsSqlGeneratorTest.CreateTableOperation_with_auto_increment_creates_sequence_and_default
    DuckDBGenericNonRelationship
    ReferencedPrincipalUpdateTests
    DualRolePrincipalUpdateTests
)
write_broad_patterns=(
    "${write_critical_patterns[@]}"
    StoreGeneratedDuckDBTest
    TransactionDuckDBTest
    UpdatesDuckDBTest
)
production_gate_patterns=(
    "${write_broad_patterns[@]}"
    Migrations
    MigrationTableRebuildTests
    StructDesignTimeWorkflowTests
    RetryingExecutionStrategyTests
    HiLoKeyGenerationTests
)
compatibility_patterns=(
    Query.PrecompiledQueryDuckDBTest.FromSql
    PrecompiledSqlPregenerationQueryDuckDBTest
    DuckDBApiConsistencyTest.Public_api_bool_parameters_should_not_be_prefixed
    NorthwindAggregateOperatorsQueryDuckDBTest.Average_on_float_column
    JsonUpdateDuckDBTest.Edit_single_property_nullable
    Struct
    ArrayRoundTrip
    NativeArrayCompatibility
    CharacterConversionCompatibility
    BinaryJsonCompatibility
    OwnedNavigationsSetOperationsDuckDBTest.Over_associate_collection_projected
    OwnedNavigationCompatibility
    JsonPathRewriting
    AnalyticalTranslation
    DuckLake
    Quack
    BulkInsert
    Upsert
    AdoNetSubstitution
    FileSource
    Spatial
    ValueTranslationRegression
    Scaffolding.CompiledModelDuckDBTest
    GroupingSetsTranslationTests
    QualifyAndAsOfJoinTests
    MapAndNestedListTests
    StoreTypeMappingTests
)

to_filter() {
    local filter="" pattern
    for pattern in "$@"; do
        filter+="${filter:+|}FullyQualifiedName~${pattern}"
    done
    printf '%s' "$filter"
}

write_critical_filter="$(to_filter "${write_critical_patterns[@]}")"
write_broad_filter="$(to_filter "${write_broad_patterns[@]}")"
production_gate_filter="$(to_filter "${production_gate_patterns[@]}")"
compatibility_filter="$(to_filter "${compatibility_patterns[@]}")"

framework_args=()
if [[ -n "${DUCKDB_TEST_FRAMEWORK:-}" ]]; then
    case "$DUCKDB_TEST_FRAMEWORK" in
        net10.0|net11.0) framework_args=(--framework "$DUCKDB_TEST_FRAMEWORK") ;;
        *) echo "Unsupported DUCKDB_TEST_FRAMEWORK: $DUCKDB_TEST_FRAMEWORK" >&2; exit 2 ;;
    esac
fi

run_build() {
    "$ROOT_DIR/scripts/restore-tools.sh"
    dotnet restore "$SOLUTION"
    dotnet build "$SOLUTION" --no-restore
}

run_filtered_tests() {
    local filter="$1"
    dotnet test "$TEST_PROJECT" --no-build "${framework_args[@]+"${framework_args[@]}"}" --filter "$filter" "${extra_args[@]+"${extra_args[@]}"}"
}

cd "$ROOT_DIR"

case "$suite" in
    write-critical)
        run_build
        run_filtered_tests "$write_critical_filter"
        ;;
    write-broad)
        run_build
        run_filtered_tests "$write_broad_filter"
        ;;
    migrations)
        run_build
        run_filtered_tests 'FullyQualifiedName~Migrations'
        ;;
    updates)
        run_build
        run_filtered_tests 'FullyQualifiedName~UpdatesDuckDBTest'
        ;;
    all)
        run_build
        run_filtered_tests "$production_gate_filter"
        ;;
    full-project)
        run_build
        dotnet test "$TEST_PROJECT" --no-build "${framework_args[@]+"${framework_args[@]}"}" "${extra_args[@]+"${extra_args[@]}"}"
        ;;
    compatibility)
        run_build
        run_filtered_tests "$compatibility_filter"
        ;;
    *)
        echo "Unknown suite '$suite'." >&2
        usage >&2
        exit 2
        ;;
esac
