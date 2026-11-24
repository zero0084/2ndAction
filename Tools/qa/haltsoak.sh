#!/bin/bash
# ボス戦の停止戦の見張り(2026-11-25)。開発版の Windows ビルドで実際のラン(-qaFullRun、ワープなし・補助あり・無敵)を
# 走らせ、[HaltWatch] の行(止まれず走り続けた/届かないまま止まった/ボスがいないのに止まった/前へずれた)を集める。
#  使い方: bash Tools/qa/haltsoak.sh OUTDIR "stage:char stage:char ..." [STOP_AT_M] [TIMESCALE]
#  例:     bash Tools/qa/haltsoak.sh /tmp/hs "wasteland_road:swordsman natural_cave:dualblade" 40000 3
#  ・保存は -memSave(通常のセーブに触れない)。同時に動かすのは 2 本まで
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
EXE=${EXE:-$ROOT/Unity/2ndAction/Builds/WindowsDevSandbox/OneMoreMile.exe}
OUT=$1; CASES=$2; STOP=${3:-40000}; TS=${4:-3}; EXTRA=${EXTRA:-}
mkdir -p "$OUT"
pids=()
for c in $CASES; do
  st=${c%%:*}; ch=${c##*:}; D=$OUT/${st}_${ch}; mkdir -p "$D"
  "$EXE" -screen-fullscreen 0 -screen-width 960 -screen-height 540 -memSave -qaFullRun "$(cygpath -w $D)" -qaStage $st -qaChar $ch -qaKmh 0 -qaTimeScale $TS -qaStopAt $STOP $EXTRA -logFile "$(cygpath -w $D/log.txt)" > /dev/null 2>&1 &
  pids+=($!)
  sleep 3
done
for p in "${pids[@]}"; do wait $p; done
for c in $CASES; do
  st=${c%%:*}; ch=${c##*:}; D=$OUT/${st}_${ch}
  echo "== $st/$ch"
  grep -h "\[HaltWatch\]\|stop at \|player stuck\|NOT defeated" "$D/log.txt" | sed 's/^.*\[HaltWatch\]/[HaltWatch]/' | cut -c1-260
  echo "holds: $(grep -c '\[BossHalt\] hold' "$D/log.txt") releases: $(grep -c '\[BossHalt\] release' "$D/log.txt")"
done
