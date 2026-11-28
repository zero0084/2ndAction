#!/bin/bash
# 2026-11-28 依頼⑦: 命中の比較(修正の前 -sisReach 0 / 後)と、強化の前後(-sisBuff 0 / 1、倒すまで)を順に(1 プロセスずつ)
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
D=$ROOT/Docs/Sisters_2026-11-28
# A: 命中(各段階 20 秒、HP 固定)
EXTRA="-sisReach 0" SECS=20 bash "$ROOT/Tools/qa/sishit.sh" "$D/hit_before" swordsman gunslinger
SECS=20 bash "$ROOT/Tools/qa/sishit.sh" "$D/hit_after" swordsman gunslinger
# B: 強化の前後(HP は本来の値、倒すまで。修正後の命中)。横と縦(横から見る)
TMO=1800 VIEWS="land side" MODE=sishit EXTRA="-shKill 1 -sisBuff 0 -ldPower 6" bash "$ROOT/Tools/qa/sishit.sh" "$D/buff_before" swordsman gunslinger
TMO=1800 VIEWS="land side" MODE=sishit EXTRA="-shKill 1 -ldPower 6" bash "$ROOT/Tools/qa/sishit.sh" "$D/buff_after" swordsman gunslinger
echo "=== all done $(date +%H:%M:%S)"
