#!/bin/bash
# 2026-11-28 朝: ラスダンの補助試験(ワープなし・カードを強くして 100km の三姉妹まで走らせる)。夜の通しの後、8:00 までの残り時間で
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
R=$ROOT/Docs/Multi4_2026-11-28/night; mkdir -p "$R"
until grep -q "LD2 done" "$ROOT/Docs/Multi4_2026-11-28/night_ld2.out" 2>/dev/null; do sleep 30; done
now=$(date +%s); end=$(date -d "today 08:00" +%s); left=$(( (end - now) / 60 - 12 ))
[ $left -gt 120 ] && left=120
if [ $left -lt 30 ]; then echo "=== skip WS (only ${left} min left)"; exit 0; fi
CARDS="-netAutoCondCards 5:attack_up:9,heart_up:9,shield:9,iron_heart:9,jump_count_up:3"
SD=$(cygpath -w "$R/N_wasteland_support_shots")
echo "=== N_wasteland_support $(date +%H:%M:%S) cap ${left} min"
STAGE=wasteland_road COMMON="-netAutoNatRuns 1 -netAutoHudShots 1800,3600,5400,$(( left * 60 - 60 )) -netAutoShotDir $SD $CARDS" P1="" P2="" P3="" P4="" \
  bash "$ROOT/Tools/net/nat4.sh" "$R/N_wasteland_support" local coop $left > "$R/N_wasteland_support.out" 2>&1
tail -12 "$R/N_wasteland_support.out" | cut -c1-260
echo "=== WS done $(date +%H:%M:%S)"
