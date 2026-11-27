#!/bin/bash
# マルチの試験の後: 単独プレイ(荒野街道・剣士・自然な加速・無敵)を 40 分走らせ、メモリの増え方を比べる(memlog.txt に記録される)
cd /c/GameProject/2ndAction
until grep -q "WS done" Docs/Multi4_2026-11-28/night_ws.out 2>/dev/null; do sleep 30; done
D=Docs/Multi4_2026-11-28/night/solo_mem; mkdir -p $D
echo "solo start $(date +%H:%M:%S)"
timeout 2400 Unity/2ndAction/Builds/WindowsDevSandbox/OneMoreMile.exe -screen-fullscreen 0 -screen-width 640 -screen-height 360 -titleScreen 0 -fileSave "$(cygpath -w $PWD/$D/save.json)" -qaFullRun "$(cygpath -w $PWD/$D)" -qaStage wasteland_road -qaChar swordsman -qaKmh 0 -logFile "$(cygpath -w $PWD/$D/run.log)" > /dev/null 2>&1
echo "solo done $(date +%H:%M:%S)"
