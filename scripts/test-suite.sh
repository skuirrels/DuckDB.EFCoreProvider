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
  full-project     Raw full functional test project; useful for backlog discovery.

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

write_critical_filter='FullyQualifiedName~ProductionWriteDuckDBTest|FullyQualifiedName~DuckDBUpdateSqlGeneratorTest|FullyQualifiedName~DuckDBMigrationsSqlGeneratorTest.AddColumnOperation_with_auto_increment_creates_sequence_and_default|FullyQualifiedName~DuckDBMigrationsSqlGeneratorTest.CreateTableOperation_with_auto_increment_creates_sequence_and_default|FullyQualifiedName~DuckDBGenericNonRelationship'
write_broad_filter="${write_critical_filter}|FullyQualifiedName~StoreGeneratedDuckDBTest|FullyQualifiedName~TransactionDuckDBTest|FullyQualifiedName~UpdatesDuckDBTest"
production_gate_filter="${write_broad_filter}|FullyQualifiedName~Migrations"

run_build() {
    dotnet restore "$SOLUTION"
    dotnet build "$SOLUTION" --no-restore
}

run_filtered_tests() {
    local filter="$1"
    dotnet test "$TEST_PROJECT" --no-build --filter "$filter" "${extra_args[@]+"${extra_args[@]}"}"
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
        dotnet test "$TEST_PROJECT" --no-build "${extra_args[@]+"${extra_args[@]}"}"
        ;;
    *)
        echo "Unknown suite '$suite'." >&2
        usage >&2
        exit 2
        ;;
esac
