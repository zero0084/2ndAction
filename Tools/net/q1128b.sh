#!/bin/bash
# 2026-11-28 追加の撮り直し(q1128.sh を実行中に書き換えないため別ファイル)
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
R=$ROOT/Docs/Multi4_2026-11-28/runs
run() { local name=$1; shift; echo "=== $name $* $(date +%H:%M:%S)"; bash "$ROOT/Tools/net/nat4.sh" "$R/$name" "$@" > "$R/$name.out" 2>&1; tail -10 "$R/$name.out"; }
LDW="-netAutoCondWarpAt 20:99700"
# P4 は静寂の区間(三姉妹戦の前)で DOWN、救助なし → クリアで立ち上がってエンドロールを走れるか
# (L1b 済み)
# VERSUS: P3 は三姉妹戦の前に脱落 → クリアの時点で P3 にも試合の結果(順位・ボーナス)が届くか
STAGE=last_corridor COMMON="-netAutoNatRuns 1 $LDW" P1="-netAutoCondSisterHp 60 -netAutoCondLdChoice no" P2="" P3="-netAutoCondKillAt 27" P4="" run L3c_versus_ld_no local versus 9
echo "=== done $(date +%H:%M:%S)"
