#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COMPOSE_FILE="$ROOT_DIR/test/tiered-storage-s3.compose.yml"
SOLUTION="$ROOT_DIR/DuckDB.EFCoreProvider.slnx"
TEST_PROJECT="$ROOT_DIR/test/DuckDB.EFCoreProvider.FunctionalTests/DuckDB.EFCoreProvider.FunctionalTests.csproj"

export DUCKDB_S3_TEST_PORT="${DUCKDB_S3_TEST_PORT:-19010}"
export DUCKDB_S3_TEST_ENDPOINT="${DUCKDB_S3_TEST_ENDPOINT:-127.0.0.1:${DUCKDB_S3_TEST_PORT}}"
export DUCKDB_S3_TEST_BUCKET="${DUCKDB_S3_TEST_BUCKET:-tiered-storage-integration}"
export DUCKDB_S3_TEST_PREFIX="${DUCKDB_S3_TEST_PREFIX:-provider-failure-matrix}"
export DUCKDB_S3_TEST_REGION="${DUCKDB_S3_TEST_REGION:-us-east-1}"
export DUCKDB_S3_TEST_KEY="${DUCKDB_S3_TEST_KEY:-minioadmin}"
export DUCKDB_S3_TEST_SECRET="${DUCKDB_S3_TEST_SECRET:-minioadmin}"
export DUCKDB_S3_TEST_SSL="${DUCKDB_S3_TEST_SSL:-false}"

compose() {
    docker compose -f "$COMPOSE_FILE" "$@"
}

cleanup() {
    compose down --volumes --remove-orphans || true
}

minio_is_ready() {
    curl --fail --silent --show-error \
        "http://127.0.0.1:${DUCKDB_S3_TEST_PORT}/minio/health/live" >/dev/null 2>&1
}

wait_for_minio() {
    local attempt
    for attempt in {1..60}; do
        if minio_is_ready; then
            return 0
        fi
        sleep 1
    done

    echo "Timed out waiting for MinIO." >&2
    compose logs
    return 1
}

trap cleanup EXIT
cleanup
compose up --detach minio
wait_for_minio
compose run --rm createbucket

dotnet restore "$SOLUTION"
dotnet build "$SOLUTION" --no-restore
dotnet test "$TEST_PROJECT" --no-build \
    --filter "FullyQualifiedName~TieredStorageS3Tests" \
    "$@"
