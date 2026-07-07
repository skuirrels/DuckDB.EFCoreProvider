#!/usr/bin/env bash
set -euo pipefail

# Benchmark DuckDB's remote Parquet read path (the tiered-storage cold tier) side by side on S3 and Azure.
#
# It seeds an identical hive-partitioned dataset to each configured store, then times a suite of queries that
# mirror how the tiered union views read cold data: a single-partition point read, a one-year range read, a
# full scan, and a projection. Timings are reported per store as cold (first, uncached metadata) and warm
# (median of the rest, metadata cached in-session), plus the partition-pruning "files scanned" from EXPLAIN.
#
# Requires the DuckDB CLI (https://duckdb.org/docs/stable/clients/cli/overview) on PATH. Configure one or both
# stores with environment variables and point them at EMPTY prefixes you own — seeding writes Parquet there.
#
#   # S3 (or S3-compatible). Omit the key/secret to use DuckDB's credential_chain (IAM role, env, etc.).
#   export BENCH_S3_URL="s3://my-bucket/duckdb-bench"
#   export BENCH_S3_REGION="eu-west-2"
#   export BENCH_S3_KEY="..."; export BENCH_S3_SECRET="..."          # optional
#   export BENCH_S3_ENDPOINT="localhost:9000"                        # optional: S3-compatible (MinIO/R2/GCS)
#   export BENCH_S3_URL_STYLE="path"; export BENCH_S3_USE_SSL="false"# optional, for the endpoint above
#
#   # Azure Blob. Use a connection string, or set ACCOUNT to use credential_chain.
#   export BENCH_AZURE_URL="azure://my-container/duckdb-bench"
#   export BENCH_AZURE_CONNECTION_STRING="DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;"
#   # ...or: export BENCH_AZURE_ACCOUNT="mystorageacct"              # uses PROVIDER credential_chain
#
#   scripts/bench-remote-read.sh seed     # write the dataset to every configured store (run once)
#   scripts/bench-remote-read.sh run      # benchmark every configured store
#   scripts/bench-remote-read.sh all      # seed then run
#
# Tunables (env): BENCH_ROWS_PER_PARTITION (50000), BENCH_MONTHS (60), BENCH_ROW_GROUP_SIZE (10000),
# BENCH_REPEAT (6).

REPEAT="${BENCH_REPEAT:-6}"
ROWS="${BENCH_ROWS_PER_PARTITION:-50000}"
MONTHS="${BENCH_MONTHS:-60}"
ROW_GROUP="${BENCH_ROW_GROUP_SIZE:-10000}"

command -v duckdb >/dev/null 2>&1 || { echo "error: the 'duckdb' CLI is required on PATH." >&2; exit 1; }

# --- Which stores are configured? ---
STORES=()
[[ -n "${BENCH_S3_URL:-}" ]] && STORES+=("s3")
[[ -n "${BENCH_AZURE_URL:-}" ]] && STORES+=("azure")
if [[ ${#STORES[@]} -eq 0 ]]; then
    echo "error: set BENCH_S3_URL and/or BENCH_AZURE_URL (see the header of this script)." >&2
    exit 1
fi

# --- Per-store setup SQL (extension + secret) and base URL. ---
setup_sql() {
    case "$1" in
        s3)
            local s="INSTALL httpfs; LOAD httpfs; CREATE OR REPLACE SECRET s3bench (TYPE s3"
            if [[ -n "${BENCH_S3_KEY:-}" ]]; then
                s+=", KEY_ID '${BENCH_S3_KEY}', SECRET '${BENCH_S3_SECRET:-}'"
            else
                s+=", PROVIDER credential_chain"
            fi
            s+=", REGION '${BENCH_S3_REGION:-us-east-1}'"
            [[ -n "${BENCH_S3_ENDPOINT:-}" ]] && s+=", ENDPOINT '${BENCH_S3_ENDPOINT}'"
            [[ -n "${BENCH_S3_URL_STYLE:-}" ]] && s+=", URL_STYLE '${BENCH_S3_URL_STYLE}'"
            [[ -n "${BENCH_S3_USE_SSL:-}" ]] && s+=", USE_SSL ${BENCH_S3_USE_SSL}"
            echo "${s});"
            ;;
        azure)
            local s="INSTALL azure; LOAD azure; CREATE OR REPLACE SECRET azbench (TYPE azure"
            if [[ -n "${BENCH_AZURE_CONNECTION_STRING:-}" ]]; then
                s+=", CONNECTION_STRING '${BENCH_AZURE_CONNECTION_STRING}'"
            else
                s+=", PROVIDER credential_chain, ACCOUNT_NAME '${BENCH_AZURE_ACCOUNT:?set BENCH_AZURE_CONNECTION_STRING or BENCH_AZURE_ACCOUNT}'"
            fi
            echo "${s});"
            ;;
    esac
}

base_url() { case "$1" in s3) echo "${BENCH_S3_URL%/}";; azure) echo "${BENCH_AZURE_URL%/}";; esac; }
label()    { case "$1" in s3) echo "S3    (httpfs)";; azure) echo "Azure (azure) ";; esac; }
glob()     { echo "$(base_url "$1")/bench/**/*.parquet"; }

