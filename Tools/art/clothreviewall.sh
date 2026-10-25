#!/bin/bash
# キャラのアニメ全コマの 元/修正 を2枚(review/anim_a.jpg, anim_b.jpg)に。bash Tools/art/clothreviewall.sh <id> ... [--h 150]
cd "$(dirname "$0")/../.." || exit 1
PY="C:/Users/0084k/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe"; D=Docs/ClothRevision_2026-10-25; H=150
for id in "$@"; do W=$D/chars/$id; mkdir -p $W/review
  fo=$(cd $W/local && find . -name "*.png" | sed 's#^\./##; s#/[^/]*$##' | sort -u); n=$(echo "$fo" | wc -l)
  "$PY" Tools/art/clothreview.py $D/ingame/original/Art $W/local $W/review/anim_a.jpg $(echo "$fo" | head -$(( (n+1)/2 ))) --h $H
  "$PY" Tools/art/clothreview.py $D/ingame/original/Art $W/local $W/review/anim_b.jpg $(echo "$fo" | tail -n +$(( (n+1)/2 + 1 ))) --h $H
done
