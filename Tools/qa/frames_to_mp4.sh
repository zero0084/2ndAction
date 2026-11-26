#!/bin/bash
# 連番の画像(f00000.png …)を動画に(2026-11-26)。使い方: bash frames_to_mp4.sh <画像のフォルダ> <出力.mp4> [fps 30]
FF=${FF:-/c/Users/0084k/AppData/Local/Microsoft/WinGet/Packages/Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe/ffmpeg-9.0.1-full_build/bin/ffmpeg.exe}
"$FF" -y -loglevel error -framerate ${3:-30} -i "$(cygpath -w "$1")\f%05d.png" -c:v libx264 -pix_fmt yuv420p -vf "scale=1280:-2" "$(cygpath -w "$2")" && ls -la "$2"