# Target one partition in the middle of the range for the point/projection queries.
MID=$((MONTHS/2)); TY=$((2020 + MID/12)); TM=$((MID%12 + 1))

seed_store() {
    local store="$1" url; url="$(base_url "$store")"
    echo ">> seeding $(label "$store")  ->  ${url}/bench   (${ROWS} rows x ${MONTHS} partitions)"
    duckdb -batch <<SQL >/dev/null
$(setup_sql "$store")
CREATE OR REPLACE TABLE bench AS
SELECT r AS id,
       (DATE '2020-01-01' + to_months((r // ${ROWS})::INTEGER))                 AS ts,
       ((r % 1000) * 1.5)::DECIMAL(10,2)                                        AS amount,
       repeat('padding-', 8)                                                    AS note,
       year(DATE '2020-01-01' + to_months((r // ${ROWS})::INTEGER))             AS year,
       month(DATE '2020-01-01' + to_months((r // ${ROWS})::INTEGER))            AS month
FROM range(0, ${ROWS} * ${MONTHS}) t(r);
COPY bench TO '${url}/bench' (FORMAT PARQUET, PARTITION_BY (year, month), OVERWRITE_OR_IGNORE, ROW_GROUP_SIZE ${ROW_GROUP});
SQL
}

# Median of the numbers on stdin.
median() { sort -n | awk '{a[NR]=$1} END{ if(NR==0){print "n/a"} else {print a[int((NR+1)/2)]} }'; }

# Time one query REPEAT times in a fresh session; echo "cold warm" seconds.
time_query() {
    local store="$1" query="$2" out reps i times cold warm
    reps=""
    for i in $(seq 1 "$REPEAT"); do reps="${reps}${query};"$'\n'; done
    out="$(duckdb -batch 2>&1 <<SQL
$(setup_sql "$store")
.timer on
${reps}
SQL
)"
    times="$(echo "$out" | awk '/Run Time/{for(i=1;i<=NF;i++) if($i=="real") print $(i+1)}')"
    if [[ -z "$times" ]]; then echo "ERR ERR"; echo "$out" | tail -3 >&2; return; fi
    cold="$(printf '%s\n' "$times" | sed -n '1p')"
    warm="$(printf '%s\n' "$times" | sed -n '2,$p' | median)"
    [[ -z "$warm" || "$warm" == "n/a" ]] && warm="$cold"
    echo "${cold} ${warm}"
}

files_scanned() {
    local store="$1"
    duckdb -batch 2>&1 <<SQL | grep -oE "Scanning Files: [0-9]+/[0-9]+" | head -1
$(setup_sql "$store")
EXPLAIN ANALYZE SELECT count(*) FROM read_parquet('$(glob "$store")', hive_partitioning=true) WHERE year=${TY} AND month=${TM};
SQL
}

# The benchmark query for a named case, over the given read_parquet glob.
query_for() {
    local name="$1" g="$2"
    case "$name" in
        point)      echo "SELECT count(*), sum(amount) FROM read_parquet('${g}', hive_partitioning=true) WHERE year=${TY} AND month=${TM}";;
        year-range) echo "SELECT count(*), sum(amount) FROM read_parquet('${g}', hive_partitioning=true) WHERE year=${TY}";;
        full-scan)  echo "SELECT count(*) FROM read_parquet('${g}', hive_partitioning=true)";;
        projection) echo "SELECT sum(amount) FROM read_parquet('${g}', hive_partitioning=true) WHERE year=${TY} AND month=${TM}";;
    esac
}

run_bench() {
    local store g q query res
    echo
    echo "Query suite (target partition year=${TY} month=${TM}; ${REPEAT} runs each; seconds)"
    printf '%-16s | %-22s | %-16s | %-16s\n' "store" "query" "cold (1st)" "warm (median)"
    printf -- '-----------------+------------------------+------------------+------------------\n'
    for store in "${STORES[@]}"; do
        g="$(glob "$store")"
        for q in point year-range full-scan projection; do
            query="$(query_for "$q" "$g")"
            res="$(time_query "$store" "$query")"
            printf '%-16s | %-22s | %-16s | %-16s\n' "$(label "$store")" "$q" "${res% *}" "${res#* }"
        done
    done
    echo
    echo "Partition pruning (EXPLAIN ANALYZE on the point query):"
    for store in "${STORES[@]}"; do
        printf '  %-16s %s\n' "$(label "$store")" "$(files_scanned "$store")"
    done
    echo
    echo "Notes: 'cold' includes first-access metadata round-trips; 'warm' has per-session metadata cached."
    echo "Emulator numbers are NOT representative of real cloud latency — run against real S3/Azure endpoints."
}

case "${1:-}" in
    seed) for s in "${STORES[@]}"; do seed_store "$s"; done ;;
    run)  run_bench ;;
    all)  for s in "${STORES[@]}"; do seed_store "$s"; done; run_bench ;;
    *)    echo "usage: scripts/bench-remote-read.sh {seed|run|all}   (configure via BENCH_* env vars; see file header)" >&2; exit 1 ;;
esac
