"""shotgrid.py <映像> <出力.jpg> [刻み秒=0.5] [幅=384] [開始] [終了]: ショットを一定の刻みで並べた一覧(時刻つき)。カットの開始位置を選ぶ時に見る"""
import sys, subprocess, os
import numpy as np
from PIL import Image, ImageDraw
FF = os.environ.get('FFMPEG', 'ffmpeg')
p, out = sys.argv[1:3]; step = float(sys.argv[3]) if len(sys.argv) > 3 else 0.5; W = int(sys.argv[4]) if len(sys.argv) > 4 else 384
t0 = float(sys.argv[5]) if len(sys.argv) > 5 else 0.0; t1 = float(sys.argv[6]) if len(sys.argv) > 6 else 1e9
H = W * 9 // 16
r = subprocess.run([FF, '-v', 'error', '-ss', str(t0), '-i', p, '-t', str(min(t1 - t0, 600)), '-vf', f'fps={1/step},scale={W}:{H}', '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-'], capture_output=True)
a = np.frombuffer(r.stdout, np.uint8).reshape(-1, H, W, 3)
cols = 5; rows = (len(a) + cols - 1) // cols
s = Image.new('RGB', (cols * W, rows * (H + 16)), (30, 30, 30)); d = ImageDraw.Draw(s)
for i, f in enumerate(a):
    x = (i % cols) * W; y = (i // cols) * (H + 16); s.paste(Image.fromarray(f), (x, y + 16)); d.text((x + 3, y + 2), f'{t0 + i * step:.2f}s', fill=(255, 255, 0))
s.save(out, quality=85); print(out, len(a))
