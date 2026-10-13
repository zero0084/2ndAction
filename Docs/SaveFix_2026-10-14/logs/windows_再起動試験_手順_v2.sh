#!/bin/bash
# セーブの消失/バックアップ復旧の試験(テスト用データだけ。本当に起動し直す)。1本ずつ順に
SP="C:/Users/0084k/AppData/Local/Temp/claude/C--GameProject-1stRPG/972e48a6-cec8-40d5-8275-62f59fbcb3de/scratchpad"
PY="/c/Users/0084k/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe"
EXE="$SP/x11/OneMoreMile.exe"; OUT="$SP/sr"; rm -rf "$OUT"; mkdir -p "$OUT"
TB="/c/Users/0084k/AppData/LocalLow/DefaultCompany/One More Mile/SaveBackups_test"
"$PY" "$SP/reg_dump.py" "$OUT/normal_before.txt"
# 起動して、結果のファイルが出たら(または240秒)4秒待ち、まだ動いていればその exe だけを止める(Application.Quit が終わらないことがあるため)
launch() { local d=$1 f=$2; shift 2
  "$EXE" -screen-width ${W:-960} -screen-height ${H:-540} -screen-fullscreen 0 -logFile "$d/player.log" "$@" > /dev/null 2>&1 &
  local pid=$! t=0
  while kill -0 $pid 2>/dev/null && [ ! -f "$f" ] && [ $t -lt 240 ]; do sleep 1; t=$((t+1)); done
  sleep 4; kill $pid 2>/dev/null; wait $pid 2>/dev/null; sleep 1; }
run() { local n=$1; shift; local d="$OUT/$n"; rm -rf "$d"; mkdir -p "$d"
  launch "$d" "$d/qa_saverecover.txt" -qaSaveRecover "$d" -srShared "$OUT" "$@"
  echo "== $n ($*)"; grep -a "\[sr\] phase\|\[sr\] before\|\[FAIL\]\|failures=\|EXC\|DATA LOSS\|kept the old" "$d/qa_saverecover.txt" 2>/dev/null | cut -c1-330
  [ -f "$d/qa_saverecover.txt" ] || echo "   (no QA output)"; }
crash() { local n=$1; shift; local d="$OUT/$n"; rm -rf "$d"; mkdir -p "$d"
  launch "$d" "$d/qa_saverecover.txt" -qaSaveRecover "$d" -srShared "$OUT" -srPhase boot "$@"
  echo "== $n ($*) crash: $(grep -a 'DEV CRASH while writing' "$d/player.log" | head -1)"; echo "   files: $(ls "$TB" | grep lastgood | tr '\n' ' ')"; }
run 01_seed -srPhase seed
run 02_boot -srPhase boot -srExpect Existing -srMile 7777 -srWriteExpect 1
run 03_lose -srPhase boot -srLoseAtStart -srExpect RestoredFromBackup -srFrom lastgood -srMile 7777 -srCompare 1
run 04_boot -srPhase boot -srExpect Existing -srMile 7777 -srCompare 1
run 05_boot -srPhase boot -srExpect Existing -srMile 7777 -srCompare 1
run 06_set -srPhase setmile -srValue 8888
run 07_boot -srPhase boot -srExpect Existing -srMile 8888
run 08_corrupt -srPhase boot -srLoseAtStart -srCorrupt lastgood -srExpect RestoredFromBackup -srFrom lastgood_prev -srMile 7777 -srCompare 1 -srDamaged 1
run 09_boot -srPhase boot -srExpect Existing -srMile 7777 -srCompare 1
i=10
for tag in prev-partial prev-tmp cur-partial cur-tmp; do
  A=$((1000+i)); B=$((5000+i))
  run ${i}a_set -srPhase setmile -srValue $A
  run ${i}b_boot -srPhase boot -srExpect Existing -srMile $A
  run ${i}c_set -srPhase setmile -srValue $B
  crash ${i}d_crash -qaBackupCrashAt $tag
  run ${i}e_lose -srPhase boot -srLoseAtStart -srExpect RestoredFromBackup -srFrom lastgood -srMile $A -srCompare 1
  run ${i}f_boot -srPhase boot -srExpect Existing -srMile $A -srCompare 1
  i=$((i+1))
done
run 20_bothbad -srPhase boot -srLoseAtStart -srCorrupt both -srExpect NewGame -srMile 0 -srDamaged 1
run 21_boot -srPhase boot -srExpect Existing -srMile 0
run 22_seed -srPhase seed
run 23_boot -srPhase boot -srExpect Existing -srMile 7777
run 24_reset -srPhase intentreset
run 25_boot -srPhase boot -srExpect Existing -srMile 0
d="$OUT/26_cc"; CCB="$d/cc_baseline.json"; rm -rf "$d"; mkdir -p "$d"
launch "$d" "$d/qa_commitcrash.txt" -qaCommitCrash "$d" -ccPhase run -qaCrashAt journal -qaCrashFlush 0
echo "== 26_cc (development run, crash after the journal) crashed=$(grep -ac 'DEV CRASH' "$d/player.log") journalInTestData=$("$PY" "$SP/reg_has.py" 'test:PendingCommitV1') journalInNormalData=$("$PY" "$SP/reg_has.py" 'PendingCommitV1')"
run 27_release -srPhase release -qaReleaseGen 1 -ccBase "$CCB"
rm -rf "$OUT/test_backups_after_release"; cp -r "$TB" "$OUT/test_backups_after_release"
run 28_release2 -srPhase release2 -qaReleaseGen 1 -ccBase "$CCB"
run 29_normal -srPhase normal
run 30_boot -srPhase boot -srExpect Existing -srNormal 1
run 31_boot -srPhase boot -srExpect Existing -srNormal 1
rm -rf "$OUT/test_backups_final"; cp -r "$TB" "$OUT/test_backups_final"
run 33_seed -srPhase seed
run 34_boot -srPhase boot -srExpect Existing -srMile 7777 -srWriteExpect 1
W=480 H=1040 run 35_lose_portrait -srPhase boot -srLoseAtStart -srExpect RestoredFromBackup -srFrom lastgood -srMile 7777 -srCompare 1
W=480 H=1040 run 36_set_portrait -srPhase setmile -srValue 8888
W=480 H=1040 run 37_boot_portrait -srPhase boot -srExpect Existing -srMile 8888
W=480 H=1040 run 38_corrupt_portrait -srPhase boot -srLoseAtStart -srCorrupt lastgood -srExpect RestoredFromBackup -srFrom lastgood_prev -srMile prev -srNotMile 8888 -srCompare 1 -srDamaged 1
run 32_cleanup -srPhase cleanup
echo "== normal data check:"; "$PY" "$SP/reg_check.py" "$OUT/normal_before.txt"
"$PY" "$SP/reg_dump.py" "$OUT/normal_after.txt"
echo "== normal data (non-test registry values) differences:"; diff "$OUT/normal_before.txt" "$OUT/normal_after.txt" | cut -c1-200
echo "== totals: $(grep -ah 'failures=' "$OUT"/*/qa_saverecover.txt | sort | uniq -c | tr '\n' ' ')"
