#!/bin/bash
# Android エミュレーターでのセーブの消失/バックアップ復旧の試験(テスト用データだけ)。起動引数は qa_args.txt(読んだら消える)で渡す
SP="C:/Users/0084k/AppData/Local/Temp/claude/C--GameProject-1stRPG/972e48a6-cec8-40d5-8275-62f59fbcb3de/scratchpad"
ADB=/c/Users/0084k/AppData/Local/Android/Sdk/platform-tools/adb.exe
export MSYS_NO_PATHCONV=1
PKG=com.zero0084.action2nd; ACT=$PKG/com.unity3d.player.UnityPlayerGameActivity
FILES=/storage/emulated/0/Android/data/$PKG/files
OUT="$SP/emu_sr"; rm -rf "$OUT"; mkdir -p "$OUT"
$ADB shell rm -rf $FILES/sr
step() { local n=$1 crash=$2; shift 2
  local attempt=1
  while :; do
    $ADB shell am force-stop $PKG; sleep 1
    printf '%s' "-qaSaveRecover $FILES/sr/$n -srShared $FILES/sr $*" > "$OUT/qa_args.txt"
    $ADB push "$OUT/qa_args.txt" $FILES/qa_args.txt > /dev/null
    $ADB logcat -c
    $ADB shell am start -n $ACT > /dev/null
    local t=0
    sleep 15
    if [ "$crash" = "1" ]; then
      while [ -n "$($ADB shell pidof $PKG | tr -d '')" ] && [ $t -lt 300 ]; do sleep 2; t=$((t+2)); done
      break
    fi
    while ! $ADB shell ls $FILES/sr/$n/qa_saverecover.txt > /dev/null 2>&1 && [ -n "$($ADB shell pidof $PKG | tr -d '')" ] && [ $t -lt 900 ]; do sleep 3; t=$((t+3)); done
    sleep 3
    if $ADB shell ls $FILES/sr/$n/qa_saverecover.txt > /dev/null 2>&1 || [ $attempt -ge 3 ]; then break; fi
    $ADB logcat -d -s Unity:V > "$OUT/$n.attempt$attempt.logcat.txt"
    echo "   (attempt $attempt: the app stopped without a result - $(grep -a -c 'berberis: Cannot process signal' "$OUT/$n.attempt$attempt.logcat.txt" 2>/dev/null; $ADB logcat -d | grep -a -c 'berberis: Cannot process signal') emulator translation crash lines; retrying)"
    attempt=$((attempt+1))
  done
  $ADB logcat -d -s Unity:V > "$OUT/$n.logcat.txt"
  $ADB shell am force-stop $PKG
  mkdir -p "$OUT/$n"; $ADB pull $FILES/sr/$n "$OUT/" > /dev/null 2>&1
  echo "== $n ($*)$([ "$crash" = "1" ] && echo " crash: $(grep -a -m1 'DEV CRASH while writing' "$OUT/$n.logcat.txt" | sed 's/.*\[Save\]/[Save]/')")"
  grep -a "\[sr\] phase\|\[sr\] boot report\|\[sr\] before\|\[sr\] backups(after)\|\[FAIL\]\|failures=\|EXC" "$OUT/$n/qa_saverecover.txt" 2>/dev/null | cut -c1-300
  grep -a "File.Replace failed\|backup write failed\|backup rotation failed" "$OUT/$n.logcat.txt" | head -2 | sed 's/^/   LOG: /'
}
step 01_seed 0 -srPhase seed
step 02_boot 0 -srPhase boot -srExpect Existing -srMile 7777 -srWriteExpect 1
step 03_lose 0 -srPhase boot -srLoseAtStart -srExpect RestoredFromBackup -srFrom lastgood -srMile 7777 -srCompare 1
step 04_boot 0 -srPhase boot -srExpect Existing -srMile 7777 -srCompare 1
step 05_set 0 -srPhase setmile -srValue 8888
step 06_boot 0 -srPhase boot -srExpect Existing -srMile 8888
step 07_corrupt 0 -srPhase boot -srLoseAtStart -srCorrupt lastgood -srExpect RestoredFromBackup -srFrom lastgood_prev -srMile prev -srCompare 1 -srDamaged 1
step 08_set 0 -srPhase setmile -srValue 1020
step 09_boot 0 -srPhase boot -srExpect Existing -srMile 1020
step 10_set 0 -srPhase setmile -srValue 5020
step 11_crash 1 -srPhase boot -qaBackupCrashAt cur-partial
step 12_lose 0 -srPhase boot -srLoseAtStart -srExpect RestoredFromBackup -srFrom lastgood -srMile latest -srNotMile 5020 -srCompare 1
step 13_boot 0 -srPhase boot -srExpect Existing -srCompare 1
step 14_bothbad 0 -srPhase boot -srLoseAtStart -srCorrupt both -srExpect NewGame -srMile 0 -srDamaged 1
step 15_cleanup 0 -srPhase cleanup
echo "== totals: $(grep -ah 'failures=' "$OUT"/*/qa_saverecover.txt | sort | uniq -c | tr '\n' ' ')"
