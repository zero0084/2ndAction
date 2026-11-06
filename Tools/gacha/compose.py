# ガチャのマップ別の飾り(2026-11-06)。エステルの部品の絵(単色背景)を切り抜き、筐体の位置に合わせて 640x768 の飾りの絵へ置く。
#  使い方: python compose.py <spec.json>
#  spec: {"src": 部品の絵, "bg": "blue"|"green", "out_dir": 出力先, "key": "wilderness",
#         "parts": [{"pick": [x0,y0,x1,y1](元の絵の中の範囲), "layer": "front"|"back", "box": [x0,y0,x1,y1](筐体 572x687 の座標。縦横比は保って box に収める), "anchor": "bottom"|"center"|"top", "mirror": false}]}
#  飾りの絵: 筐体の中心を中心に 1.6 倍の範囲(GachaSkinFx.DecoScale)。筐体は (120,144) から 400x481(倍率 0.6993)
import json, sys, os
import numpy as np
from PIL import Image
spec = json.load(open(sys.argv[1], encoding='utf-8'))
W, H = 640, 768; MW, MH = 572, 687; K = 400 / MW
OX, OY = (W - MW * K) / 2, (H - MH * K) / 2
src = Image.open(spec['src']).convert('RGB'); a = np.asarray(src).astype(np.float32)
R, G, B = a[..., 0], a[..., 1], a[..., 2]
if spec.get('bg', 'blue') == 'blue':
    key = B - np.maximum(R, G)          # 青っぽさ
    alpha = np.clip(1.0 - (key - 60) / 90.0, 0, 1)
    B2 = np.minimum(B, np.maximum(R, G) * 1.05)  # 青のにじみを消す
    rgb = np.stack([R, G, B2], -1)
else:
    key = G - np.maximum(R, B)
    alpha = np.clip(1.0 - (key - 40) / 70.0, 0, 1)
    G2 = np.minimum(G, np.maximum(R, B) * 1.0)
    rgb = np.stack([R, G2, B], -1)
rgba = np.concatenate([np.clip(rgb, 0, 255), (alpha * 255)[..., None]], -1).astype(np.uint8)
cut = Image.fromarray(rgba, 'RGBA')
layers = {'front': Image.new('RGBA', (W, H)), 'back': Image.new('RGBA', (W, H))}
for p in spec['parts']:
    piece = cut.crop(tuple(p['pick']))
    for ex in p.get('erase', []):  # 元の絵の座標の四角を消す(隣の部品のはみ出し)
        x0e, y0e, x1e, y1e = ex; px0, py0 = p['pick'][0], p['pick'][1]
        piece.paste((0, 0, 0, 0), (x0e - px0, y0e - py0, x1e - px0, y1e - py0))
    bb = piece.split()[3].point(lambda v: 255 if v > 24 else 0).getbbox()
    piece = piece.crop(bb)
    if p.get('mirror'): piece = piece.transpose(Image.FLIP_LEFT_RIGHT)
    x0, y0, x1, y1 = p['box']
    bw, bh = (x1 - x0) * K, (y1 - y0) * K
    s = min(bw / piece.width, bh / piece.height)
    pw, ph = max(1, round(piece.width * s)), max(1, round(piece.height * s))
    piece = piece.resize((pw, ph), Image.LANCZOS)
    px = OX + x0 * K + (bw - pw) / 2
    anc = p.get('anchor', 'center')
    py = OY + y0 * K + (bh - ph if anc == 'bottom' else 0 if anc == 'top' else (bh - ph) / 2)
    layers[p.get('layer', 'front')].alpha_composite(piece, (round(px), round(py)))
os.makedirs(spec['out_dir'], exist_ok=True)
for name, im in layers.items():
    if im.getbbox() is None: continue
    out = os.path.join(spec['out_dir'], f"{spec['key']}_{name}.png"); im.save(out); print('wrote', out, im.getbbox())
# 確認用: 5段階の筐体に重ねた絵
mdir = spec.get('machine_dir')
if mdir:
    prev = Image.new('RGBA', (W * 5, H), (52, 40, 30, 255))
    for i in range(1, 6):
        m = Image.open(os.path.join(mdir, f'stage{i}.png')).convert('RGBA').resize((round(MW * K), round(MH * K)), Image.LANCZOS)
        c = Image.new('RGBA', (W, H)); c.alpha_composite(layers['back']); c.alpha_composite(m, (round(OX), round(OY))); c.alpha_composite(layers['front'])
        prev.alpha_composite(c, (W * (i - 1), 0))
    prev.save(spec['preview']); print('preview', spec['preview'])
