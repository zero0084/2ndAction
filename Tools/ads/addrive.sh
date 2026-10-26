#!/bin/bash
# addrive.sh <出力フォルダ> [秒数]: 開発版の -qaAdSdk の試験の間、外から広告を操作する(2026-10-26)。
#  ゲームのログの「>>> <名前> <操作>」を見て、その広告の画面に対して操作する:
#   close   … 閉じるボタン(uiautomator で「Close」等を探して押す。報酬広告は報酬の後に出る)
#   early   … 報酬の前に戻るキーで閉じる(「動画を閉じますか」が出たら閉じる方を押す)
#   home    … ホームキーで外へ出て 5秒後にアプリへ戻り、その後に閉じる
#   rotate  … 表示中に画面の向きを変えてから閉じる
#  ログと画面の写真をフォルダへ残す。adb の場所は ADB で変えられる
export MSYS_NO_PATHCONV=1
ADB=${ADB:-/c/Users/0084k/AppData/Local/Android/Sdk/platform-tools/adb.exe}
D=$1; SEC=${2:-900}; PK=com.zero0084.action2nd; mkdir -p "$D"
t_end=$(( $(date +%s) + SEC ))
tapclose() { # 閉じるボタンを探して押す(見つかったら 0)
  $ADB shell uiautomator dump /sdcard/ui.xml > /dev/null 2>&1
  local xml; xml=$($ADB shell cat /sdcard/ui.xml 2>/dev/null)
  local b; b=$(echo "$xml" | tr '>' '\n' | grep -i -E 'content-desc="(Close|Close Ad|閉じる|Skip)[^"]*"|text="(Close|閉じる|×|X|Close video)"' | grep -o 'bounds="\[[0-9]*,[0-9]*\]\[[0-9]*,[0-9]*\]"' | head -1)
  [ -z "$b" ] && return 1
  local n; n=$(echo "$b" | grep -o '[0-9]*'); set -- $n
  $ADB shell input tap $(( ($1+$3)/2 )) $(( ($2+$4)/2 )); echo "$(date +%T) tap close at $(( ($1+$3)/2 )),$(( ($2+$4)/2 ))" >> "$D/drive.txt"; return 0
}
shot() { $ADB exec-out screencap -p > "$D/$1.png" 2>/dev/null; }
handled=""
while [ $(date +%s) -lt $t_end ]; do
  line=$($ADB logcat -d -s Unity 2>/dev/null | grep -a -o ">>> [A-Za-z0-9_]* [a-z]*" | tail -1)
  if [ -n "$line" ] && [ "$line" != "$handled" ]; then
    handled="$line"; name=$(echo $line | cut -d' ' -f2); act=$(echo $line | cut -d' ' -f3)
    echo "$(date +%T) marker $name $act" >> "$D/drive.txt"
    if [ "$act" = netoff ]; then $ADB shell cmd connectivity airplane-mode enable; $ADB shell svc wifi disable; $ADB shell svc data disable; echo "$(date +%T) network off" >> "$D/drive.txt"; continue; fi
    if [ "$act" = neton ]; then $ADB shell cmd connectivity airplane-mode disable; $ADB shell svc wifi enable; $ADB shell svc data enable; echo "$(date +%T) network on" >> "$D/drive.txt"; continue; fi
    sleep 3; shot "${name}_showing"
    case $act in
      early) sleep 2; $ADB shell input keyevent BACK; sleep 2; shot "${name}_backdialog"; tapclose || $ADB shell input keyevent BACK ;;
      home) sleep 2; $ADB shell input keyevent HOME; sleep 5; shot "${name}_away"; $ADB shell monkey -p $PK -c android.intent.category.LAUNCHER 1 > /dev/null 2>&1; sleep 4; shot "${name}_back" ;;
      rotate) sleep 2; $ADB shell settings put system user_rotation 0; sleep 3; shot "${name}_rotated"; $ADB shell settings put system user_rotation 1; sleep 3 ;;
    esac
    # 閉じるボタンが出るまで(報酬広告は報酬の後)探して押す。最大 70秒
    for i in $(seq 1 35); do tapclose && break; sleep 2; done
    sleep 2; shot "${name}_after"
  fi
  if $ADB logcat -d -s Unity 2>/dev/null | grep -a -q "\[QA\] adsdk done"; then echo "$(date +%T) done" >> "$D/drive.txt"; break; fi
  sleep 1
done
