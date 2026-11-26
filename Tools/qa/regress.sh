#!/bin/bash
# 主要な回帰試験(2026-11-25、Unity 6000.6.10f1 への更新の確認用)。Windows 開発版(WindowsDevSandbox)で、試験ごとに専用の保存ファイル(-fileSave)。
# 本物のセーブ・production には触れない(ONLINE/ランキングは development)。同時に動かすのは 2 本まで。
# 使い方: bash Tools/qa/regress.sh <出力フォルダ> [試験名の絞り込み(正規表現)]
OUT=${1:?out dir}; ONLY=${2:-.}
EXE=${EXE:-/c/GameProject/2ndAction/Unity/2ndAction/Builds/WindowsDevSandbox/OneMoreMile.exe}
mkdir -p "$OUT"; SUM="$OUT/summary.txt"; : > "$SUM"
W() { cygpath -w "$1"; }
one() { # name save-key timeout args...
  local n=$1 sk=$2 to=$3; shift 3
  echo "$n" | grep -Eq "$ONLY" || return 0
  local d="$OUT/$n"; mkdir -p "$d"
  # 保存キー "-" = -fileSave を付けない(PlayerPrefs を直接使う試験。試験版の別製品名のレジストリだけを使い、試験自身が前後で控え/戻す)
  local fs=(); [ "$sk" != "-" ] && { mkdir -p "$OUT/saves/$sk"; fs=(-fileSave "$(W "$OUT/saves/$sk/save.json")"); }
  local t0=$(date +%s)
  timeout $to "$EXE" -screen-width 1280 -screen-height 720 -screen-fullscreen 0 "${fs[@]}" -logFile "$(W "$d")\log.txt" "$@" >/dev/null 2>&1 &
  local tp=$!; memwatch $tp "$d/mem.csv" & local mp=$!
  wait $tp; local rc=$? t=$(( $(date +%s) - t0 )); kill $mp 2>/dev/null; wait $mp 2>/dev/null
  local mem=$(awk -F, 'NR==1{f=$2} {l=$2; if($2>m)m=$2} END{if(NR) printf "mem MB first %d max %d last %d (%d samples)", f/1024, m/1024, l/1024, NR}' "$d/mem.csv" 2>/dev/null)
  local q=$(ls "$d"/qa_*.txt 2>/dev/null | head -1)
  local res="NO RESULT"; [ -n "$q" ] && res=$(grep -a -E "ALL CHECKS PASSED|CHECK\(S\) FAILED" "$q" | tail -1)
  local ex=$(grep -a -c "Exception" "$d/log.txt" 2>/dev/null)
  echo "$n | ${res:-?} | exit $rc | ${t}s | exceptions in log $ex | $mem" | tee -a "$SUM"
}
# 試験中の exe のメモリ(作業セット、KB)を30秒ごとに記録: 秒,KB
memwatch() { local tp=$1 f=$2 s=0 wp=""; : > "$f"
  for i in $(seq 1 20); do sleep 1; local c=$(ps -ef | awk -v p=$tp '$3==p{print $2}' | head -1); [ -n "$c" ] && wp=$(cat /proc/$c/winpid 2>/dev/null) && [ -n "$wp" ] && break; done
  [ -z "$wp" ] && return
  while kill -0 $tp 2>/dev/null; do
    local kb=$(tasklist //FI "PID eq $wp" //FO CSV //NH 2>/dev/null | awk -F'","' '{gsub(/[^0-9]/,"",$5); print $5}')
    [ -n "$kb" ] && echo "$s,$kb" >> "$f"; sleep 30; s=$((s+30)); done; }
pair() { "$@" & }
# --- セーブ・引き継ぎ
one save          -        900 -qaSave "$(W "$OUT/save")"
TR=$(W "$OUT/trshared"); mkdir -p "$OUT/trshared"
one tr1_seedA     trA      300 -qaTransfer "$(W "$OUT/tr1_seedA")" -trPhase seedA -trShared "$TR"
one tr1b_bootA    trA      300 -qaTransfer "$(W "$OUT/tr1b_bootA")" -trPhase bootA -trShared "$TR"
one tr2_seedB     trB      300 -qaTransfer "$(W "$OUT/tr2_seedB")" -trPhase seedB -trShared "$TR"
one tr2b_bootB    trB      300 -qaTransfer "$(W "$OUT/tr2b_bootB")" -trPhase bootB -trShared "$TR"
one tr3_cancel    trB      300 -qaTransfer "$(W "$OUT/tr3_cancel")" -trPhase cancel -trShared "$TR"
one tr4_reject    trB      300 -qaTransfer "$(W "$OUT/tr4_reject")" -trPhase reject -trShared "$TR"
one tr5_import    trB      300 -qaTransfer "$(W "$OUT/tr5_import")" -trPhase import -trShared "$TR"
one tr6_boot      trB      300 -qaTransfer "$(W "$OUT/tr6_boot")" -trPhase boot -trShared "$TR"
one tr7_again     trB      300 -qaTransfer "$(W "$OUT/tr7_again")" -trPhase again -trShared "$TR"
one tr8_old       trOld    300 -qaTransfer "$(W "$OUT/tr8_old")" -trPhase old -trShared "$TR"
# --- 広告・購入・購入の復元(模擬)・ログイン特典
one monet         monet    900 -qaMonet "$(W "$OUT/monet")" -monetizationMock 1
one login_win     lbw      300 -qaLoginBonus "$(W "$OUT/login_win")" -loginBonusOn -lbCase windows
one login_r1      lbr      300 -qaLoginBonus "$(W "$OUT/login_r1")" -loginBonusOn -lbCase restart1
one login_r2      lbr      300 -qaLoginBonus "$(W "$OUT/login_r2")" -loginBonusOn -lbCase restart2
one login_mobile  lbm      300 -qaLoginBonus "$(W "$OUT/login_mobile")" -loginBonusOn -lbCase mobilefree -loginBonusAs mobile
one login_pass    lbp      300 -qaLoginBonus "$(W "$OUT/login_pass")" -loginBonusOn -lbCase mobilepass -loginBonusAs mobile -monetizationMock 1
# --- CONTINUE・ランキング(development)
one resume        resume   900 -qaResume "$(W "$OUT/resume")"
one rank_cont     rank     900 -qaRankRoute "$(W "$OUT/rank_cont")" -rrPhase cont -ugsProfile regress_rank
one records       -        600 -qaRecords "$(W "$OUT/records")"
one acprobe       ac       600 -qaAcProbe "$(W "$OUT/acprobe")" -ugsProfile regress_ac
# --- 画面(縦横・戻る・UI)
one uicheck       ui       600 -qaUiCheck "$(W "$OUT/uicheck")"
one keynav        keynav   600 -qaKeyNav "$(W "$OUT/keynav")"
one portrait      portrait 1200 -qaPortrait "$(W "$OUT/portrait")"
one result        result   600 -qaResult "$(W "$OUT/result")"
one retry         retry    600 -qaRetry "$(W "$OUT/retry")"
# --- 遊び・性能
one boss          boss     1500 -qaBoss "$(W "$OUT/boss")"
one halt          halt     1500 -qaHalt "$(W "$OUT/halt")"
one rewards       rewards  900 -qaRewards "$(W "$OUT/rewards")"
one fullrun20k    full     1500 -qaFullRun "$(W "$OUT/fullrun20k")" -qaStage wasteland_road -qaChar swordsman -qaKmh 0 -qaStopAt 20000 -qaNoShots 1
one fps           fps      900 -qaFps "$(W "$OUT/fps")"
echo "== done" | tee -a "$SUM"
