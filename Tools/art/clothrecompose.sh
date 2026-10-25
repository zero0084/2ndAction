#!/bin/bash
# キャラの全コマを aligned から合成し直す(合成の設定を変えた時)。bash Tools/art/clothrecompose.sh <id> ...
# chars/<id>.txt の despeck_frames=… (DESPECK=N) にあるコマはその値で
cd "$(dirname "$0")/../.." || exit 1
for id in "$@"; do W=Docs/ClothRevision_2026-10-25/chars/$id
  ds=$(grep '^despeck_frames=' Docs/ClothRevision_2026-10-25/chars/$id.txt | cut -d= -f2-); dv=$(echo "$ds" | grep -o 'DESPECK=[0-9]*' | cut -d= -f2)
  for f in $(cd $W/aligned && find . -name "*.png" | sed 's#^\./##; s#\.png$##' | sort); do
    if echo " $ds " | grep -q " $f "; then DESPECK=$dv bash Tools/art/clothchar.sh $id compose $f > /dev/null; else bash Tools/art/clothchar.sh $id compose $f > /dev/null; fi
  done; [ -f $W/recolor.txt ] && C:/Users/0084k/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe Tools/art/clothrecolor.py $id > /dev/null; echo "$id recomposed"; done
