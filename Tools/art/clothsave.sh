#!/bin/bash
# エステルの並べた絵を取り込むだけ(処理はしない): bash Tools/art/clothsave.sh <id> <out.jpg> <シート名:ダウンロード名> ...
#  sheets/<名前>_out.png へ写し、入力と出力の比較を縦に並べて <out.jpg> へ
cd "$(dirname "$0")/../.." || exit 1
id=$1; out=$2; shift 2; W=Docs/ClothRevision_2026-10-25/chars/$id
PY="C:/Users/0084k/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe"; SP=C:/Users/0084k/AppData/Local/Temp/claude/C--GameProject-1stRPG/972e48a6-cec8-40d5-8275-62f59fbcb3de/scratchpad
ims=""
for p in "$@"; do nm=${p%%:*}; f=${p#*:}
  for i in 1 2 3 4 5; do [ -f "/c/Users/0084k/Downloads/$f" ] && break; sleep 1; done
  cp "/c/Users/0084k/Downloads/$f" $W/sheets/${nm}_out.png && "$PY" Tools/art/clothsheetcmp.py $W/sheets/$nm.png $W/sheets/${nm}_out.png $SP/cmp_$nm.jpg && ims="$ims $SP/cmp_$nm.jpg"; done
"$PY" -c "
import sys
from PIL import Image
ims=[Image.open(p) for p in sys.argv[2:]]
M=Image.new('RGB',(max(i.width for i in ims),sum(i.height+10 for i in ims)))
y=0
for i in ims: M.paste(i,(0,y)); y+=i.height+10
M.thumbnail((2000,2400)); M.save(sys.argv[1],quality=85)" $out $ims
echo $out
