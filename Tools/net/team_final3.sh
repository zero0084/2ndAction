#!/bin/bash
# 2026-11-25 第3(表記・選択中の保護・3人)の最終ビルドでの確認。結果は Docs/Multiplayer_2026-11-25/runs3/summary.txt
cd "$(dirname "$0")/../.."
O=Docs/Multiplayer_2026-11-25/runs3/summary.txt; mkdir -p Docs/Multiplayer_2026-11-25/runs3; : > $O
SD=Docs/Multiplayer_2026-11-25/shots3; rm -rf $SD; mkdir -p $SD; W=$(cygpath -w "$(pwd)/$SD")
t3() { echo "### $1: $2" >> $O; bash Tools/net/team3.sh "$1" "$3" "$4" "$5" "$6" "$7" "$8" 2>&1 >> $O; echo >> $O; }
t2() { echo "### $1: $2" >> $O; bash Tools/net/team.sh "$1" "$3" "$4" "$5" "$6" "$7" "$8" Docs/Multiplayer_2026-11-25/runs3 2>&1 | grep -v "^SUMMARY" >> $O; echo >> $O; }
t3 T1 "CO-OP 3人: P1 ボス戦 / P2 カード選択(穴の上・選択中に被弾の試し)/ P3 走行" coop 75 "-netAutoLead host -netAutoLeadGap 150 -netAutoDamage -netAutoStartHp 500" "-netAutoBossAt 20 -netAutoRwKills 42:boss -netAutoHoldChoiceSec 5" "-netAutoChoiceAt 24 -netAutoChoiceHold 12 -netAutoChoiceOverPit 1" ""
t3 T2 "VERSUS 3人: 同上" versus 75 "-netAutoLead host -netAutoLeadGap 150 -netAutoDamage -netAutoStartHp 500" "-netAutoBossAt 20 -netAutoRwKills 42:boss -netAutoHoldChoiceSec 5" "-netAutoChoiceAt 24 -netAutoChoiceHold 12 -netAutoChoiceOverPit 1" ""
t3 T3 "CO-OP 3人: P2 がとどめ・P2 がボス報酬を10秒選ぶ間に P1 は走り P3 は後ろから" coop 75 "-netAutoLead host -netAutoLeadGap 80 -netAutoDamage -netAutoStartHp 500" "-netAutoBossAt 20" "-netAutoRwKills 32:boss -netAutoHoldChoiceSec 10" ""
t2 R1 "CO-OP 2人 HOST がとどめ・10秒選ぶ" local coop 60 "" "-netAutoBossAt 20 -netAutoRwKills 30:boss -netAutoHoldChoiceSec 10" ""
t2 UI1 "CO-OP 2人 ランの終わり→RESULT(チーム距離/個人距離の表記を撮影)" local coop 70 "-netAutoResultShot 7 -netAutoRunOverHold 14 -netAutoHudShots 15" "-netAutoBossAt 20 -netAutoRwKills 30:boss -netAutoKillAt 45 -netAutoStartHp 500 -netAutoShotDir $W\\coop_host" "-netAutoKillAt 45 -netAutoStartHp 500 -netAutoShotDir $W\\coop_join"
t2 UI2 "VERSUS 2人 RESULT(表記は今まで通り)" local versus 50 "-netAutoResultShot 7 -netAutoRunOverHold 14 -netAutoHudShots 15" "-netAutoKillAt 40 -netAutoStartHp 500 -netAutoShotDir $W\\vs_host" "-netAutoKillAt 40 -netAutoStartHp 500 -netAutoShotDir $W\\vs_join"
t2 O1 "CO-OP ONLINE(Relay)HOST がとどめ・10秒選ぶ" online coop 60 "-netAutoLead host -netAutoLeadGap 60" "-netAutoBossAt 20 -netAutoRwKills 30:boss -netAutoHoldChoiceSec 10" ""
echo DONE >> $O
