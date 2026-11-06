#!/bin/bash
# proc.sh <boss> <sheet.png in ArtSource_final> <bg green|magenta> <names,comma> [anchor=bottom] [mul=0.96] [extra slice args...]
# エステルのシート(横一列のポーズ)→ 切り分け → 待機の縮尺へ合わせる → Resources/BossAnim/<boss>/ へ → 確認用の並べ画像 chk_<sheet>.jpg
set -e
B=$1; SH=$2; BG=$3; NAMES=$4; AN=${5:-bottom}; MUL=${6:-0.96}; shift 6 2>/dev/null || shift $#
PY=/c/Users/0084k/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe
A=/c/GameProject/ArtSource_final
T=/c/GameProject/2ndAction/Tools
cd $A
base=${SH%.png}
n=$(echo $NAMES | tr ',' '\n' | wc -l)
tmp=$(seq -s, 0 $((n-1)) | sed 's/\([0-9]*\)/p\1/g')
rm -rf sl_$base; $PY -I $T/air/slice.py $SH sl_$base --bg $BG --names $tmp --max 2048 "$@" | tail -1
$PY -I $T/final/fitframes.py cut_$B/idle.png sl_$base fit_$B --names $NAMES --src $tmp --anchor $AN --mul $MUL
mkdir -p /c/GameProject/2ndAction/Unity/2ndAction/Assets/Resources/BossAnim/$B
for nm in $(echo $NAMES | tr ',' ' '); do cp fit_$B/$nm.png /c/GameProject/2ndAction/Unity/2ndAction/Assets/Resources/BossAnim/$B/; done
$PY -I -c "
import sys
from PIL import Image
names=sys.argv[3].split(',')
import os
ims=[Image.open(sys.argv[2]+'/idle_00.png') if os.path.exists(sys.argv[2]+'/idle_00.png') else Image.open(sys.argv[1]+'/idle.png')]+[Image.open(sys.argv[2]+'/'+n+'.png') for n in names]
W=Image.new('RGBA',(256*len(ims),256),(60,70,80,255))
for i,im in enumerate(ims): W.alpha_composite(im.resize((256,256)),(256*i,0))
W.convert('RGB').save('chk_'+sys.argv[4]+'.jpg')" cut_$B fit_$B $NAMES $base
echo "chk: $A/chk_$base.jpg"
