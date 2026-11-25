#!/bin/bash
# 裏へ → 復帰 → 戻る(ボタン 2 回・ジェスチャー 1 回)で、アプリが終わらずゲームに「戻る」が届くか(UUM-159613 の確認)。
# Run In Background を有効にするため -qaLifecycle(試験の起動は Run In Background が有効)でラン中に行う。
# 使い方: bash back_after_resume.sh <APK> [ラベル]
ADB="${ADB:-/c/Program Files/Unity/Hub/Editor/6000.5.6f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe}"
P=com.zero0084.action2nd; A=$P/com.unity3d.player.UnityPlayerGameActivity; APK=$1; L=${2:-test}
"$ADB" wait-for-device; until [ "$("$ADB" shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" = "1" ]; do sleep 3; done
"$ADB" shell svc power stayon true
[ -n "$APK" ] && { "$ADB" install -r "$APK" | tail -1; }
echo "manifest: $("$ADB" shell dumpsys package $P | grep -o 'versionName=[^ ]*' | head -1)"
"$ADB" shell am force-stop $P; "$ADB" shell "am start -n $A -e unity '-force-gles30 -qaLifecycle /sdcard/Android/data/$P/files/lc -lcRun 1 -lcBg 1'" >/dev/null; sleep 40
ok=0; n=0
resume_back() { # $1 = button|gesture
  "$ADB" shell input keyevent KEYCODE_HOME; sleep 5; "$ADB" shell monkey -p $P -c android.intent.category.LAUNCHER 1 >/dev/null 2>&1; sleep 6
  "$ADB" logcat -c
  if [ "$1" = gesture ]; then "$ADB" shell input swipe 2 900 500 900 250; else "$ADB" shell input keyevent KEYCODE_BACK; fi
  sleep 4; n=$((n+1))
  local pid=$("$ADB" shell pidof $P | tr -d '\r'); local kn=$("$ADB" logcat -d | grep -a -c "KeyNav\] esc=True")
  if [ -n "$pid" ] && [ "$kn" -ge 1 ]; then ok=$((ok+1)); r=OK; else r=NG; fi
  echo "[$L] $1 after resume: $r (app ${pid:+alive}${pid:-ENDED}, back reached the game: $kn)"
  "$ADB" logcat -d | grep -a "KeyNav\] screen" | tail -1 | sed 's/^.*Unity   : /    /' | cut -c1-120
  [ -z "$pid" ] && { "$ADB" shell "am start -n $A -e unity '-force-gles30 -qaLifecycle /sdcard/Android/data/$P/files/lc -lcRun 1 -lcBg 1'" >/dev/null; sleep 40; }
}
resume_back button; "$ADB" shell input keyevent KEYCODE_BACK; sleep 2
resume_back button; "$ADB" shell input keyevent KEYCODE_BACK; sleep 2



echo "[$L] result: $ok/$n OK"
