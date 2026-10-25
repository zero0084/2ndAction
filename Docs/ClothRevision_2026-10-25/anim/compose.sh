#!/bin/bash
# 合成をやり直す: bash compose.sh <folder/name> ...(clears.txt の範囲指定を足す)
cd "$(dirname "$0")/../../.."
PY="C:/Users/0084k/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe"; W=Docs/ClothRevision_2026-10-25/anim; A=Unity/2ndAction/Assets/Art
for f in "$@"; do d=${f%%/*}; n=${f##*/}; extra=$(grep -P "^$f\t" $W/clears.txt | cut -f2-); mkdir -p $W/local/$d $W/dbg/$d
  "$PY" Tools/art/clothlocal.py $A/$f.png $W/aligned/$d/$n.png $W/local/$d/$n.png --near 3 --mode cape $extra --debug $W/dbg/$d; done
