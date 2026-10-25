#!/bin/bash
# 布表現の見直し(2026-10-25〜、全12人): キャラ1人ぶんの作業をまとめる。設定は Docs/ClothRevision_2026-10-25/chars/<id>.txt
#   bash Tools/art/clothchar.sh <id> backup            … Assets/Art の <prefix>* の PNG と .meta を ingame/original/Art へ控える(既にあれば上書きしない)
#   bash Tools/art/clothchar.sh <id> prep [folder ...]  … 元のコマを4倍+マゼンタにして chars/<id>/in/ へ(既定は全フォルダ)
#   bash Tools/art/clothchar.sh <id> fit <folder/name> [clothframe の追加の引数]  … chars/<id>/out/<folder>__<name>_a.png を元のコマへ合わせる → aligned/
#   bash Tools/art/clothchar.sh <id> compose <folder/name> ...  … 体は元の画素、布だけ新しい絵の合成 → local/(範囲指定は chars/<id>/clears.txt)
#   bash Tools/art/clothchar.sh <id> sheet <名前> [suffix]  … sheets/<名前>_out.png(エステルの並べた絵)を切り分けて fit と compose
#   bash Tools/art/clothchar.sh <id> candidate          … local/ の合成を ingame/candidate/Art へ(切り替えは cloth_swordsman.sh apply)
# 元の絵は常に ingame/original/Art の控えから読む(試作を入れた後の Assets から読まない)。
cd "$(dirname "$0")/../.." || exit 1
PY="C:/Users/0084k/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe"
id=$1; cmd=$2; shift 2
D=Docs/ClothRevision_2026-10-25; CF=$D/chars/$id.txt; W=$D/chars/$id
[ -f "$CF" ] || { echo "no $CF"; exit 1; }
prefix=$(grep '^prefix=' "$CF" | cut -d= -f2); export CLOTH_SPEC=$(grep '^CLOTH_SPEC=' "$CF" | cut -d= -f2-)
export CLOTH_MODE=$(grep "^CLOTH_MODE=" "$CF" | cut -d= -f2-)
extra_all=$(grep '^compose_args=' "$CF" | cut -d= -f2-)
A=Unity/2ndAction/Assets/Art; O=$D/ingame/original/Art
folders() { (cd $O && find . -path "./${prefix}*" -name "*.png" | sed 's#^\./##' | sed 's#/[^/]*$##' | sort -u); }
mkdir -p $W/in $W/out $W/aligned $W/local $W/dbg $W/review
case "$cmd" in
  backup)
    n=0; for f in $(cd $A && find . -path "./${prefix}*" -name "*.png" | sed 's#^\./##'); do
      mkdir -p "$O/$(dirname $f)"; [ -f "$O/$f" ] || { cp "$A/$f" "$O/$f"; cp "$A/$f.meta" "$O/$f.meta"; n=$((n+1)); }; done
    for f in UI/Characters/${id}_main.png UI/Characters/${id}_card.png; do mkdir -p "$O/UI/Characters"; [ -f "$O/$f" ] || { git show HEAD:Unity/2ndAction/Assets/Art/$f > "$O/$f"; cp "$A/$f.meta" "$O/$f.meta"; n=$((n+1)); }; done
    echo "backup: $n new files" ;;
  prep)
    sel=${*:-$(folders)}
    for fo in $sel; do "$PY" Tools/art/clothprep.py $W/in $O/$fo/*.png > /dev/null; done; ls $W/in/*_x4.png | wc -l ;;
  fit)
    fn=$1; shift; fo=${fn%/*}; n=${fn##*/}; mkdir -p $W/aligned/$fo
    src=$(ls -t $W/out/${fo##*/}__${n}_?.png 2>/dev/null | head -1)  # clothprep の名前は「フォルダの最後の名前__コマ」。一番新しい版(_a/_b...)
    FS=$(grep "^FIT_SPEC=" "$CF" | cut -d= -f2-); grep -q "^FIT_PREFER_RESIZE=1" "$CF" && export FIT_PREFER_RESIZE=1; CLOTH_SPEC=${FS:-$CLOTH_SPEC} "$PY" Tools/art/clothframe.py "$src" $O/$fn.png $W/in/crop.json $W/aligned/$fo/$n.png --fit-auto --fit-left $(echo "$@" | grep -q -- "--scales" || echo "--scales-rel 0.88,1.12,0.004") "$@" | grep -E "^  ->|overflow" ;;
  compose)
    for fn in "$@"; do fo=${fn%/*}; n=${fn##*/}; ex=$(grep -P "^$fn\t" $W/clears.txt 2>/dev/null | cut -f2-); mkdir -p $W/local/$fo $W/dbg/$fo
      "$PY" Tools/art/clothlocal.py $O/$fn.png $W/aligned/$fo/$n.png $W/local/$fo/$n.png --near 3 --mode ${CLOTH_MODE:-cape} --despeck ${DESPECK:-800} $extra_all $ex --debug $W/dbg/$fo | tail -1; done ;;
  sheet)
    # エステルが描いた並べた絵 <sheets/名前_out.png> を切り分け → 合わせ → 合成まで(並びは sheets/名前.json)
    nm=$1; suf=${2:-a}
    "$PY" Tools/art/clothsheet2.py split $W/sheets/${nm}_out.png $W/sheets/${nm}.json $W/out $suf
    for k in $("$PY" -c "import json;print(' '.join(c['key'] for c in json.load(open('$W/sheets/${nm}.json'))['cells']))"); do
      fo=$(cd $O && ls -d ${k%%__*} 2>/dev/null || find . -path "./${prefix}*" -type d -name "${k%%__*}" | sed 's#^\./##' | head -1); n=${k##*__}
      bash "$0" $id fit $fo/$n --scales ${SHEET_SCALES:-0.8,1.25,0.005} && bash "$0" $id compose $fo/$n; done ;;
  candidate)
    n=0; for f in $(cd $W/local && find . -name "*.png" | sed 's#^\./##'); do mkdir -p "$D/ingame/candidate/Art/$(dirname $f)"; cp "$W/local/$f" "$D/ingame/candidate/Art/$f"; n=$((n+1)); done
    for f in main card; do [ -f $W/${id}_$f.png ] && { mkdir -p $D/ingame/candidate/Art/UI/Characters; cp $W/${id}_$f.png $D/ingame/candidate/Art/UI/Characters/; n=$((n+1)); }; done
    echo "candidate: $n files" ;;
  *) echo "usage: $0 <id> backup|prep|fit|compose|sheet|candidate"; exit 1 ;;
esac
