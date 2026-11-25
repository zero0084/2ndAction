#!/bin/bash
# 2026-11-25 マルチ 第2(HOST の選択中も雑魚が出る/撃破の演出で全員を止めない/チーム距離のそろえ/ヒットストップは本人だけ)
#  結果は Docs/Multiplayer_2026-11-25/runs/summary.txt。_legacy は変更前の決まり(-netLegacyStops 1: 11-24 と 11-25 のマルチの変更を両方戻す)
cd "$(dirname "$0")/../.."
T=Tools/net/team.sh; OUT=Docs/Multiplayer_2026-11-25/runs; mkdir -p $OUT; O=$OUT/summary.txt; : > $O
run() { echo "### $1: $2" >> $O; bash $T "$1" "$3" "$4" "$5" "$6" "$7" "$8" $OUT 2>&1 | grep -v "^SUMMARY" >> $O; echo >> $O; }
ONLY=${ONLY:-}
want() { [ -z "$ONLY" ] || [[ " $ONLY " == *" $1 "* ]]; }
want R1 && run R1 "CO-OP HOSTがとどめ・HOSTがボス報酬を10秒選ばない(参加者が追い越して先へ・雑魚が出続けるか)" local coop 60 "" "-netAutoBossAt 20 -netAutoRwKills 30:boss -netAutoHoldChoiceSec 10" ""
want R1_legacy && run R1_legacy "同上を変更前の決まり" local coop 60 "-netLegacyStops 1" "-netAutoBossAt 20 -netAutoRwKills 30:boss -netAutoHoldChoiceSec 10" ""
want R2 && run R2 "CO-OP HOST先行60m・HOSTがとどめ・HOSTが10秒選ぶ(後ろの人が止まらない)" local coop 60 "-netAutoLead host -netAutoLeadGap 60" "-netAutoBossAt 20 -netAutoRwKills 30:boss -netAutoHoldChoiceSec 10" ""
want R3 && run R3 "CO-OP 参加者がとどめ・参加者が10秒選ぶ" local coop 60 "-netAutoLead join -netAutoLeadGap 60" "-netAutoBossAt 20" "-netAutoRwKills 30:boss -netAutoHoldChoiceSec 10"
want E2 && run E2 "CO-OP HOSTが一時停止のメニュー8秒(雑魚が出続ける)" local coop 45 "" "-netAutoPauseAt 15 -netAutoPauseFor 8" ""
want G2 && run G2 "CO-OP 連続するボス2体+参加者の DOWN→復活" local coop 55 "" "-netAutoRwKills 18:spawnboss,22:boss,32:spawnboss,36:boss -netAutoHoldChoiceSec 4" "-netAutoKillAt 26 -netAutoRevive -netAutoReviveAfter 3"
want V1 && run V1 "VERSUS HOST先行・HOSTがとどめ・HOSTが8秒選ぶ" local versus 55 "-netAutoLead host -netAutoLeadGap 80" "-netAutoBossAt 20 -netAutoRwKills 30:boss -netAutoHoldChoiceSec 8" ""
want V2 && run V2 "VERSUS 参加者がとどめ・参加者が8秒選ぶ" local versus 55 "-netAutoLead join -netAutoLeadGap 80" "-netAutoBossAt 20" "-netAutoRwKills 30:boss -netAutoHoldChoiceSec 8"
want V3 && run V3 "VERSUS 参加者が一時停止のメニュー6秒" local versus 45 "" "" "-netAutoPauseAt 15 -netAutoPauseFor 6"
want RS && run RS "CO-OP ボスの後に2人とも倒れてランの終わり→RESULT(終わりのチーム距離がそろうか・RESULT の撮影)" local coop 70 "-netAutoResultShot 2 -netAutoRunOverHold 8" "-netAutoBossAt 20 -netAutoRwKills 30:boss -netAutoKillAt 45 -netAutoStartHp 500" "-netAutoKillAt 45 -netAutoStartHp 500"
want O1 && run O1 "CO-OP ONLINE(Relay)HOSTがとどめ・HOSTが10秒選ぶ" online coop 60 "-netAutoLead host -netAutoLeadGap 60" "-netAutoBossAt 20 -netAutoRwKills 30:boss -netAutoHoldChoiceSec 10" ""
want O2 && run O2 "VERSUS ONLINE(Relay)参加者がとどめ・参加者が10秒選ぶ" online versus 60 "-netAutoLead join -netAutoLeadGap 60" "-netAutoBossAt 20" "-netAutoRwKills 30:boss -netAutoHoldChoiceSec 10"
echo DONE >> $O
