#!/bin/bash
# キャラの全コマの古い布を消す(chars/<id>.txt の CLOTH_SPEC と erase_args)。bash Tools/art/clotheraseall.sh <id> ...
cd "$(dirname "$0")/../.." || exit 1
PY="C:/Users/0084k/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe"
for id in "$@"; do CF=Docs/ClothRevision_2026-10-25/chars/$id.txt; W=Docs/ClothRevision_2026-10-25/chars/$id
  export CLOTH_SPEC=$(grep -E '^(ERASE_SPEC|CLOTH_SPEC)=' $CF | sort | tail -1 | cut -d= -f2-); args=$(grep '^erase_args=' $CF | cut -d= -f2-)
  [ -f $W/in/crop.json ] || bash Tools/art/clothchar.sh $id prep > /dev/null 2>&1
  mkdir -p $W/in_erased; for f in $W/in/*_x4.png; do "$PY" Tools/art/clotherase.py $f $W/in_erased/$(basename $f) $args > /dev/null; done
  echo "$id erased $(ls $W/in_erased | wc -l)"; done
