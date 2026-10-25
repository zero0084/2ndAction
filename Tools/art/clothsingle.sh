#!/bin/bash
# 1コマずつ描き直した絵を取り込む: bash Tools/art/clothsingle.sh <id> <folder/name> <ダウンロード名> [suffix=b]
#  out/<フォルダの最後>__<名前>_<suffix>.png へ写し、fit(倍率は幅の比から)と compose
cd "$(dirname "$0")/../.." || exit 1
id=$1; fn=$2; f=$3; suf=${4:-b}; W=Docs/ClothRevision_2026-10-25/chars/$id; fo=${fn%/*}; n=${fn##*/}
for i in 1 2 3 4 5; do [ -f "/c/Users/0084k/Downloads/$f" ] && break; sleep 1; done
cp "/c/Users/0084k/Downloads/$f" $W/out/${fo##*/}__${n}_$suf.png && bash Tools/art/clothchar.sh $id fit $fn && bash Tools/art/clothchar.sh $id compose $fn
