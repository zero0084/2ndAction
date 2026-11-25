#!/bin/bash
# Android の画面の出入りの試験(エミュレーター、開発版 APK)。Unity 6000.6.10f1 の UUM-159613(Run In Background + Predictive Back で
# 裏から戻った後の戻る操作)を含む。ジェスチャーの戻るはエミュレーターの input swipe では起こせないため実機で確認する。使い方: bash lifecycle_emulator.sh <出力フォルダ> [APK]
ADB="${ADB:-/c/Program Files/Unity/Hub/Editor/6000.5.6f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe}"
P=com.zero0084.action2nd; A=$P/com.unity3d.player.UnityPlayerGameActivity
OUT=${1:-lc_out}; APK=$2; mkdir -p "$OUT"
"$ADB" wait-for-device; until [ "$("$ADB" shell getprop sys.boot_completed 2>/dev/null | tr -d '
')" = "1" ]; do sleep 3; done
[ -n "$APK" ] && "$ADB" install -r "$APK" | tail -1
"$ADB" shell svc power stayon true; "$ADB" shell settings put system accelerometer_rotation 0
launch() { "$ADB" shell am force-stop $P; "$ADB" logcat -c; "$ADB" shell "am start -n $A -e unity '-force-gles30 -qaLifecycle /sdcard/Android/data/$P/files/lc $1'" >/dev/null
  for i in $(seq 1 60); do sleep 2; "$ADB" logcat -d -s Unity 2>/dev/null | grep -aq "\[lc\] t=" && break; done; sleep ${2:-5}; }
front() { "$ADB" shell monkey -p $P -c android.intent.category.LAUNCHER 1 >/dev/null 2>&1; }  # ランチャーから戻る(利用者と同じ)
last() { "$ADB" logcat -d -s Unity 2>/dev/null | grep -a "\[lc\]" | tail -1 | sed 's/^.*\[lc\]/[lc]/' | tr -d '\r'; }
keynav() { "$ADB" logcat -d -s Unity 2>/dev/null | grep -a "\[KeyNav\]" | tail -1 | sed 's/^.*\[KeyNav\]/[KeyNav]/' | cut -c1-120 | tr -d '\r'; }
alive() { "$ADB" shell pidof $P >/dev/null && echo alive || echo DEAD; }
shot() { "$ADB" exec-out screencap -p > "$OUT/$1.png"; }
step() { echo "--- $1"; sleep ${2:-4}; echo "    $(alive) $(last)"; shot "$3"; }
errs() { "$ADB" logcat -d 2>/dev/null | grep -a -E "FATAL|ANR in $P|Exception" | grep -a -v "QA\]" | head -3; }

if [[ " ${CASES:-L1 L2 L3} " == *" L1 "* ]]; then
echo "== L1 ラン中: 裏へ → 復帰 → 戻る(ボタン)→ 戻る(ジェスチャー)→ 回転 → 消灯 → 点灯"
launch "-lcRun 1" 30; step "running" 1 l1_00_run
"$ADB" shell input keyevent KEYCODE_HOME; step "HOME (background)" 5 l1_01_bg
front; step "back to the game" 5 l1_02_front
"$ADB" logcat -c; "$ADB" shell input keyevent KEYCODE_BACK; step "BACK button after resume" 3 l1_03_back; echo "    $(keynav)"
"$ADB" logcat -c; "$ADB" shell input keyevent KEYCODE_BACK; step "BACK button again" 3 l1_04_back2; echo "    $(keynav)"
"$ADB" shell settings put system user_rotation 1; step "rotate to landscape" 5 l1_07_rot1
"$ADB" shell settings put system user_rotation 0; step "rotate to portrait" 5 l1_08_rot0
"$ADB" shell svc power stayon false; "$ADB" shell input keyevent KEYCODE_SLEEP; step "screen off" 6 l1_09_off
"$ADB" shell input keyevent KEYCODE_WAKEUP; "$ADB" shell wm dismiss-keyguard; "$ADB" shell svc power stayon true; step "screen on" 6 l1_10_on
errs

fi
if [[ " ${CASES:-L1 L2 L3} " == *" L2 "* ]]; then
echo "== L2 ラン中に裏へ → OS が終了 → 起動 → CONTINUE"
launch "-lcRun 1" 40; step "running" 1 l2_00_run
"$ADB" shell input keyevent KEYCODE_HOME; sleep 4; "$ADB" shell am force-stop $P; echo "--- killed while in the background"
launch "-lcContinue 1" 15; "$ADB" logcat -d -s Unity | grep -a "\[lc\] boot\|\[lc\] continued" | sed 's/^.*\[lc\]/    [lc]/' | tr -d '\r'; step "after CONTINUE" 1 l2_01_cont
errs

fi
if [[ " ${CASES:-L1 L2 L3} " == *" L3 "* ]]; then
echo "== L3 LOCAL の部屋(Run In Background 有効): 裏へ → 復帰 → 戻る"
launch "-lcRun 0 -lcMulti 1 -lcLeaveAt 75" 25; "$ADB" logcat -d -s Unity | grep -a "\[lc\] local room" | sed 's/^.*\[lc\]/    [lc]/' | tr -d '\r'; step "room open" 1 l3_00_room
"$ADB" shell input keyevent KEYCODE_HOME; step "HOME (background)" 8 l3_01_bg
front; step "back to the game" 5 l3_02_front
"$ADB" logcat -c; "$ADB" shell input keyevent KEYCODE_BACK; step "BACK after resume (room)" 3 l3_03_back; echo "    $(keynav)"
"$ADB" shell input keyevent KEYCODE_BACK; sleep 2
for i in $(seq 1 30); do "$ADB" logcat -d -s Unity | grep -aq "\[lc\] left the room" && break; sleep 2; done
"$ADB" logcat -d -s Unity | grep -a "\[lc\] left the room\|run in background restored" | sed 's/^.*Unity *: /    /' | tr -d '\r'; step "after leaving the room (bg must be False)" 3 l3_05_left
errs
fi
"$ADB" shell am force-stop $P
echo "== done"
