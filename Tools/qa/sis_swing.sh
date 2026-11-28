#!/bin/bash
# 2026-11-28 依頼⑦: 近接の振りの出し直し(EnableSwing)を入れた版の確認
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
D=$ROOT/Docs/Sisters_2026-11-28
U="/c/Program Files/Unity/Hub/Editor/6000.6.10f1/Editor/Unity.exe"
P=$ROOT/Unity/2ndAction
W() { cygpath -w "$1"; }
step() { echo "######## $1 $(date +%H:%M:%S)"; }
step "build dev"
"$U" -batchmode -quit -projectPath "$(W $P)" -executeMethod BuildWindowsProductSandbox.BuildDev -logFile "$(W $D/build_swing_dev.log)"
grep -a "result=\|error CS" "$D/build_swing_dev.log" | head -5
grep -aq "result=Succeeded" "$D/build_swing_dev.log" || { echo "DEV BUILD FAILED"; exit 1; }
step "solo hit, swing fix ON"
SECS=20 bash "$ROOT/Tools/qa/sishit.sh" "$D/swing_on" swordsman
step "solo hit, swing fix OFF (before)"
SECS=20 EXTRA="-swingCycle 0" bash "$ROOT/Tools/qa/sishit.sh" "$D/swing_off" swordsman
step "solo kill + YES"
TMO=1800 VIEWS="land" EXTRA="-shKill 1 -ldPower 6 -shAfter yes" bash "$ROOT/Tools/qa/sishit.sh" "$D/swing_kill" gunslinger
step "multi LOCAL coop (all cards Lv9)"
cd "$ROOT" && bash Tools/net/sis4.sh G1_local_coop_swingfix local coop 16
step "all done"
