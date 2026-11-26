#!/bin/bash
# 製品版と同じ中身の確認用ビルド(OMM_RELEASE_PROBE、製品名「One More Mile Test」= 本物の保存と別)で、敵の出現と初回プレイ〜帰還の確認(2026-11-26)。
#  使い方: bash Tools/qa/release_probe.sh <出力フォルダ>   (先に BuildWindowsProductSandbox.BuildReleaseProbe)
#  強制終了は自分が起動した PID だけ
OUT=${1:?out}; mkdir -p "$OUT"
EXE=${EXE:-/c/GameProject/2ndAction/Unity/2ndAction/Builds/WindowsReleaseProbe/OneMoreMile.exe}
W() { cygpath -w "$1"; }
run() { # name timeout donefile args...
  local n=$1 to=$2 f=$3; shift 3
  "$EXE" -screen-width 1280 -screen-height 720 -screen-fullscreen 0 -logFile "$(W "$OUT")\\$n.log" "$@" >/dev/null 2>&1 &
  local p=$! t=0
  while kill -0 $p 2>/dev/null && [ $t -lt $to ]; do
    if [ -f "$OUT/$f" ] && grep -aq -E "^DONE|KILL" "$OUT/$f"; then sleep 2; break; fi
    sleep 3; t=$((t+3))
  done
  kill $p 2>/dev/null; wait $p 2>/dev/null
  echo "== $n (${t}s): $(grep -a -c '^PASS' "$OUT/$f" 2>/dev/null) PASS / $(grep -a -c '^FAIL' "$OUT/$f" 2>/dev/null) FAIL $(grep -a -m1 -o 'DONE.*\|KILL.*' "$OUT/$f" 2>/dev/null)"
  grep -a '^FAIL' "$OUT/$f" 2>/dev/null | head -5 | cut -c1-200
}
run run1 900 run_probe_1.txt -runProbe "$(W "$OUT")" -runProbePhase 1
run run2 900 run_probe_2.txt -runProbe "$(W "$OUT")" -runProbePhase 2
run run3 300 run_probe_3.txt -runProbe "$(W "$OUT")" -runProbePhase 3
run spawn 900 spawn_probe.txt -spawnProbe "$(W "$OUT")"   # 敵の出現は最後(脱出で MILE/最高距離が書かれ、run1 の「新しい保存」が崩れるため)
echo "exceptions: $(cat "$OUT"/*.log | grep -a -c Exception)"
