#!/usr/bin/env bash
# Runs the load test scenarios against a running Instance and records dotnet-counters while each one runs
#   tests/load/run.sh <instance pid> <output directory> [base url]
#   SCENARIOS="api streams" tests/load/run.sh ...   (a subset of the scenarios)
# Writes <scenario>.k6.json (k6 summary) and <scenario>.counters.csv for each scenario

set -euo pipefail

PID="$1"
OUT="$2"
BASE_URL="${3:-http://localhost:8495}"
K6="${K6:-k6}"
DIR="$(cd "$(dirname "$0")" && pwd)"

mkdir -p "$OUT"

run() {
    local name="$1" seconds="$2"; shift 2

    dotnet-counters collect -p "$PID" --counters System.Runtime,Microsoft.AspNetCore.Hosting --refresh-interval 1 \
        --format csv -o "$OUT/$name.counters.csv" --duration "$(printf '%02d:%02d:%02d' $((seconds / 3600)) $((seconds % 3600 / 60)) $((seconds % 60)))" > /dev/null &
    local counters=$!
    sleep 1

    "$K6" run --quiet -e BASE_URL="$BASE_URL" "$@" --summary-export "$OUT/$name.k6.json" "$DIR/$name.js" > "$OUT/$name.k6.txt" 2>&1 || true

    wait "$counters" || true
    echo "$name done"
}

# Warm up (JIT, caches) so the first scenario isn't measured cold
"$K6" run --quiet -e BASE_URL="$BASE_URL" --duration 10s --vus 10 "$DIR/api.js" > /dev/null 2>&1 || true

# The scenarios to run (default all), and how long to record counters for each (the scenario's length plus a few seconds)
for scenario in ${SCENARIOS:-api app streams}; do
    case "$scenario" in
        api) run api 58 ;;
        app) run app 58 ;;
        streams) run streams 68 ;;
    esac
done
