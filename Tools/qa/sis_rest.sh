#!/bin/bash
# 2026-11-28 依頼⑦(止まって戦う形 + 命中の修正 + 強化)と宣伝動画の撮影を、重い処理を1つずつ順に
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
D=$ROOT/Docs/Sisters_2026-11-28
U="/c/Program Files/Unity/Hub/Editor/6000.6.10f1/Editor/Unity.exe"
P=$ROOT/Unity/2ndAction
W() { cygpath -w "$1"; }
FFM=$(command -v ffmpeg)
step() { echo "######## $1 $(date +%H:%M:%S)"; }
step "solo: hit check (halted form), 4 views"
SECS=20 bash "$ROOT/Tools/qa/sishit.sh" "$D/final_solo_hit" swordsman
step "solo: kill + credits + YES (landscape) / NO (portrait side)"
TMO=1800 VIEWS="land" EXTRA="-shKill 1 -ldPower 6 -shAfter yes" bash "$ROOT/Tools/qa/sishit.sh" "$D/final_solo_kill" gunslinger
TMO=1800 VIEWS="side" EXTRA="-shKill 1 -ldPower 6 -shAfter no" bash "$ROOT/Tools/qa/sishit.sh" "$D/final_solo_kill" swordsman

step "multi (all cards Lv9)"
cd "$ROOT"
EXTRA="-sisBuff 0" bash Tools/net/sis4.sh F1_local_coop_buff_before local coop 16
bash Tools/net/sis4.sh F2_local_coop_buff_after local coop 16
LDC=yes bash Tools/net/sis4.sh F3_local_versus_yes local versus 16
bash Tools/net/sis4.sh F4_online_coop online coop 16
LDC=yes bash Tools/net/sis4.sh F5_online_versus_yes online versus 16
step "all done"
