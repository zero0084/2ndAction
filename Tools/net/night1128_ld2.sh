#!/bin/bash
# 2026-11-28 朝: ラスダンの補助試験(ワープなし・カードを強くして 100km の三姉妹まで走らせる)。夜の通しの後、8:00 までの残り時間で
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
R=$ROOT/Docs/Multi4_2026-11-28/night; mkdir -p "$R"
until grep -q "night done" "$ROOT/Docs/Multi4_2026-11-28/night_run.out" 2>/dev/null; do sleep 30; done
now=$(date +%s); end=$(date -d "today 08:00" +%s); left=$(( (end - now) / 60 - 12 ))
[ $left -gt 120 ] && left=120
if [ $left -lt 30 ]; then echo "=== skip LD2 (only ${left} min left)"; exit 0; fi
CARDS="-netAutoCondCards 5:attack_up:9,heart_up:9,shield:9,iron_heart:9,jump_count_up:3"
SD=$(cygpath -w "$R/N_last_corridor_support_shots")
echo "=== N_last_corridor_support $(date +%H:%M:%S) cap ${left} min"
STAGE=last_corridor COMMON="-netAutoNatRuns 1 -netAutoHudShots 1800,3600,5400,$(( left * 60 - 60 )) -netAutoShotDir $SD $CARDS" P1="-netAutoCondLdChoice no" P2="-netAutoCondLdChoice none" P3="-netAutoCondLdChoice none" P4="-netAutoCondLdChoice none" \
  bash "$ROOT/Tools/net/nat4.sh" "$R/N_last_corridor_support" local coop $left > "$R/N_last_corridor_support.out" 2>&1
tail -12 "$R/N_last_corridor_support.out" | cut -c1-260
echo "=== LD2 done $(date +%H:%M:%S)"
