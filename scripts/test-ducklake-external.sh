#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COMPOSE_FILE="$ROOT_DIR/test/ducklake-external.compose.yml"
SOLUTION="$ROOT_DIR/DuckDB.EFCoreProvider.slnx"
TEST_PROJECT="$ROOT_DIR/test/DuckDB.EFCoreProvider.FunctionalTests/DuckDB.EFCoreProvider.FunctionalTests.csproj"

export DUCKLAKE_EXTERNAL_TESTS=1
export DUCKLAKE_POSTGRES_HOST="${DUCKLAKE_POSTGRES_HOST:-127.0.0.1}"
export DUCKLAKE_POSTGRES_PORT="${DUCKLAKE_POSTGRES_PORT:-15432}"
export DUCKLAKE_POSTGRES_DATABASE="${DUCKLAKE_POSTGRES_DATABASE:-ducklake}"
export DUCKLAKE_POSTGRES_USER="${DUCKLAKE_POSTGRES_USER:-ducklake}"
export DUCKLAKE_POSTGRES_PASSWORD="${DUCKLAKE_POSTGRES_PASSWORD:-ducklake-integration}"
export DUCKLAKE_S3_PORT="${DUCKLAKE_S3_PORT:-19000}"
export DUCKLAKE_S3_ENDPOINT="${DUCKLAKE_S3_ENDPOINT:-127.0.0.1:${DUCKLAKE_S3_PORT}}"
export DUCKLAKE_S3_KEY_ID="${DUCKLAKE_S3_KEY_ID:-minioadmin}"
export DUCKLAKE_S3_SECRET="${DUCKLAKE_S3_SECRET:-minioadmin}"
export DUCKLAKE_S3_REGION="${DUCKLAKE_S3_REGION:-us-east-1}"
export DUCKLAKE_S3_BUCKET="${DUCKLAKE_S3_BUCKET:-ducklake-integration}"

compose() {
    docker compose -f "$COMPOSE_FILE" "$@"
}

cleanup() {
    compose down --volumes --remove-orphans || true
}

wait_for_service() {
    local name="$1"
    local readiness_check="$2"
    local attempt

    for attempt in {1..60}; do
        if "$readiness_check"; then
            return 0
        fi
        sleep 1
    done

    echo "Timed out waiting for $name." >&2
    compose logs
    return 1
}

postgres_is_ready() {
    compose exec --no-TTY postgres pg_isready \
        --username "$DUCKLAKE_POSTGRES_USER" \
        --dbname "$DUCKLAKE_POSTGRES_DATABASE" >/dev/null 2>&1
}

minio_is_ready() {
    curl --fail --silent --show-error \
        "http://127.0.0.1:${DUCKLAKE_S3_PORT}/minio/health/live" >/dev/null 2>&1
}

trap cleanup EXIT
cleanup
compose up --detach postgres minio

wait_for_service PostgreSQL postgres_is_ready
wait_for_service MinIO minio_is_ready
compose run --rm createbucket

dotnet restore "$SOLUTION"
dotnet build "$SOLUTION" --no-restore
dotnet test "$TEST_PROJECT" --no-build \
    --filter "FullyQualifiedName~DuckLake" \
    "$@"
