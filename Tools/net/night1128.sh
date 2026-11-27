#!/bin/bash
# 2026-11-28 夜〜朝(マスター依頼): 4人の CO-OP を各マップ順に、通常条件で(ワープ・HP の指定・強制の致命傷なし)。各マップ上限 2 時間。
#  ・30分ごと/上限の直前に HOST と参加者の画面を撮る(-netAutoHudShots。上限を超えた時に何km・どこで止まったかを残す)
#  ・ラスダン: エンドロールの文字の壁を bot が攻撃しないので、試験側で前攻撃を押す(-netAutoCondLdChoice。HOST は NO を選ぶ)
#  使い方: bash Tools/net/night1128.sh [マップ...](省略で last_corridor wasteland_road natural_cave sky_corridor)
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
R=$ROOT/Docs/Multi4_2026-11-28/night; mkdir -p "$R"
MAPS=${*:-last_corridor wasteland_road natural_cave sky_corridor}
END_AT=${END_AT:-08:00}
SHOTS="1800,3600,5400,7080"
for m in $MAPS; do
  now=$(date +%H:%M); if [[ "$now" > "$END_AT" ]] && [[ "$now" < "12:00" ]]; then echo "=== skip $m (past $END_AT)"; continue; fi
  name=N_${m}
  SD=$(cygpath -w "$R/${name}_shots")
  if [ "$m" = last_corridor ]; then P1X="-netAutoCondLdChoice no"; PJX="-netAutoCondLdChoice none"; else P1X=""; PJX=""; fi
  echo "=== $name $(date +%H:%M:%S)"
  STAGE=$m COMMON="-netAutoNatRuns 1 -netAutoHudShots $SHOTS -netAutoShotDir $SD" P1="$P1X" P2="$PJX" P3="$PJX" P4="$PJX" \
    bash "$ROOT/Tools/net/nat4.sh" "$R/$name" local coop 120 > "$R/$name.out" 2>&1
  tail -12 "$R/$name.out" | cut -c1-260
done
echo "=== night done $(date +%H:%M:%S)"
