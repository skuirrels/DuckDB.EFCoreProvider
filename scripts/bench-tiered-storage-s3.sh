#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COMPOSE_FILE="$ROOT_DIR/test/tiered-storage-s3.compose.yml"
BENCHMARK_PROJECT="$ROOT_DIR/test/DuckDB.EFCoreProvider.Benchmarks/DuckDB.EFCoreProvider.Benchmarks.csproj"
TRACE_FILE="$(mktemp)"

export DUCKDB_S3_TEST_PORT="${DUCKDB_S3_TEST_PORT:-19010}"
export DUCKDB_TIER_SCALE_S3_PROXY_PORT="${DUCKDB_TIER_SCALE_S3_PROXY_PORT:-19011}"
export DUCKDB_S3_TEST_BUCKET="${DUCKDB_S3_TEST_BUCKET:-tiered-storage-integration}"
export DUCKDB_S3_TEST_KEY="${DUCKDB_S3_TEST_KEY:-minioadmin}"
export DUCKDB_S3_TEST_SECRET="${DUCKDB_S3_TEST_SECRET:-minioadmin}"
export DUCKDB_TIER_SCALE_S3_BUCKET="${DUCKDB_TIER_SCALE_S3_BUCKET:-$DUCKDB_S3_TEST_BUCKET}"
export DUCKDB_TIER_SCALE_S3_ENDPOINT="${DUCKDB_TIER_SCALE_S3_ENDPOINT:-127.0.0.1:$DUCKDB_TIER_SCALE_S3_PROXY_PORT}"
export DUCKDB_TIER_SCALE_S3_PREFIX="${DUCKDB_TIER_SCALE_S3_PREFIX:-provider-scale-matrix}"
export DUCKDB_TIER_SCALE_S3_REGION="${DUCKDB_TIER_SCALE_S3_REGION:-us-east-1}"
export DUCKDB_TIER_SCALE_S3_KEY="${DUCKDB_TIER_SCALE_S3_KEY:-$DUCKDB_S3_TEST_KEY}"
export DUCKDB_TIER_SCALE_S3_SECRET="${DUCKDB_TIER_SCALE_S3_SECRET:-$DUCKDB_S3_TEST_SECRET}"
export DUCKDB_TIER_SCALE_S3_SSL="${DUCKDB_TIER_SCALE_S3_SSL:-false}"
export DUCKDB_TIER_SCALE_PARTITIONS="${DUCKDB_TIER_SCALE_PARTITIONS:-16}"
export DUCKDB_TIER_SCALE_PERIODS="${DUCKDB_TIER_SCALE_PERIODS:-12}"
export DUCKDB_TIER_SCALE_NODES="${DUCKDB_TIER_SCALE_NODES:-2}"
export DUCKDB_TIER_SCALE_FILE_FANOUT="${DUCKDB_TIER_SCALE_FILE_FANOUT:-1}"
export DUCKDB_TIER_SCALE_RETAINED_SCOPES="${DUCKDB_TIER_SCALE_RETAINED_SCOPES:-0}"
export DUCKDB_TIER_SCALE_SCOPE_PREFIX_WIDTH="${DUCKDB_TIER_SCALE_SCOPE_PREFIX_WIDTH:-1}"
export DUCKDB_TIER_SCALE_SHARED_DESCENDANT="${DUCKDB_TIER_SCALE_SHARED_DESCENDANT:-false}"

compose() {
    docker compose -f "$COMPOSE_FILE" "$@"
}

cleanup() {
    compose down --volumes --remove-orphans || true
    rm -f "$TRACE_FILE"
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

report_requests() {
    compose logs --no-color request-proxy >"$TRACE_FILE"
    awk '
        /TIER_REQUEST HEAD / { head += 1; next }
        /TIER_REQUEST GET / {
            if ($0 ~ /[?&](list-type|prefix|delimiter)=/) { list += 1 }
            else { get += 1 }
            next
        }
        END {
            printf "Remote S3 request telemetry: LIST=%d HEAD=%d GET=%d\n", list, head, get
        }
    ' "$TRACE_FILE"
}

trap cleanup EXIT
compose down --volumes --remove-orphans
compose up --detach minio request-proxy
wait_for_minio
compose run --rm createbucket

dotnet run -c Release --project "$BENCHMARK_PROJECT" -- \
    --filter '*TieredCatalogueScaleBenchmarks*' \
    --job Dry \
    "$@"

report_requests
