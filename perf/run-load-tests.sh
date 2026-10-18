#!/usr/bin/env bash
# One-command local runner for the JMeter load & performance suite.
#   ./perf/run-load-tests.sh [smoke|load|stress|spike|soak|all]     (default: all = smoke -> load)
# Needs: jmeter on PATH (or JMETER_HOME), python3. Services running on 5030/5201/5210/5000.
# Optional env: SEED=1 (apply schemas+seed first, needs mysql client), JMETER_ARGS="-Jwave1.threads=50 -Jhost=..."
set -euo pipefail
cd "$(dirname "$0")"
SCENARIO="${1:-all}"
JMETER="${JMETER_HOME:+$JMETER_HOME/bin/}jmeter"
command -v "$JMETER" >/dev/null || { echo "jmeter not found. Install Apache JMeter 5.6+ and add bin/ to PATH (or set JMETER_HOME)."; exit 1; }

if [[ "${SEED:-0}" == "1" ]]; then
  MYSQL="mysql -h ${DB_HOST:-127.0.0.1} -P ${DB_PORT:-3306} -u ${DB_USER:-root} ${DB_PASSWORD:+-p$DB_PASSWORD}"
  echo ">> applying schemas and seed data"
  $MYSQL -e "CREATE DATABASE IF NOT EXISTS AuthDB"
  $MYSQL AuthDB < ../AuthService/Database/001_CreateUsersTable.sql
  $MYSQL AuthDB < ../AuthService/Database/002_CreateLoginHistoryTable.sql
  for s in LogIntakeService SawmillService TreatmentService; do $MYSQL < "../$s/Database/schema.sql"; done
  $MYSQL < seed/seed-auth.sql
  $MYSQL < seed/seed-data.sql
fi

cd jmeter
mkdir -p results
run() {
  local p="$1"
  echo; echo "=================== JMeter: $p ==================="
  rm -rf "results/$p-report" "results/$p.jtl"
  "$JMETER" -n -t timber-yard-load.jmx -q "profiles/$p.properties" -l "results/$p.jtl" -j "results/$p-jmeter.log" \
            -e -o "results/$p-report" ${JMETER_ARGS:-}
  python3 check_thresholds.py "results/$p.jtl" --profile "$p" --md "results/$p-summary.md"
}
case "$SCENARIO" in
  all) run smoke; run load ;;                 # set -e: a failing smoke stops before load
  smoke|load|stress|spike|soak) run "$SCENARIO" ;;
  *) echo "Unknown scenario '$SCENARIO' (smoke|load|stress|spike|soak|all)"; exit 2 ;;
esac
echo; echo "HTML dashboards: perf/jmeter/results/<scenario>-report/index.html   Summaries: results/*-summary.md"
