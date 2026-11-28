#!/bin/bash
# 2026-11-28 依頼⑦: 死神三姉妹の命中を 横 / 縦(横から見る・上下2段・斜め上から見る)で比べる(1 プロセスずつ)
#  使い方: bash Tools/qa/sishit.sh <出力フォルダ> [キャラ...](省略で swordsman gunslinger mage lancer)
#   VIEWS="land side fold oblique"  SECS=30  EXTRA="追加の引数"
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
OUT=${1:?out}; shift; mkdir -p "$OUT"; OUT=$(cd "$OUT" && pwd)
CHARS=${*:-swordsman gunslinger mage lancer}
VIEWS=${VIEWS:-land side fold oblique}
EXE=${EXE:-$ROOT/Unity/2ndAction/Builds/WindowsDevSandbox/OneMoreMile.exe}
W() { cygpath -w "$1"; }
for ch in $CHARS; do
  for v in $VIEWS; do
    d=$OUT/${ch}_$v; mkdir -p "$d/save"
    if [ "$v" = land ]; then SW=960; SH=540; else SW=540; SH=960; fi
    echo "=== $ch $v $(date +%H:%M:%S)"
    timeout ${TMO:-900} "$EXE" -screen-fullscreen 0 -screen-width $SW -screen-height $SH -titleScreen 0 -fileSave "$(W $d/save/save.json)" \
      -ldQa "$(W $d)" -ldQaMode ${MODE:-sishit} -ldChar $ch -shView $v -shSecs ${SECS:-30} $EXTRA -logFile "$(W $d/run.log)" > /dev/null 2>&1
    grep -a -h "attacks\|time \|FAIL\|PASSED\|EXCEPTIONS\|sishit\] char" "$d"/ld_*.txt 2>/dev/null | cut -c1-220
  done
done
echo "=== done $(date +%H:%M:%S)"
