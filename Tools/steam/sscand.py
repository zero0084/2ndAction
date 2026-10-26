"""sscand.py <素材フォルダ> <候補.json> <出力.jpg> [幅=360]: スクリーンショットの候補を並べる。候補.json = [[名前, ショット, 中心の秒, 刻み], ...]
各行に中心の前後 5 コマ(-2..+2 刻み)。選んだら ssexport.py で原寸を書き出す"""
import sys, json, subprocess, os
import numpy as np
from PIL import Image, ImageDraw
FF = os.environ.get('FFMPEG', 'ffmpeg')
root, cj, out = sys.argv[1:4]; W = int(sys.argv[4]) if len(sys.argv) > 4 else 360; H = W * 9 // 16
C = json.load(open(cj, encoding='utf-8'))
def grab(p, t):
    r = subprocess.run([FF, '-v', 'error', '-ss', f'{max(0, t):.3f}', '-i', p, '-frames:v', '1', '-vf', f'scale={W}:{H}', '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-'], capture_output=True)
    a = np.frombuffer(r.stdout, np.uint8); return Image.fromarray(a.reshape(H, W, 3)) if a.size == W * H * 3 else Image.new('RGB', (W, H))
s = Image.new('RGB', (W * 5 + 150, len(C) * (H + 18)), (30, 30, 30)); d = ImageDraw.Draw(s)
for r, (name, shot, t, st) in enumerate(C):
    y = r * (H + 18); d.text((4, y + 20), name, fill=(255, 255, 0)); d.text((4, y + 40), shot, fill=(200, 200, 200))
    for k in range(5):
        tt = t + (k - 2) * st; s.paste(grab(os.path.join(root, shot + '.mkv'), tt), (150 + k * W, y + 18)); d.text((150 + k * W + 3, y + 3), f'{tt:.2f}', fill=(120, 255, 120))
s.save(out, quality=85); print(out)
