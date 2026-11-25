#!/bin/bash
# Android 復元の見分けの試験(エミュレーターだけ)
ADB="/c/Program Files/Unity/Hub/Editor/6000.5.6f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe"
P=com.zero0084.action2nd
APK=/c/GameProject/2ndAction/Unity/2ndAction/Builds/Android/2ndAction.apk
run() { # $1 label $2 extra
  "$ADB" shell am force-stop $P; "$ADB" logcat -c
  "$ADB" shell "am start -n $P/com.unity3d.player.UnityPlayerGameActivity -e unity '-force-gles30 -qaInstallGuard /sdcard/Android/data/$P/files/ig $2'" >/dev/null
  for i in $(seq 1 40); do sleep 3; "$ADB" logcat -d -s Unity 2>/dev/null | grep -q "\[ig\] after" && break; done
  echo "== $1"; "$ADB" logcat -d -s Unity 2>/dev/null | grep -a -E "\[ig\] (player|before|after)|\[Online\]|Authentication succeeded|failures=" | sed "s/^.*Unity *: //" | tr -d "
" | sort -u
  sleep 3
}
case "$1" in
 fresh) "$ADB" uninstall $P >/dev/null 2>&1; "$ADB" install $APK | tail -1; run "S1 fresh install" "-igJoin 1";;
 preguard) "$ADB" shell "run-as $P rm -f no_backup/omm_install_id"; "$ADB" shell "run-as $P sh -c 'sed -i \"/Device.InstallIdV1/d\" shared_prefs/$P.v2.playerprefs.xml'"; run "S2 update from a version without the guard (no marks)";;
 update) "$ADB" install -r $APK | tail -1; run "S3 normal update (install -r)";;
 restoresim) "$ADB" shell "run-as $P rm -f no_backup/omm_install_id"; run "S4 restore simulated (no_backup mark missing)";;
 backup) "$ADB" shell bmgr enable true; "$ADB" shell bmgr transport com.android.localtransport/.LocalTransport; "$ADB" shell bmgr backupnow $P 2>&1 | tail -2;;
 reinstall_restore) "$ADB" shell bmgr autorestore true; "$ADB" uninstall $P | tail -1; "$ADB" install $APK | tail -1; run "S5 uninstall + install with the backup restored";;
 reinstall_clean) "$ADB" shell bmgr autorestore false; "$ADB" uninstall $P | tail -1; "$ADB" install $APK | tail -1; run "S6 uninstall + install without a backup" "-igJoin 0";;
 join) run "join before the backup" "-igJoin 1";;
 strip) "$ADB" shell "run-as $P rm -f no_backup/omm_install_id"; "$ADB" shell "run-as $P sh -c 'sed -i \"/Device.InstallIdV1/d\" shared_prefs/$P.v2.playerprefs.xml'"; echo "marks removed (= the state of a version before the guard)";;
 oldupdate) bash "$0" strip; "$ADB" install -r $APK | tail -1; run "S8 update from a version before the guard (install -r)";;
 oldbackup) bash "$0" strip; bash "$0" backup; "$ADB" shell bmgr autorestore true; "$ADB" uninstall $P | tail -1; "$ADB" install $APK | tail -1; "$ADB" shell bmgr transport com.google.android.gms/.backup.BackupTransportService >/dev/null; run "S7 restore of a backup made before the guard";;
 files) "$ADB" shell "run-as $P ls -la no_backup shared_prefs";;
esac
