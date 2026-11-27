#!/bin/bash
# 2026-11-28 第3弾(マスター依頼 ②③④)の試験を順に走らせる(4プロセスを1組ずつ。重い処理は1つずつ)。
#  使い方: bash Tools/net/q1128.sh <名前...>(省略で全部)。出力は Docs/Multi4_2026-11-28/runs/<名前>
#  条件を作る試験(C*/H*/OC1)は通常の通し試験(N*/O1/O2)とは別に記録する。
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
R=$ROOT/Docs/Multi4_2026-11-28/runs; mkdir -p "$R"
W="-netAutoCondWarpAt 20:99200"
run() { # name net mode cap  (env: COMMON P1..P4 HOSTX STAGE)
  local name=$1; shift
  echo "=== $name $* $(date +%H:%M:%S)"
  bash "$ROOT/Tools/net/nat4.sh" "$R/$name" "$@" > "$R/$name.out" 2>&1
  tail -12 "$R/$name.out"
}
want() { [ $# -eq 0 ] && return 0; for x in $ALL; do [ "$x" = "$1" ] && return 0; done; return 1; }
ALL="$*"
sel() { [ -z "$ALL" ] && return 0; for x in $ALL; do [ "$x" = "$1" ] && return 0; done; return 1; }

# ③ 死神で倒れた(CO-OP): P2 = HP 1 で死神に / P1(HOST)= PHOENIX を持って死神に / P3 = 死神ではない致命傷(DOWN → 救助)
sel C1 && COMMON="-netAutoNatRuns 1 $W" P1="-netAutoCondCards 12:phoenix:1 -netAutoCondPhoenixKeep 1 -netAutoCondHpKeep 25:20" P2="-netAutoCondHpKeep 25:10" P3="-netAutoCondHpKeep 25:10 -netAutoCondKillAt 35" P4="" run C1b_coop_reaper local coop 7
# ③ HOST の PHOENIX(普通の致命傷では復活する=対照)→ 最終進化(PHOENIX)中に死神
sel C2 && COMMON="-netAutoNatRuns 1 $W" P1="-netAutoCondFeAt 24:phoenix -netAutoCondFeKeep 1 -netAutoCondHpKeep 25:20" P2="" P3="" P4="" run C2_coop_fe local coop 7
# ③ VERSUS: HOST が PHOENIX を持って死神に / P2 は HP 1 で死神に / P3 は普通の致命傷(脱落は今まで通り)
sel C3 && COMMON="-netAutoNatRuns 1 $W" P1="-netAutoCondCards 12:phoenix:1 -netAutoCondPhoenixKeep 1 -netAutoCondHpKeep 25:20" P2="-netAutoCondHpKeep 25:10" P3="-netAutoCondKillAt 35" P4="" run C3_versus_reaper local versus 7
# ③ 例外: ラスダン(last_corridor)では死神に倒されても今まで通り DOWN → 救助
sel C4 && STAGE=last_corridor COMMON="-netAutoNatRuns 1 $W" P1="-netAutoCondCards 12:phoenix:1 -netAutoCondPhoenixKeep 1 -netAutoCondHpKeep 25:20" P2="-netAutoCondHpKeep 25:10" P3="" P4="" run C4_coop_lastdungeon local coop 6
# ④ HOST が脱出 → 全員クリア(状態がばらばら): P2 = カード選択中のまま / P3 = 一時停止中 / P4 = DOWN(救助を押さない)
sel H1 && COMMON="-netAutoNatRuns 1" P1="-netAutoNatEscapeAt 1150" P2="-netAutoNatHold 900,900" P3="-netAutoNatPauseAt 55 -netAutoNatPauseFor 900" P4="-netAutoCondKillAt 65 -netAutoCondNoRevive 1" run H1_coop_hostclear local coop 8
# ④ VERSUS: 同じ(P4 は脱落 = 結果を保持)
sel H2 && COMMON="-netAutoNatRuns 1" P1="-netAutoNatEscapeAt 1150" P2="-netAutoNatHold 900,900" P3="-netAutoNatPauseAt 55 -netAutoNatPauseFor 900" P4="-netAutoCondKillAt 65" run H2b_versus_hostclear local versus 8
# 通常条件の通し(HOST が自然に脱出 → 全員クリア)。順位の表示も記録(NAT 行の rank)
sel N1 && COMMON="-netAutoNatRuns 1" W1=1280 H1=720 W2=540 H2=960 P1="-netAutoNatEscapeAt 3300 -netAutoHudShots 45,110,170,230 -netAutoShotDir $(cygpath -w $R/N1_coop_natural_shots)" P2="-netAutoHudShots 45,110,170,230 -netAutoShotDir $(cygpath -w $R/N1_coop_natural_shots)" P3="" P4="" run N1_coop_natural local coop 14
sel N2 && COMMON="-netAutoNatRuns 1" W1=1280 H1=720 W2=540 H2=960 P1="-netAutoNatEscapeAt 3300 -netAutoHudShots 45,110,170,230 -netAutoShotDir $(cygpath -w $R/N2_versus_natural_shots)" P2="-netAutoHudShots 45,110,170,230 -netAutoShotDir $(cygpath -w $R/N2_versus_natural_shots)" P3="" P4="" run N2_versus_natural local versus 14
# Relay(ONLINE、UGS development)
sel O1 && COMMON="-netAutoNatRuns 1" P1="-netAutoNatEscapeAt 3300" P2="" P3="" P4="" run O1_online_coop online coop 14
sel O2 && COMMON="-netAutoNatRuns 1" P1="-netAutoNatEscapeAt 3300" P2="" P3="" P4="" run O2_online_versus online versus 14
sel OC1 && COMMON="-netAutoNatRuns 1 $W" P1="-netAutoCondCards 12:phoenix:1 -netAutoCondPhoenixKeep 1 -netAutoCondHpKeep 25:20" P2="-netAutoCondSpeedAt 24:0.55 -netAutoCondHpKeep 25:10" P3="-netAutoCondHpKeep 25:10 -netAutoCondKillAt 35" P4="" run OC1_online_reaper online coop 7
# ③ 参加者が死神に倒される(遅く走る参加者を死神が狙う)。2回目のランで状態が残らないことも見る(-netAutoNatRuns 2)
sel C1c && COMMON="-netAutoNatRuns 2 $W" P1="-netAutoCondCards 12:phoenix:1 -netAutoCondPhoenixKeep 1" P2="-netAutoCondSpeedAt 24:0.55 -netAutoCondHpKeep 25:10" P3="-netAutoCondSpeedAt 24:0.6 -netAutoCondHpKeep 25:10" P4="" run C1c_coop_reaper_join local coop 12
sel C3c && COMMON="-netAutoNatRuns 1 $W" P1="" P2="-netAutoCondSpeedAt 24:0.55 -netAutoCondHpKeep 25:10" P3="" P4="" run C3c_versus_reaper_join local versus 7
# ④ ラスダン(補助試験: 99,700m へワープ・三姉妹の HP を短く)。P4 は三姉妹戦の間に DOWN(救助なし)= クリアで立ち上がる
LDW="-netAutoCondWarpAt 20:99700"
sel L1 && STAGE=last_corridor COMMON="-netAutoNatRuns 1 $LDW" P1="-netAutoCondSisterHp 300 -netAutoCondLdChoice no" P2="" P3="" P4="-netAutoCondKillAt 45 -netAutoCondNoRevive 1" run L1_coop_ld_no local coop 9
sel L2 && STAGE=last_corridor COMMON="-netAutoNatRuns 1 $LDW" P1="-netAutoCondSisterHp 300 -netAutoCondLdChoice yes" P2="" P3="" P4="" run L2_coop_ld_yes local coop 8
sel L3 && STAGE=last_corridor COMMON="-netAutoNatRuns 1 $LDW" P1="-netAutoCondSisterHp 300 -netAutoCondLdChoice no" P2="" P3="-netAutoCondKillAt 40" P4="" run L3_versus_ld_no local versus 9
sel OL1 && STAGE=last_corridor COMMON="-netAutoNatRuns 1 $LDW" P1="-netAutoCondSisterHp 300 -netAutoCondLdChoice no" P2="" P3="" P4="" run OL1_online_ld_no online coop 10
sel OL2 && STAGE=last_corridor COMMON="-netAutoNatRuns 1 $LDW" P1="-netAutoCondSisterHp 300 -netAutoCondLdChoice yes" P2="" P3="" P4="" run OL2_online_versus_ld_yes online versus 9
sel OC3 && COMMON="-netAutoNatRuns 1 $W" P1="-netAutoCondCards 12:phoenix:1 -netAutoCondPhoenixKeep 1" P2="-netAutoCondSpeedAt 24:0.55 -netAutoCondHpKeep 25:10" P3="" P4="" run OC3_online_versus_reaper online versus 7
sel OH2 && COMMON="-netAutoNatRuns 1" P1="-netAutoNatEscapeAt 1150" P2="-netAutoNatHold 900,900" P3="-netAutoNatPauseAt 55 -netAutoNatPauseFor 900" P4="-netAutoCondKillAt 65" run OH2_online_versus_hostclear online versus 8
echo "=== done $(date +%H:%M:%S)"
