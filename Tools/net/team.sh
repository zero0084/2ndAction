#!/bin/bash
# マルチの止まり方・チーム走行距離の2台の自動確認(2026-11-24)。開発版の Windows ビルドを2つ起動して、記録を teamrun.py で比べる。
#  使い方: bash Tools/net/team.sh TAG local|online coop|versus RUNSEC "共通の引数" "HOST だけの引数" "JOIN だけの引数" [OUTDIR]
#  例:     bash Tools/net/team.sh A local coop 55 "-netAutoLead host -netAutoLeadGap 80" "-netAutoBossAt 20 -netAutoRwKills 30:boss -netAutoHoldChoiceSec 8" ""
#  ・保存は -memSave(通常のセーブに触れない)。ONLINE は UGS development の Relay(-ugsProfile で別の匿名プレイヤー)
#  ・変更前の動きとの比べ: 共通の引数に -netLegacyStops 1
TAG=$1; NET=$2; GM=$3; RUNSEC=$4; COMMON=$5; HARGS=$6; JARGS=$7
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
EXE=${EXE:-$ROOT/Unity/2ndAction/Builds/WindowsDevSandbox/OneMoreMile.exe}
OUT=${8:-$ROOT/Docs/Multiplayer_2026-11-24/runs}
D=$OUT/$TAG; rm -rf "$D"; mkdir -p "$D"
BASE="-netAutoMode $GM -netAutoStage wasteland_road -netAutoRunSeconds $RUNSEC -netAutoTeamLog 1 -netAutoBossHp 400000"
if [ "$NET" = online ]; then
  ROOM=QATEAM$RANDOM
  HNET="-netAutoOnline -netAutoRoom $ROOM -ugsProfile qateam1"; JNET="-netAutoJoin online -netAutoOnline -netAutoRoom $ROOM -ugsProfile qateam2"; JGAP=20
else
  HNET=""; JNET="-netAutoJoin 127.0.0.1"; JGAP=4
fi
pids=()
"$EXE" -screen-fullscreen 0 -screen-width 960 -screen-height 540 -memSave -netAutoHost $HNET -netAutoPlayers 2 -netAutoStartDelay 10 $BASE $COMMON $HARGS -logFile "$(cygpath -w $D/host.log)" > /dev/null 2>&1 &
pids+=($!)
sleep $JGAP
"$EXE" -screen-fullscreen 0 -screen-width 960 -screen-height 540 -memSave $JNET $BASE $COMMON $JARGS -logFile "$(cygpath -w $D/join.log)" > /dev/null 2>&1 &
pids+=($!)
LIMIT=$((RUNSEC + 240)); t=0
while [ $t -lt $LIMIT ]; do
  alive=0; for p in "${pids[@]}"; do kill -0 $p 2>/dev/null && alive=1; done
  [ $alive -eq 0 ] && break
  sleep 5; t=$((t+5))
done
for p in "${pids[@]}"; do kill $p 2>/dev/null; done   # この試験で起動した2つだけ(番号で)
echo "== $TAG ($NET $GM) after ${t}s"
grep -h "SUMMARY reason" $D/host.log $D/join.log | sed 's/.*\[NETTEST\] utc=[^ ]* //' | cut -c1-200
PY=${PY:-/c/Users/0084k/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe}
"$PY" -I "$ROOT/Tools/net/teamrun.py" "$D" "$GM"
