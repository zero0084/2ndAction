#!/bin/bash
# Android の性能の比べ(更新前後の APK を同じエミュレーターで)。起動の時間・メモリ(合計 PSS)・FPS(5,000m まで走る間)。
# 使い方: bash perf_ab.sh <APK> <ラベル>
ADB="${ADB:-/c/Program Files/Unity/Hub/Editor/6000.5.6f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe}"
P=com.zero0084.action2nd; A=$P/com.unity3d.player.UnityPlayerGameActivity; APK=$1; L=$2
"$ADB" wait-for-device; until [ "$("$ADB" shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" = "1" ]; do sleep 3; done
"$ADB" shell svc power stayon true
"$ADB" install -r "$APK" | tail -1
"$ADB" shell am force-stop $P; "$ADB" shell "echo 3 > /proc/sys/vm/drop_caches" 2>/dev/null; sleep 3; "$ADB" logcat -c
t0=$(date +%s)
W=$("$ADB" shell "am start -W -n $A -e unity '-force-gles30 -qaFullRun /sdcard/Android/data/$P/files/perf -qaStage wasteland_road -qaChar swordsman -qaKmh 0 -qaStopAt 5600 -qaNoShots 1'" | tr -d '\r' | grep -E "TotalTime|WaitTime")
home=""; for i in $(seq 1 120); do sleep 1; "$ADB" logcat -d -s Unity | grep -aq "\[QA\] full run:" && { home=$(( $(date +%s) - t0 )); break; }; done
sleep 90; pss=$("$ADB" shell dumpsys meminfo $P | tr -d '\r' | grep -E "TOTAL PSS|^ *TOTAL:" | head -1)
for i in $(seq 1 200); do sleep 5; "$ADB" logcat -d -s Unity | grep -aq "stop at" && break; done
t1=$("$ADB" logcat -d -s Unity | grep -a "\[QA\] full run:" | head -1 | awk '{print $2}'); t2=$("$ADB" logcat -d -s Unity | grep -a "\[QA\] stop at" | head -1 | awk '{print $2}')
secs=$(python -c "import sys;f=lambda t:sum(float(x)*m for x,m in zip(t.split(':'),(3600,60,1)));print(round(f(sys.argv[2])-f(sys.argv[1])))" "$t1" "$t2" 2>/dev/null)
fps=$("$ADB" logcat -d -s Unity | grep -a "fps~" | sed 's/^.*\[QA\] *//' | tr -d '\r' | tail -2)
echo "[$L] launch: $W | home ready (run started): ${home}s"
echo "[$L] memory during the run: $pss"
echo "[$L] run to the stop: ${secs}s real | $fps"
"$ADB" shell am force-stop $P
