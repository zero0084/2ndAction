#!/bin/bash
# 3人の同時の確認(2026-11-25 第3)。開発版の Windows ビルドを3つ起動し、TEAMLOG を team3.py で比べる。
#  使い方: bash Tools/net/team3.sh TAG coop|versus RUNSEC "共通" "P1(HOST)" "P2" "P3" [OUTDIR]
#  ・保存は -memSave。LOCAL(127.0.0.1)だけ
TAG=$1; GM=$2; RUNSEC=$3; COMMON=$4; A1=$5; A2=$6; A3=$7
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
EXE=${EXE:-$ROOT/Unity/2ndAction/Builds/WindowsDevSandbox/OneMoreMile.exe}
OUT=${8:-$ROOT/Docs/Multiplayer_2026-11-25/runs3}
D=$OUT/$TAG; rm -rf "$D"; mkdir -p "$D"
BASE="-netAutoMode $GM -netAutoStage wasteland_road -netAutoRunSeconds $RUNSEC -netAutoTeamLog 1 -netAutoBossHp 400000"
pids=()
"$EXE" -screen-fullscreen 0 -screen-width 800 -screen-height 450 -memSave -netAutoHost -netAutoPlayers 3 -netAutoStartDelay 12 $BASE $COMMON $A1 -logFile "$(cygpath -w $D/p1.log)" > /dev/null 2>&1 &
pids+=($!); sleep 4
"$EXE" -screen-fullscreen 0 -screen-width 800 -screen-height 450 -memSave -netAutoJoin 127.0.0.1 $BASE $COMMON $A2 -logFile "$(cygpath -w $D/p2.log)" > /dev/null 2>&1 &
pids+=($!); sleep 3
"$EXE" -screen-fullscreen 0 -screen-width 800 -screen-height 450 -memSave -netAutoJoin 127.0.0.1 $BASE $COMMON $A3 -logFile "$(cygpath -w $D/p3.log)" > /dev/null 2>&1 &
pids+=($!)
LIMIT=$((RUNSEC + 240)); t=0
while [ $t -lt $LIMIT ]; do
  alive=0; for p in "${pids[@]}"; do kill -0 $p 2>/dev/null && alive=1; done
  [ $alive -eq 0 ] && break
  sleep 5; t=$((t+5))
done
for p in "${pids[@]}"; do kill $p 2>/dev/null; done   # この試験で起動した3つだけ(番号で)
echo "== $TAG ($GM, 3 players) after ${t}s"
PY=${PY:-/c/Users/0084k/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe}
"$PY" -I "$ROOT/Tools/net/team3.py" "$D" "$GM"
