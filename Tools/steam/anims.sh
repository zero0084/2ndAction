#!/bin/bash
# anims.sh <撮影のrawフォルダ> <出力フォルダ>: Steam の紹介画像の短いアニメーション(WebM 1232 幅 30fps / GIF 616 幅)を作る(2026-10-26 撮り直し)
# 形式は 2026-10-23 の版と同じ。場面の開始秒は 2026-10-26 の撮影(cap_n_ja)に合わせてある。撮り直したら開始秒を見直す
FF=${FFMPEG:-ffmpeg}; R=$1; O=$2; mkdir -p "$O"
mk() { # 名前 ショット 開始 webmの長さ gifの長さ gifのfps
  "$FF" -v error -y -ss $3 -t $4 -i "$R/$2.mkv" -vf "scale=1232:-2:flags=lanczos,fps=30" -c:v libvpx-vp9 -b:v 0 -crf 30 -row-mt 1 -an "$O/omm_anim_$1.webm"
  "$FF" -v error -y -ss $3 -t $5 -i "$R/$2.mkv" -vf "fps=$6,scale=616:-2:flags=lanczos,split[a][b];[a]palettegen=max_colors=192:stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle" -loop 0 "$O/omm_anim_$1.gif"
  echo "$1 done"
}
mk run_combat    run_day       6.0  3.5 3.54 15
mk aerial_juggle boss_fight    17.6 4.5 3.83 12
mk card_choice   cards         1.4  4.8 4.8  15
mk escape        result_escape 1.6  4.5 3.5  12
