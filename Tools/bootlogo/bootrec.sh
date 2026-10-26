#!/bin/bash
# bootrec.sh <出力フォルダ> <land|port> [秒数]: エミュレーターで起動を録画し、時刻つきでログを取る(2026-10-26 起動ロゴの調査)
#  録画はエミュレーター本体の録画(adb emu screenrecord、GL の画面も写る。端末の screenrecord はエミュでは Unity の画面がほぼ写らない)
#  時刻: 録画の開始と起動の時刻(ホストの時計 = エミュの時計)、logcat -v epoch
#  保存は試験用のファイル(-fileSave bs/save.json)。環境変数: PK / NOFORCE=1(-force-gles30 を付けない) / FS(試験用の保存ファイル)
export MSYS_NO_PATHCONV=1
ADB=${ADB:-/c/Users/0084k/AppData/Local/Android/Sdk/platform-tools/adb.exe}
D=$1; o=$2; SEC=${3:-30}; PK=${PK:-com.zero0084.action2nd}
rm -rf "$D"; mkdir -p "$D"; DW=$(cygpath -m "$D")
$ADB shell am force-stop $PK
$ADB shell settings put system accelerometer_rotation 0
if [ "$o" = port ]; then $ADB shell settings put system user_rotation 0; else $ADB shell settings put system user_rotation 1; fi
printf -- "-fileSave ${FS:-bs/save.json} ${EXTRAQA}\n" > "$D/qa_args.txt"   # EXTRAQA: 追加の試験引数(例 -bootSplash 0)
$ADB push "$D/qa_args.txt" /sdcard/Android/data/$PK/files/qa_args.txt > /dev/null
sleep 1
$ADB logcat -c
$ADB emu screenrecord start --time-limit $SEC --fps 30 "$DW/screen.webm" > /dev/null 2>&1
echo "REC $(date +%s.%N)" > "$D/rec.txt"
sleep 1.0
EXTRA='-e unity -force-gles30'; [ -n "$NOFORCE" ] && EXTRA=''
echo "LAUNCH $(date +%s.%N)" > "$D/launch.txt"
$ADB shell am start -W -n $PK/com.unity3d.player.UnityPlayerGameActivity $EXTRA >> "$D/launch.txt" 2>&1
sleep $((SEC - 2))
$ADB emu screenrecord stop > /dev/null 2>&1; sleep 3
$ADB logcat -d -v epoch > "$D/logcat_all.txt"
grep -a -E "\[Boot\]|\[BootSplash\]|Loading\] done|ActivityTaskManager: (Displayed|START)|Start proc .*$PK|SplashScreenView: Build|normal save|Exception" "$D/logcat_all.txt" | grep -v -E "AssetPack|BillingClient|Chimera" > "$D/boot.txt"
cat "$D/rec.txt" "$D/launch.txt" | grep -E "REC|LAUNCH|TotalTime"
