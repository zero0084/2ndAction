#!/bin/bash
# 属性の視認性(2026-10-30)の比較動画: QaSweep -qaElemVis -evPart vis -evVideo 1 の連番(frames/f%05d.png)と video.tsv から作る。
#   mkelemvid.sh <新のフォルダ> <旧のフォルダ> <出力フォルダ>
#   ・elem_<属性>_3cases.mp4 … 「属性なし / 入口 Lv9 / 系統を全部 Lv9」を横に3つ(新の表示)
#   ・elem_<属性>_old_new.mp4 … 系統 Lv9 の 旧 / 新 を横に2つ
NEW=$1; OLD=$2; OUT=$3; mkdir -p "$OUT"
# 撮影は描画の終わりに非同期で書かれるので、番号が抜けることがある(連番が切れると ffmpeg がそこで止まる)→ 前のコマで埋める
PY="C:/Users/0084k/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe"
for d in "$NEW" "$OLD"; do "$PY" -c "
import os,shutil,sys
d=sys.argv[1]+'/frames'; fs=sorted(int(f[1:6]) for f in os.listdir(d) if f.startswith('f') and f.endswith('.png'))
n=0
for i in range(fs[0],fs[-1]+1):
    p=d+'/f%05d.png'%i
    if not os.path.exists(p): shutil.copy(d+'/f%05d.png'%(i-1),p); n+=1
print(d,'filled',n)" "$d"; done
rng() { awk -F'\t' -v e="$2" -v c="$3" '$1==e && $2==c {print $3" "$4}' "$1/video.tsv"; }
for e in fire ice thunder wind blood; do
  args=(); filt=""; k=0
  for c in none entry family; do
    read -r a b < <(rng "$NEW" $e $c); [ -z "$a" ] && continue
    args+=(-framerate 30 -start_number $a -i "$NEW/frames/f%05d.png"); n=$((b-a+1))
    filt="$filt[$k:v]trim=end_frame=$n,setpts=PTS-STARTPTS,crop=iw*0.62:ih*0.55:iw*0.16:ih*0.3,scale=640:-2,setsar=1[v$k];"; k=$((k+1))
  done
  ffmpeg -y -loglevel error "${args[@]}" -filter_complex "${filt}[v0][v1][v2]hstack=inputs=3[v]" -map "[v]" -c:v libx264 -pix_fmt yuv420p -crf 22 -shortest "$OUT/elem_${e}_3cases.mp4" && echo "$OUT/elem_${e}_3cases.mp4"
  read -r a1 b1 < <(rng "$OLD" $e family); read -r a2 b2 < <(rng "$NEW" $e family)
  if [ -n "$a1" ] && [ -n "$a2" ]; then
    n=$(( (b1-a1) < (b2-a2) ? (b1-a1+1) : (b2-a2+1) ))
    ffmpeg -y -loglevel error -framerate 30 -start_number $a1 -i "$OLD/frames/f%05d.png" -framerate 30 -start_number $a2 -i "$NEW/frames/f%05d.png" \
      -filter_complex "[0:v]crop=iw*0.62:ih*0.55:iw*0.16:ih*0.3,scale=960:-2,setsar=1[a];[1:v]crop=iw*0.62:ih*0.55:iw*0.16:ih*0.3,scale=960:-2,setsar=1[b];[a][b]hstack=inputs=2[v]" \
      -map "[v]" -frames:v $n -c:v libx264 -pix_fmt yuv420p -crf 22 "$OUT/elem_${e}_old_new.mp4" && echo "$OUT/elem_${e}_old_new.mp4"
  fi
done
