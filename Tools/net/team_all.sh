#!/bin/bash
# 2026-11-24 マルチの止まり方/チーム走行距離の全組み合わせ(LOCAL)。結果は Docs/Multiplayer_2026-11-24/runs/summary_local.txt
cd "$(dirname "$0")/../.."
T=Tools/net/team.sh; O=Docs/Multiplayer_2026-11-24/runs/summary_local.txt; : > $O
run() { echo "### $1: $2" >> $O; bash $T "$1" local "$3" "$4" "$5" "$6" "$7" 2>&1 | grep -v "^SUMMARY" >> $O; echo >> $O; }
run B "CO-OP 参加者が先行・参加者がとどめ・参加者が8秒カード選択" coop 50 "-netAutoLead join -netAutoLeadGap 80" "-netAutoBossAt 20" "-netAutoRwKills 30:boss -netAutoHoldChoiceSec 8"
run C "CO-OP HOST先行・後続(参加者)が闘技場に着いて合流・参加者がとどめ" coop 60 "-netAutoLead host -netAutoLeadGap 60" "-netAutoBossAt 20" "-netAutoRwKills 42:boss -netAutoHoldChoiceSec 6"
run D "CO-OP 後続が着く前に撃破(HOST が3秒で倒す・差150m)" coop 50 "-netAutoLead host -netAutoLeadGap 150" "-netAutoBossAt 20 -netAutoRwKills 23:boss -netAutoHoldChoiceSec 6" ""
run E "CO-OP HOST が一時停止のメニュー6秒" coop 40 "" "-netAutoPauseAt 15 -netAutoPauseFor 6" ""
run F "CO-OP 参加者が一時停止のメニュー6秒" coop 40 "" "" "-netAutoPauseAt 15 -netAutoPauseFor 6"
run G "CO-OP 連続するボス2体(HOST がとどめ)+参加者の DOWN→復活" coop 55 "" "-netAutoRwKills 18:spawnboss,22:boss,32:spawnboss,36:boss -netAutoHoldChoiceSec 4" "-netAutoKillAt 26 -netAutoRevive -netAutoReviveAfter 3"
run H "CO-OP ボス戦の途中で参加者が退出" coop 50 "-netAutoLead host -netAutoLeadGap 60" "-netAutoBossAt 20 -netAutoRwKills 34:boss -netAutoHoldChoiceSec 6" "-netAutoLeaveAt 28"
run V1 "VERSUS HOST 先行・HOST がとどめ・HOST が8秒カード選択" versus 50 "-netAutoLead host -netAutoLeadGap 80" "-netAutoBossAt 20 -netAutoRwKills 30:boss -netAutoHoldChoiceSec 8" ""
run V2 "VERSUS 参加者が先行・参加者がとどめ・参加者が8秒カード選択" versus 50 "-netAutoLead join -netAutoLeadGap 80" "-netAutoBossAt 20" "-netAutoRwKills 30:boss -netAutoHoldChoiceSec 8"
run V3 "VERSUS 参加者が一時停止のメニュー6秒" versus 40 "" "" "-netAutoPauseAt 15 -netAutoPauseFor 6"
echo DONE >> $O
