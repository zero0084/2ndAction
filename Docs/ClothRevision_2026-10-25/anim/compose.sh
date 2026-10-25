#!/bin/bash
# 合成をやり直す: bash compose.sh <folder/name> ...(clears.txt の範囲指定を足す)
cd "$(dirname "$0")/../../.."
PY="C:/Users/0084k/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe"; W=Docs/ClothRevision_2026-10-25/anim; A=Docs/ClothRevision_2026-10-25/ingame/original/Art  # 元の絵は控えから読む(試作を入れた後でも正しく合成する)
for f in "$@"; do d=${f%%/*}; n=${f##*/}; extra=$(grep -P "^$f\t" $W/clears.txt | cut -f2-); mkdir -p $W/local/$d $W/dbg/$d
  eff=""; case "$d" in PlayerAttack*|PlayerUpAttack*|PlayerDownAttack*) eff="--effects";; esac
  case "$d" in PlayerUpAttack*) eff="$eff --waist 0.8";; esac  # 剣を高く掲げるので、中身の上端は剣先。腰の高さを下げる
  case "$d" in PlayerStart*|PlayerFinish*) eff="$eff --despeck 300";; esac  # 古いマントを抜いてちぎれた粒を消す(第5群から)
  "$PY" Tools/art/clothlocal.py $A/$f.png $W/aligned/$d/$n.png $W/local/$d/$n.png --near 3 --mode cape $eff $extra --debug $W/dbg/$d; done
