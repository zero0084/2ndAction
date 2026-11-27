#!/bin/bash
# 2026-11-28 依頼⑥: BREAK の変更前後の比較(闘技場の診断 -qaBossDiag、ソロ)。変更前 = -dragonBreakMul 1 -rageBreakLock 0
#  使い方: bash Tools/net/brk1128.sh <出力> [chars] [bosses] [build=1|0]
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
OUT=${1:?out}; CHARS=${2:-swordsman,gunslinger}; BOSSES=${3:-W:Dragon,S:Dragon,C:Drake,S:MechDragon,C:Troll,S:Majin}; NOBUILD=${4:-0}
EXE=$ROOT/Unity/2ndAction/Builds/WindowsDevSandbox/OneMoreMile.exe
mkdir -p "$OUT"; OUT=$(cd "$OUT" && pwd)
for ch in ${CHARS//,/ }; do
  for mode in ${MODES:-before after}; do
    case $mode in before) X="-dragonBreakMul 1 -rageBreakLock 0";; dragononly) X="-rageBreakLock 0";; *) X="";; esac
    D=$OUT/${ch}_$mode; mkdir -p $D
    echo "== $ch $mode $(date +%H:%M:%S)"
    timeout ${QTO:-900} "$EXE" -screen-fullscreen 0 -screen-width 960 -screen-height 540 -titleScreen 0 -fileSave "$(cygpath -w $D/save.json)" -qaBossDiag "$(cygpath -w $D)" -qaDiagChar $ch -qaDiagBoss $BOSSES -qaDiagDist 57000 -qaDiagSec 150 -qaDiagNoBuild $NOBUILD -qaDiagReps 2 $X ${XA:-} -logFile "$(cygpath -w $D/run.log)" > /dev/null 2>&1
    grep -a "\[diag result\]\|defeated phase\|BREAK #\| PHASE " $D/run.log | grep -v "^ " | cut -c1-200 | grep "diag result" | head -20
  done
done
