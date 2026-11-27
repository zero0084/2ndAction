#!/bin/bash
# 4人マルチの通常条件の通し試験(2026-11-27)。HOST 1 + 参加者 3 を同じ PC の 4 プロセスで(開発版の Windows ビルド、試験専用の保存ファイル)。
#  使い方: bash Tools/net/nat4.sh <出力フォルダ> local|online coop|versus <上限(分)> [restart]
#   ・保存: <出力>/saves/p1..p4.json(-fileSave。普段のセーブ/レジストリに触れない)。初回は -netAutoNatSeed で試験の前の準備
#   ・操作: -netAutoNatural 1..4(仮想のパッドで本物の入力の経路。無敵/HP補充/速度/ワープ/強制撃破なし)
#   ・人ごとの追加の引数: 環境変数 P1..P4(例 P3="-netAutoNatEscapeAt 1500")、全員: COMMON、HOST だけ: HOSTX
#   ・SAVES=<フォルダ> で前の保存を使い続ける(2回目の試験)。restart を付けると、起動してホームで保存の値を出すだけ(再起動後の照合)
#   ・ONLINE は UGS development の Relay(-ugsProfile qanat_<n> で4人の別の匿名プレイヤー。公開ランキングはマルチでは送らない)
#  終了は自分が起動した PID だけ。KILLP<n>=秒 で その人のプロセスを強制終了(通信の切断の試験)
OUT=${1:?out}; NET=${2:-local}; GM=${3:-coop}; MAXMIN=${4:-20}; MODE=${5:-run}
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
EXE=${EXE:-$ROOT/Unity/2ndAction/Builds/WindowsDevSandbox/OneMoreMile.exe}
mkdir -p "$OUT"; OUT=$(cd "$OUT" && pwd); SAVES=${SAVES:-$OUT/saves}; mkdir -p "$SAVES"; SAVES=$(cd "$SAVES" && pwd); for n in 1 2 3 4; do mkdir -p "$SAVES/p$n"; done # 人ごとのフォルダ(同じフォルダだとバックアップ SaveBackups を共有して、新しい保存が他の人の控えから戻される) # 絶対パス(相対だとゲームのデータのフォルダの下に作られ、前の試験の保存が残る)
W() { cygpath -w "$1"; }
if [ "$MODE" = restart ]; then
  pids=()
  for n in 1 2 3 4; do
    "$EXE" -screen-fullscreen 0 -screen-width 480 -screen-height 270 -titleScreen 0 -fileSave "$(W $SAVES/p$n/save.json)" -netAutoHost -netAutoNatural $n -netAutoNatSnapOnly 1 -logFile "$(W $OUT/restart_p$n.log)" > /dev/null 2>&1 &
    pids+=($!); sleep 2
  done
  for p in "${pids[@]}"; do wait $p; done
  grep -a -h "NATSNAP tag=restart" "$OUT"/restart_p*.log | sed 's/.*\[NETTEST\] utc=[^ ]* //'
  exit 0
fi
BASE="-titleScreen 0 -netAutoNatDoneDir $(cygpath -w $OUT) -netAutoMode $GM -netAutoStage wasteland_road -netAutoWaitPlayers 150 -netAutoNatMaxMin $MAXMIN -netAutoNatSeed 1 -netAutoRunSeconds 99999 $COMMON"
if [ "$NET" = online ]; then ROOM=QANAT$RANDOM; HNET="-netAutoOnline -netAutoRoom $ROOM -ugsProfile qanat_1"; GAP=8
else HNET=""; GAP=4; fi
pids=(); names=()
"$EXE" -screen-fullscreen 0 -screen-width 640 -screen-height 360 -fileSave "$(W $SAVES/p1/save.json)" -netAutoHost $HNET -netAutoPlayers 4 -netAutoStartDelay $((GAP * 4 + 15)) -netAutoNatural 1 $BASE $HOSTX $P1 -logFile "$(W $OUT/p1_host.log)" > /dev/null 2>&1 &
pids+=($!); names+=(p1)
if [ "$NET" = online ]; then for w in $(seq 1 60); do grep -aq "Session created" "$OUT/p1_host.log" 2>/dev/null && break; sleep 3; done; echo "host room: $(grep -a -o "ONLINE CREATE ok=[A-Za-z]* in [0-9.]*s" "$OUT/p1_host.log" | head -1)"; fi
for n in 2 3 4; do
  sleep $GAP
  if [ "$NET" = online ]; then JNET="-netAutoJoin online -netAutoOnline -netAutoRoom $ROOM -ugsProfile qanat_$n"; else JNET="-netAutoJoin 127.0.0.1"; fi
  eval "PX=\$P$n"
  "$EXE" -screen-fullscreen 0 -screen-width 640 -screen-height 360 -fileSave "$(W $SAVES/p$n/save.json)" $JNET -netAutoNatural $n $BASE $PX -logFile "$(W $OUT/p${n}_join.log)" > /dev/null 2>&1 &
  pids+=($!); names+=(p$n)
done
wp=(); for p in "${pids[@]}"; do w=$(cat /proc/$p/winpid 2>/dev/null); wp+=(${w:-0}); done
echo "pids(bash) ${pids[*]} / windows ${wp[*]}" > "$OUT/pids.txt"
LIMIT=$((MAXMIN * 60 + 600)); t=0
killed=()
while [ $t -lt $LIMIT ]; do
  alive=0; for p in "${pids[@]}"; do kill -0 $p 2>/dev/null && alive=1; done
  [ $alive -eq 0 ] && break
  for n in 1 2 3 4; do eval "K=\$KILLP$n"; if [ -n "$K" ] && [ $t -ge $K ] && [ -z "${killed[$n]}" ]; then killed[$n]=1; i=$((n-1)); echo "t=$t force-kill p$n (windows pid ${wp[$i]})" | tee -a "$OUT/kills.txt"; taskkill //F //PID ${wp[$i]} > /dev/null 2>&1; fi; done
  sleep 5; t=$((t+5))
done
for p in "${pids[@]}"; do kill $p 2>/dev/null; done
echo "== nat4 ($NET $GM) after ${t}s"
for f in "$OUT"/p*_*.log; do
  s=$(grep -a -h "SUMMARY reason" "$f" | tail -1 | sed 's/.*\[NETTEST\] utc=[^ ]* //' | cut -c1-120)
  echo "$(basename $f .log): ${s:-NO SUMMARY} | Exception $(grep -a -c Exception "$f")"
  grep -a -h "NATRESULT" "$f" | sed 's/.*\[NETTEST\] utc=[^ ]* //' | cut -c1-330
done
