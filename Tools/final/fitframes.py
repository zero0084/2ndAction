"""ラスボスのコマ(slice.py で切り分けたシートの各ポーズ)を、待機の絵(idle_00.png、1024 の正方形)と同じ縮尺・同じ置き方にそろえる(2026-11-06)。

  python fitframes.py <idle.png> <in_dir> <out_dir> --names move_00,move_01,... [--src a,b,c] [--anchor bottom|center] [--mul 1.0]

  ・縮尺: シート1枚の中は同じ縮尺で描かれている前提 → シートの全ポーズの「不透明な面積」の中央値を待機の面積に合わせる倍率を1つだけ使う
           (ポーズごとに合わせると、伏せ/立ち上がりで体の大きさが変わって見える)。--mul で微調整
  ・置き方: bottom = 足元(不透明の最下段)を待機の足元へ、左右は太い所(体)の重心を待機の重心へ(地上のボス)
            center = 太い所の重心を待機の重心へ(浮くボス)
  ・in_dir の <src>.png を順に読み、out_dir/<name>.png に書く(--src を省くと in_dir の png を名前順)
"""
import os
import sys
import argparse
import numpy as np
from PIL import Image

ap = argparse.ArgumentParser()
ap.add_argument('idle'); ap.add_argument('in_dir'); ap.add_argument('out_dir')
ap.add_argument('--names', required=True)
ap.add_argument('--src', default='')
ap.add_argument('--anchor', default='bottom')
ap.add_argument('--mul', type=float, default=1.0)
ap.add_argument('--dy', type=float, default=0.0)  # 置いた後に上下へずらす(px、+ で下)
a = ap.parse_args()


def alpha_of(im):
    return np.array(im)[..., 3].astype(np.float32) / 255.0


def thick_centroid(al):
    # 細い所(脚・尾・旗竿・翼の先)を除いた太い所の重心: 不透明の画素のうち、周囲 9x9 の不透明率が高い所だけ
    m = (al > 0.5).astype(np.float32)
    k = 9
    pad = np.pad(m, k // 2)
    cs = pad.cumsum(0).cumsum(1)
    cs = np.pad(cs, ((1, 0), (1, 0)))
    h, w = m.shape
    box = cs[k:k + h, k:k + w] - cs[0:h, k:k + w] - cs[k:k + h, 0:w] + cs[0:h, 0:w]
    thick = m * (box >= k * k * 0.85)
    if thick.sum() < 50: thick = m
    ys, xs = np.nonzero(thick)
    return xs.mean(), ys.mean()


def bottom_of(al):
    rows = np.nonzero((al > 0.5).sum(1) > 3)[0]
    return rows.max() if len(rows) else al.shape[0] - 1


idle = Image.open(a.idle).convert('RGBA')
ia = alpha_of(idle)
idle_area = (ia > 0.5).sum()
icx, icy = thick_centroid(ia)
ibot = bottom_of(ia)
S = idle.size[0]

names = a.names.split(',')
srcs = a.src.split(',') if a.src else sorted(f[:-4] for f in os.listdir(a.in_dir) if f.endswith('.png'))
assert len(srcs) == len(names), (srcs, names)
ims = [Image.open(os.path.join(a.in_dir, s + '.png')).convert('RGBA') for s in srcs]
areas = [(alpha_of(im) > 0.5).sum() for im in ims]
scale = float(np.sqrt(idle_area / max(1, np.median(areas)))) * a.mul
print(f'scale {scale:.3f} (idle area {idle_area}, sheet median {np.median(areas):.0f})')
os.makedirs(a.out_dir, exist_ok=True)
for im, n in zip(ims, names):
    w, h = im.size
    big = im.resize((max(1, round(w * scale)), max(1, round(h * scale))), Image.LANCZOS)
    al = alpha_of(big)
    cx, cy = thick_centroid(al)
    if a.anchor == 'bottom':
        ox = icx - cx
        oy = ibot - bottom_of(al)
    else:
        ox, oy = icx - cx, icy - cy
    oy += a.dy
    out = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    # 範囲外も扱えるように貼り付け(負の位置は切り取り)
    x0, y0 = int(round(ox)), int(round(oy))
    sx0, sy0 = max(0, -x0), max(0, -y0)
    dx0, dy0 = max(0, x0), max(0, y0)
    cw = min(big.size[0] - sx0, S - dx0); ch = min(big.size[1] - sy0, S - dy0)
    clipped = 0
    if cw > 0 and ch > 0:
        piece = big.crop((sx0, sy0, sx0 + cw, sy0 + ch))
        out.alpha_composite(piece, (dx0, dy0))
        clipped = int((al > 0.5).sum() - (alpha_of(piece) > 0.5).sum())
    out.save(os.path.join(a.out_dir, n + '.png'))
    print(f'{n}: area {(alpha_of(out) > 0.5).sum()} offset ({x0},{y0}) clipped {clipped}px')
