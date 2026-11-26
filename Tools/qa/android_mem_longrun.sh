#!/bin/bash
# Android の長距離走行のメモリの推移(2026-11-26、Unity 6000.6.10f1 の確認用。エミュレーター、開発版 APK)。
# ゲームの動きは変えない(QaSweep -qaFullRun を走らせ、外から dumpsys meminfo の PSS を 60 秒ごとに記録するだけ)。
# 使い方: bash android_mem_longrun.sh <出力フォルダ> [APK] [止める距離 m(既定 20000)]
ADB="${ADB:-/c/Program Files/Unity/Hub/Editor/6000.5.6f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe}"
P=com.zero0084.action2nd; A=$P/com.unity3d.player.UnityPlayerGameActivity
OUT=${1:-amem_out}; APK=$2; STOP=${3:-20000}; mkdir -p "$OUT"
"$ADB" wait-for-device; until [ "$("$ADB" shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" = "1" ]; do sleep 3; done
[ -n "$APK" ] && "$ADB" install -r "$APK" | tail -1
"$ADB" shell svc power stayon true
"$ADB" shell am force-stop $P; "$ADB" logcat -c
D=/sdcard/Android/data/$P/files/amem
"$ADB" shell "am start -n $A -e unity '-force-gles30 -memSave -qaFullRun $D -qaStage wasteland_road -qaChar swordsman -qaKmh 0 -qaStopAt $STOP -qaNoShots 1'" >/dev/null
CSV="$OUT/mem.csv"; echo "sec,pss_kb,java_kb,native_kb,graphics_kb,distance" > "$CSV"
t0=$(date +%s)
while :; do
  sleep 60; s=$(( $(date +%s) - t0 ))
  "$ADB" shell pidof $P >/dev/null || { echo "app ended at ${s}s"; break; }
  mi=$("$ADB" shell dumpsys meminfo $P 2>/dev/null | tr -d '\r')
  pss=$(echo "$mi" | awk '/TOTAL PSS:/{print $3; exit} /^ *TOTAL /{print $2; exit}')
  jv=$(echo "$mi" | awk '/Java Heap:/{print $3; exit}'); nt=$(echo "$mi" | awk '/Native Heap:/{print $3; exit}'); gr=$(echo "$mi" | awk '/Graphics:/{print $2; exit}')
  dist=$("$ADB" logcat -d -s Unity 2>/dev/null | grep -a -o "[0-9]*m  game" | tail -1 | cut -dm -f1)
  echo "$s,$pss,$jv,$nt,$gr,$dist" | tee -a "$CSV"
  "$ADB" logcat -d -s Unity 2>/dev/null | grep -aq -E "stop at [0-9]+m|CHECK\(S\) FAILED|ALL CHECKS PASSED" && { echo "run finished at ${s}s"; break; }
  [ $s -gt 3000 ] && { echo "time limit"; break; }
done
"$ADB" logcat -d -s Unity > "$OUT/unity.log" 2>/dev/null
"$ADB" logcat -d 2>/dev/null | grep -a -E "FATAL|ANR in $P|lowmemorykiller|Low on memory" | head -5
"$ADB" shell "cat $D/qa_*.txt" 2>/dev/null > "$OUT/qa.txt"; grep -a -E "^end:|CHECK" "$OUT/qa.txt" | head -5
echo "exceptions: $(grep -a -c Exception "$OUT/unity.log")"
"$ADB" shell am force-stop $P
