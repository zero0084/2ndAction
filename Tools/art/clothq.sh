#!/bin/bash
# 1コマずつの描き直しを取り込む(clothsingle.sh の呼び方を簡単に): bash Tools/art/clothq.sh <id> <in_erasedの名前(Folder__name)> <ダウンロード名> [suffix=b]
#  Folder__name → 元のフォルダ(Poses の下も探す)/name。結果は chars/<id>/q.log へ追記
cd "$(dirname "$0")/../.." || exit 1
id=$1; key=$2; dl=$3; suf=${4:-b}; O=Docs/ClothRevision_2026-10-25/ingame/original/Art
fo=${key%%__*}; n=${key##*__}
d=$(cd $O && ls -d $fo 2>/dev/null || find . -type d -name "$fo" | sed 's#^\./##' | head -1)
bash Tools/art/clothsingle.sh $id $d/$n $dl $suf 2>&1 | grep -o "^$n.png {'scale': [0-9.]*, 'dx': -*[0-9]*, 'dy': -*[0-9]*, 'score': [0-9.]*" | head -1 | sed "s#^#$d/#" >> Docs/ClothRevision_2026-10-25/chars/$id/q.log
