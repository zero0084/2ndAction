#!/bin/bash
# Unity の版をまたいだ通信の互換(2026-11-26、Unity 6000.6.10f1 への更新の確認用)。
#  エミュレーターの Android(引数の APK、例: 更新前 6000.5.6f1 の開発版)と Windows 開発版(WindowsDevSandbox、更新後)を、
#  UGS development の ONLINE(Sessions + Relay、DTLS)で同じ部屋へ入れて、協力で走らせる。保存は両方 -memSave。
#  使い方: bash Tools/net/xver_online.sh <出力フォルダ> android-host|windows-host [APK(入れ直す時だけ)] [走る秒数]
OUT=${1:?out}; ROLE=${2:?role}; APK=$3; RUNSEC=${4:-60}
ADB="${ADB:-/c/Program Files/Unity/Hub/Editor/6000.5.6f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe}"
P=com.zero0084.action2nd; A=$P/com.unity3d.player.UnityPlayerGameActivity
EXE=${EXE:-/c/GameProject/2ndAction/Unity/2ndAction/Builds/WindowsDevSandbox/OneMoreMile.exe}
mkdir -p "$OUT"; ROOM=QAXV$RANDOM
"$ADB" wait-for-device; until [ "$("$ADB" shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" = "1" ]; do sleep 3; done
[ -n "$APK" ] && "$ADB" install -r "$APK" | tail -1
"$ADB" shell svc power stayon true
BASE="-netAutoMode coop -netAutoStage wasteland_road -netAutoRunSeconds $RUNSEC -netAutoTeamLog 1 -netAutoBossHp 400000"
if [ "$ROLE" = android-host ]; then
  ANET="-netAutoHost -netAutoOnline -netAutoRoom $ROOM -netAutoPlayers 2 -netAutoStartDelay 15"; WNET="-netAutoJoin online -netAutoOnline -netAutoRoom $ROOM"
else
  WNET="-netAutoHost -netAutoOnline -netAutoRoom $ROOM -netAutoPlayers 2 -netAutoStartDelay 15"; ANET="-netAutoJoin online -netAutoOnline -netAutoRoom $ROOM"
fi
"$ADB" shell am force-stop $P; "$ADB" logcat -c
start_android() { "$ADB" shell "am start -n $A -e unity '-force-gles30 -memSave -ugsProfile qaxv_android $ANET $BASE'" >/dev/null; }
start_windows() { "$EXE" -screen-fullscreen 0 -screen-width 960 -screen-height 540 -memSave -ugsProfile qaxv_windows $WNET $BASE -logFile "$(cygpath -w "$OUT")\\windows.log" >/dev/null 2>&1 & WP=$!; }
if [ "$ROLE" = android-host ]; then start_android; sleep 45; start_windows; else start_windows; sleep 25; start_android; fi
LIMIT=$((RUNSEC + 300)); t=0
while [ $t -lt $LIMIT ]; do
  "$ADB" logcat -d -s Unity 2>/dev/null | grep -a -E "NETTEST|\[NET\]|\[ONLINE\]|\[LAN\]|Exception" | sed 's/^.*Unity *: //' | tr -d '\r' > "$OUT/android.log"
  w=0; grep -aq "SUMMARY reason" "$OUT/windows.log" 2>/dev/null && w=1
  a=0; grep -aq "SUMMARY reason" "$OUT/android.log" && a=1
  kill -0 $WP 2>/dev/null || w=1
  [ $w -eq 1 ] && [ $a -eq 1 ] && break
  sleep 10; t=$((t+10))
done
sleep 5; kill $WP 2>/dev/null   # この試験で起動した Windows の1つだけ(番号で)
"$ADB" shell am force-stop $P
echo "== $ROLE room $ROOM after ${t}s"
for f in android windows; do
  echo "-- $f"; grep -a -h -E "\[ONLINE\]|protocol|joined|connected|SUMMARY reason|DTLS|secure|rejected|mismatch|run in background" "$OUT/$f.log" | sed 's/.*\[NETTEST\] utc=[^ ]* //' | cut -c1-220 | head -20
  echo "   exceptions: $(grep -a -c Exception "$OUT/$f.log")"
done
