#!/bin/bash
# 2026-11-28 依頼⑦(止まって戦う形 + 命中の修正 + 強化)と宣伝動画の撮影を、重い処理を1つずつ順に
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
D=$ROOT/Docs/Sisters_2026-11-28
U="/c/Program Files/Unity/Hub/Editor/6000.6.10f1/Editor/Unity.exe"
P=$ROOT/Unity/2ndAction
W() { cygpath -w "$1"; }
FFM=$(command -v ffmpeg)
step() { echo "######## $1 $(date +%H:%M:%S)"; }
step "build dev"
"$U" -batchmode -quit -projectPath "$(W $P)" -executeMethod BuildWindowsProductSandbox.BuildDev -logFile "$(W $D/build_final_dev.log)"
grep -a "result=\|error CS" "$D/build_final_dev.log" | head -5
grep -aq "result=Succeeded" "$D/build_final_dev.log" || { echo "DEV BUILD FAILED"; exit 1; }
step "build capture"
"$U" -batchmode -quit -projectPath "$(W $P)" -executeMethod BuildStoreCapture.Build -logFile "$(W $D/build_final_capture.log)"
grep -a "result=\|error CS\|Build succeeded\|Build Finished" "$D/build_final_capture.log" | head -5

step "solo: hit check (halted form), 4 views"
SECS=20 bash "$ROOT/Tools/qa/sishit.sh" "$D/final_solo_hit" swordsman
step "solo: kill + credits + YES (landscape) / NO (portrait side)"
TMO=1800 VIEWS="land" EXTRA="-shKill 1 -ldPower 6 -shAfter yes" bash "$ROOT/Tools/qa/sishit.sh" "$D/final_solo_kill" gunslinger
TMO=1800 VIEWS="side" EXTRA="-shKill 1 -ldPower 6 -shAfter no" bash "$ROOT/Tools/qa/sishit.sh" "$D/final_solo_kill" swordsman

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

step "multi (all cards Lv9)"
cd "$ROOT"
EXTRA="-sisBuff 0" bash Tools/net/sis4.sh F1_local_coop_buff_before local coop 16
bash Tools/net/sis4.sh F2_local_coop_buff_after local coop 16
LDC=yes bash Tools/net/sis4.sh F3_local_versus_yes local versus 16
bash Tools/net/sis4.sh F4_online_coop online coop 16
LDC=yes bash Tools/net/sis4.sh F5_online_versus_yes online versus 16
step "all done"
