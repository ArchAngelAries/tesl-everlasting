#!/usr/bin/env bash
# Wait for a Run-Audit diagnostic to finish and print a summary (read-only).
# Usage: bash tools/wait_audit.sh <pid>   (run from .analysis/offline-engine)
# Done = match report and engine results are newer than diagnostic-run.json AND the engine results row count has been stable
# for 15 s (the file is written incrementally), or the tracked process exited.
pid="$1"; cd "$(dirname "$0")/.." || exit 1
last=-1; stable=0
for i in $(seq 1 180); do
  if [ exports/original-default-recipe-match.tsv -nt diagnostic-run.json ] && [ exports/engine-test-results.tsv -nt diagnostic-run.json ]; then
    n=$(wc -l < exports/engine-test-results.tsv)
    if [ "$n" = "$last" ]; then stable=$((stable+5)); else stable=0; last=$n; fi
    [ $stable -ge 15 ] && break
  fi
  if ! tasklist //FI "PID eq $pid" | grep -q "$pid"; then echo "process exited"; break; fi
  sleep 5
done
# Never summarize an earlier run after a launch failure or timeout.
for report in engine-test-results.tsv engine-command-test-results.tsv original-default-recipe-match.tsv rule-import-summary.tsv spec-smoke-results.tsv spec-paid-play-results.tsv; do
  if ! [ "exports/$report" -nt diagnostic-run.json ]; then
    echo "ERROR: no fresh $report for this diagnostic; old results are not evidence."
    exit 1
  fi
done
if [ "$stable" -lt 15 ]; then
  echo "ERROR: diagnostic exited or timed out before the reports were verified stable. Inspect scoped logs."
  exit 1
fi
echo "== engine groups"; LC_ALL=C awk -F'\t' 'NR>1{c[$2]++} END{for(k in c) print k, c[k]}' exports/engine-test-results.tsv
LC_ALL=C awk -F'\t' '$2=="FAIL"{print "FAIL " $1 ": " substr($3,1,700)}' exports/engine-test-results.tsv
echo "== commands"; awk -F'\t' 'NR>1{c[$2]++} END{for(k in c) print k, c[k]}' exports/engine-command-test-results.tsv
echo "== summary"; head -4 exports/rule-import-summary.tsv; grep -E "ticks|terminal|winner" exports/original-default-recipe-match.tsv
echo "== spec smoke"; LC_ALL=C awk -F'\t' 'NR>1{print $3}' exports/spec-smoke-results.tsv | sort | uniq -c
echo "== spec paid play"; LC_ALL=C awk -F'\t' 'NR>1{print $3}' exports/spec-paid-play-results.tsv | sort | uniq -c
echo "== spec skips"; awk -F'\t' 'NR>1 && $2=="skipped"{print $3}' exports/spec-emission-report.tsv | sed 's/-\?[0-9]\{4,\}/N/g' | sort | uniq -c | sort -rn | head -12
