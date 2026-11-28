#!/bin/bash
# 2026-11-28 依頼⑦: 4人マルチで死神三姉妹戦を縦横の混ざった端末で(HOST=横 / P2=縦・横から見る / P3=縦・上下2段 / P4=縦・斜め上)。
#  三姉妹の HP は本来の値(強化後)のまま。99,700m へワープ → 三姉妹 → 全員クリア → エンドロール → HOST が NO(ホームへ)。
#  全員が全カード(#100 ULTIMATE を除く)を Lv9 で持つ(マスター指定)。EXTRA="-sisBuff 0" で強化前、LDC=yes で HOST が YES
#  使い方: bash Tools/net/sis4.sh <名前> local|online coop|versus [上限(分)]
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
R=$ROOT/Docs/Sisters_2026-11-28/multi; mkdir -p "$R"
name=${1:?name}; NET=${2:-local}; GM=${3:-coop}; CAP=${4:-14}
CARDS="-netAutoCondAllCards 15:9" # 2026-11-28 マスター: 全カード(約99枚、#100 ULTIMATE を除く)を Lv9
STAGE=last_corridor COMMON="-netAutoNatRuns 1 -netAutoCondWarpAt 20:99700 $CARDS $EXTRA" \
  P1="-netAutoCondLdChoice ${LDC:-no}" P2="-pview side -netAutoCondLdChoice none" P3="-pview fold -netAutoCondLdChoice none" P4="-pview oblique -netAutoCondLdChoice none" \
  W1=640 H1=360 W2=360 H2=640 W3=360 H3=640 W4=360 H4=640 \
  bash "$ROOT/Tools/net/nat4.sh" "$R/$name" "$NET" "$GM" "$CAP" > "$R/$name.out" 2>&1
tail -14 "$R/$name.out" | cut -c1-300
echo "-- damage to sisters by attacker (HOST log)"
grep -a -h "\[NET\]\[BOSS\] Spawn id=.*ReaperSisterBoss\|\[NET\]\[BOSS\] Death id=.*ReaperSisterBoss" "$R/$name/p1_host.log" | sed 's/.*\[NET\]\[BOSS\] //' | cut -c1-160
grep -a -h "\[NET\]\[BOSS\] Damage id=" "$R/$name/p1_host.log" | sed 's/.*Damage id=\([0-9]*\) attacker=\(P[0-9]\).*/\1 \2/' | sort | uniq -c
echo "-- spread during the sisters fight (largest '先頭まで' on each device between the first sister and the clear)"
for f in "$R/$name"/p*_*.log; do
  s=$(grep -a -n "Solo 1/3\|SyncLdFinale\|sisters finale started" "$f" | head -1 | cut -d: -f1); e=$(grep -a -n "LAST DUNGEON CLEAR" "$f" | head -1 | cut -d: -f1)
  [ -z "$s" ] && s=1; [ -z "$e" ] && e=999999999
  m=$(sed -n "${s},${e}p" "$f" | grep -a -o "先頭まで [0-9,]*m" | tr -d ',m' | awk '{print $2}' | sort -n | tail -1)
  sp=$(sed -n "${s},${e}p" "$f" | grep -a "NAT utc" | grep -o "spd=[0-9.]*" | sort | uniq -c | sort -rn | head -2 | tr '\n' ' ')
  echo "$(basename $f .log): max gap to front ${m:-0}m | speeds $sp"
done
