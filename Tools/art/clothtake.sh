#!/bin/bash
# エステルの並べた絵をダウンロードから取り込む: bash Tools/art/clothtake.sh <id> <シート名> <ダウンロードのファイル名>
#  sheets/<名前>_out.png へ写し、比較の画像を scratchpad/take_<id>_<名前>.jpg に作り、切り分け→合わせ→合成を裏で始める(ログは scratchpad/take_<id>_<名前>.log)
cd "$(dirname "$0")/../.." || exit 1
id=$1; nm=$2; f=$3; W=Docs/ClothRevision_2026-10-25/chars/$id; SP=C:/Users/0084k/AppData/Local/Temp/claude/C--GameProject-1stRPG/972e48a6-cec8-40d5-8275-62f59fbcb3de/scratchpad
PY="C:/Users/0084k/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe"
for i in 1 2 3 4 5; do [ -f "/c/Users/0084k/Downloads/$f" ] && break; sleep 1; done
cp "/c/Users/0084k/Downloads/$f" $W/sheets/${nm}_out.png || exit 1
"$PY" Tools/art/clothsheetcmp.py $W/sheets/$nm.png $W/sheets/${nm}_out.png $SP/take_${id}_$nm.jpg
(bash Tools/art/clothchar.sh $id sheet $nm > $SP/take_${id}_$nm.log 2>&1 &)
echo "$SP/take_${id}_$nm.jpg"
