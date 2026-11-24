#!/bin/bash
# 2026-11-24 ボスの報酬のカードを保持した時(本人だけ止まる)+ 変更前の比較 + ONLINE(実際の Relay)
cd "$(dirname "$0")/../.."
T=Tools/net/team.sh; O=Docs/Multiplayer_2026-11-24/runs/summary_reward_online.txt; : > $O
run() { echo "### $1: $2" >> $O; bash $T "$1" "$3" "$4" "$5" "$6" "$7" "$8" 2>&1 | grep -v "^SUMMARY" >> $O; echo >> $O; }
run A2 "CO-OP HOST先行・HOSTがとどめ・HOSTがボス報酬を10秒選ばない(LOCAL)" local coop 55 "-netAutoLead host -netAutoLeadGap 60" "-netAutoBossAt 20 -netAutoRwKills 30:boss -netAutoHoldChoiceSec 10" ""
run A2_legacy "同上を変更前の決まり(-netLegacyStops 1)" local coop 55 "-netAutoLead host -netAutoLeadGap 60 -netLegacyStops 1" "-netAutoBossAt 20 -netAutoRwKills 30:boss -netAutoHoldChoiceSec 10" ""
run B2 "CO-OP 参加者がとどめ・参加者がボス報酬を10秒選ばない(LOCAL)" local coop 55 "-netAutoLead join -netAutoLeadGap 60" "-netAutoBossAt 20" "-netAutoRwKills 30:boss -netAutoHoldChoiceSec 10"
run O1 "CO-OP ONLINE(Relay)HOSTがとどめ・HOSTがボス報酬を10秒選ばない" online coop 55 "-netAutoLead host -netAutoLeadGap 60" "-netAutoBossAt 20 -netAutoRwKills 30:boss -netAutoHoldChoiceSec 10" ""
run O2 "VERSUS ONLINE(Relay)参加者がとどめ・参加者がボス報酬を10秒選ばない" online versus 55 "-netAutoLead join -netAutoLeadGap 60" "-netAutoBossAt 20" "-netAutoRwKills 30:boss -netAutoHoldChoiceSec 10"
echo DONE >> $O
