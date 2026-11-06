#!/bin/bash
# cardart.sh <id> [cy=0.47] : Downloads/<id>_v*.png(最新)→ ArtSource_cards へ移し、iconprep で CardIcons/<id>.png と確認画像(ArtSource_cards/prev_<id>.png)を作る
set -e
ID=$1; CY=${2:-0.47}
PY=/c/Users/0084k/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe
SRC=$(ls -t /c/Users/0084k/Downloads/${ID}_v*.png 2>/dev/null | head -1)
A=/c/GameProject/ArtSource_cards
if [ -n "$SRC" ]; then mv "$SRC" $A/; SRC=$A/$(basename "$SRC"); else SRC=$(ls -t $A/${ID}_v*.png | head -1); fi
OUT=/c/GameProject/2ndAction/Unity/2ndAction/Assets/Art/Icons/CardIcons/$ID.png
$PY -I /c/GameProject/2ndAction/Tools/art/iconprep.py "$(cygpath -w $SRC)" "$(cygpath -w $OUT)" --cy $CY --preview "$(cygpath -w $A/prev_$ID.png)"
echo "src=$SRC out=$OUT prev=$A/prev_$ID.png"
