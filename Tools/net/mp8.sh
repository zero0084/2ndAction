#!/bin/bash
# 同じ PC で HOST 1 + JOIN (N-1) の負荷・メモリ試験(2026-11-26、Unity 6000.6.10f1 の確認用。2026-10-12 の mp8.sh の作り直し)。
#  使い方: bash Tools/net/mp8.sh <出力フォルダ> [人数 8] [走る秒 90] [local|online] [coop|versus]
#  ・保存は -memSave(通常のセーブに触れない)。ONLINE は UGS development の Relay(-ugsProfile で人数分の別の匿名プレイヤー)
#  ・メモリ: 各プロセスの作業セット(MB)を30秒ごと。見張り・終了は自分が起動した PID だけ
OUT=${1:?out}; N=${2:-8}; RUNSEC=${3:-90}; NET=${4:-local}; GM=${5:-coop}
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
EXE=${EXE:-$ROOT/Unity/2ndAction/Builds/WindowsDevSandbox/OneMoreMile.exe}
rm -rf "$OUT"; mkdir -p "$OUT"
BASE="-netAutoMode $GM -netAutoStage wasteland_road -netAutoRunSeconds $RUNSEC -netAutoBossHp 400000 -netAutoWaitPlayers 150"
if [ "$NET" = online ]; then ROOM=QAMP8$RANDOM; HNET="-netAutoOnline -netAutoRoom $ROOM -ugsProfile qamp8_0"; GAP=${GAP:-8}
else HNET=""; GAP=4; fi
pids=()
"$EXE" -screen-fullscreen 0 -screen-width 640 -screen-height 360 -memSave -netAutoHost $HNET -netAutoPlayers $N -netAutoStartDelay $((GAP * N + 15)) $BASE -logFile "$(cygpath -w $OUT/p0_host.log)" > /dev/null 2>&1 &
pids+=($!)
# ONLINE: 部屋ができてから JOIN を起動する(同時に起動すると HOST の Relay の準備が遅れる)
if [ "$NET" = online ]; then for w in $(seq 1 60); do grep -aq "Session created" "$OUT/p0_host.log" 2>/dev/null && break; sleep 3; done; echo "host room: $(grep -a -o "ONLINE CREATE ok=[A-Za-z]* in [0-9.]*s" "$OUT/p0_host.log" | head -1)"; fi
for i in $(seq 1 $((N - 1))); do
  sleep $GAP
  if [ "$NET" = online ]; then JNET="-netAutoJoin online -netAutoOnline -netAutoRoom $ROOM -ugsProfile qamp8_$i"; else JNET="-netAutoJoin 127.0.0.1"; fi
  "$EXE" -screen-fullscreen 0 -screen-width 640 -screen-height 360 -memSave $JNET $BASE -logFile "$(cygpath -w $OUT/p${i}_join.log)" > /dev/null 2>&1 &
  pids+=($!)
done
# bash の PID → Windows の PID
wp=(); for p in "${pids[@]}"; do w=$(cat /proc/$p/winpid 2>/dev/null); wp+=(${w:-0}); done
echo "sec,${wp[*]}" | tr ' ' ',' > "$OUT/mem.csv"
LIMIT=$((RUNSEC + GAP * N + 240)); t=0
while [ $t -lt $LIMIT ]; do
  alive=0; for p in "${pids[@]}"; do kill -0 $p 2>/dev/null && alive=1; done
  [ $alive -eq 0 ] && break
  if [ $((t % 30)) -eq 0 ]; then
    row="$t"; for w in "${wp[@]}"; do kb=$(tasklist //FI "PID eq $w" //FO CSV //NH 2>/dev/null | awk -F'","' '{gsub(/[^0-9]/,"",$5); print $5}'); row="$row,$(( ${kb:-0} / 1024 ))"; done
    echo "$row" >> "$OUT/mem.csv"
  fi
  sleep 5; t=$((t+5))
done
for p in "${pids[@]}"; do kill $p 2>/dev/null; done
echo "== mp $N players ($NET $GM) after ${t}s"
for f in "$OUT"/p*.log; do
  s=$(grep -a -h "SUMMARY reason" "$f" | tail -1 | sed 's/.*\[NETTEST\] utc=[^ ]* //' | cut -c1-170)
  echo "$(basename $f .log): ${s:-NO SUMMARY} | Exception lines $(grep -a -c Exception "$f")"
done
grep -a -h -o "players=[0-9]*/[0-9]*" "$OUT/p0_host.log" | sort -u | tail -2
echo "-- memory MB (columns = processes, first is HOST)"; cat "$OUT/mem.csv"
