#!/bin/bash
# 2026-11-28 依頼⑦(止まって戦う形 + 命中の修正 + 強化)と宣伝動画の撮影を、重い処理を1つずつ順に
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
D=$ROOT/Docs/Sisters_2026-11-28
U="/c/Program Files/Unity/Hub/Editor/6000.6.10f1/Editor/Unity.exe"
P=$ROOT/Unity/2ndAction
W() { cygpath -w "$1"; }
FFM=$(command -v ffmpeg)
step() { echo "######## $1 $(date +%H:%M:%S)"; }
step "promo capture"
C=$ROOT/Docs/PromoFlick_2026-11-28/capture; mkdir -p "$C"
CEXE=$P/Builds/StoreCapture/OneMoreMile.exe
cap() { # name view w h shots
  local dir=$C/$1; mkdir -p "$dir"
  timeout 900 "$CEXE" -screen-fullscreen 0 -screen-width $3 -screen-height $4 -fileSave "$(W $dir/save.json)" -storeCapture "$(W $dir)" -scShots $5 \
    -scLang ja -scNoBonus 1 -scVideoSuper ${6:-1} -caps android -pview $2 -scFfmpeg "$(W $FFM)" -logFile "$(W $dir/log.txt)" > /dev/null 2>&1
  echo "  $1: $(ls $dir/raw 2>/dev/null | tr '\n' ' ')"; grep -a "\[pf\]\|FAIL" "$dir/log.txt" | sed 's/.*\[QaSweep\] //' | cut -c1-120 | head -30
}
cap side side 608 1080 pf_launch,pf_combo 2
cap fold fold 608 1080 pf_launch 2
cap oblique oblique 608 1080 pf_launch 2
cap land side 1920 1080 pf_land 1

step "promo edit"
V=$ROOT/Docs/PromoFlick_2026-11-28
PYX="C:/Users/0084k/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe"
"$PYX" -I -X utf8 "$ROOT/Tools/promo_flick/make_edl.py" "$(W $C)" "$(W $V/edl_ja.json)"
"$PYX" -I -X utf8 "$ROOT/Tools/promo_flick/edit.py" "$(W $V/edl_ja.json)" "$(W $V/omm_howto_ja_1080x1920_60.mp4)" "$(W $V/_work)"
step "video done"
