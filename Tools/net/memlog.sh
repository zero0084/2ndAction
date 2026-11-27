#!/bin/bash
# 5分ごとにゲームのプロセスのメモリ(Private MB)を記録する(朝 8:10 まで)
OUT=${1:?out}
while [[ "$(date +%H:%M)" < "08:10" ]]; do
  echo "$(date +%H:%M:%S) $(powershell -NoProfile -Command "Get-Process OneMoreMile -ErrorAction SilentlyContinue | ForEach-Object { '{0}:{1}MB' -f \$_.Id, [int](\$_.PrivateMemorySize64/1MB) }" | tr '\n' ' ')" >> "$OUT"
  sleep 300
done
