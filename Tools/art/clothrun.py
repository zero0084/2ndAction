"""編集された走りのシートから各コマを取り出し、元のコマに位置を合わせる(2026-10-25 布表現の見直し)。
シートの人物は元の位置から少しずれて返ってくることがあるので、決まった枠では切らず、
「人物の塊を探す → 布以外(赤くない所 = 顔・髪・体・鎧・剣)の形で元のコマへ倍率と位置を合わせる」。

usage: clothrun.py <編集シート.png> <元のコマのフォルダ> <出力フォルダ> [--sheet-scale 1.2] [--order rows]
出力: <出力>/run_XX.png(元のコマと同じ大きさ。まだ布以外の画素は戻していない = aligned)、align.json、
      <出力>/run_XX_full.png(キャンバスで切らない版。布がキャンバスの外へ出た分を確かめる用)
"""
import sys, os, glob, json
import numpy as np
from PIL import Image
from scipy import ndimage
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from clothcmp import cut, body_mask, resize

def opt(a, n, d): return a[a.index(n) + 1] if n in a else d

sheet, srcdir, outdir = sys.argv[1:4]; a = sys.argv[4:]
sheet_scale = float(opt(a, '--sheet-scale', '1.2'))
os.makedirs(outdir, exist_ok=True)
S = cut(sheet)
alpha = S[..., 3] > 128
lab, n = ndimage.label(ndimage.binary_dilation(alpha, iterations=12))
sizes = ndimage.sum(alpha, lab, range(1, n + 1))
files = sorted(glob.glob(os.path.join(srcdir, 'run_*.png')))
big = sorted(range(1, n + 1), key=lambda i: -sizes[i - 1])[:len(files)]
objs = ndimage.find_objects(lab)
boxes = [(objs[i - 1], i) for i in big]
# 段(上/下)→ 左から右の順
cy = lambda b: (b[0][0].start + b[0][0].stop) / 2
rows_split = (min(cy(b) for b in boxes) + max(cy(b) for b in boxes)) / 2  # 上段と下段の真ん中(中央値だと 4人+3人 で1人ずれる)
boxes.sort(key=lambda b: (0 if cy(b) < rows_split else 1, b[0][1].start))
info = {}
for k, (f, (sl, li)) in enumerate(zip(files, boxes)):
    O = np.array(Image.open(f).convert('RGBA'))
    crop = S[sl].copy(); crop[lab[sl] != li] = 0  # 隣の人物を混ぜない
    ref = body_mask(O).astype(float)
    # 元のマントは後ろ(左)へ流れ、縁が黒っぽい = 赤くないので体と見分けにくい。ピボットより少し左から右(体の前半分)だけで合わせる
    import re
    px = float(re.search(r'spritePivot: \{x: ([\d.]+)', open(f + '.meta', encoding='utf-8').read()).group(1))
    ref[:, :max(0, int(px * O.shape[1]) - 30)] = 0
    # 脚は描き直されて形が少し変わることがあるので、マントの付く上半身(頭〜腰の上)だけで合わせる
    py = float(re.search(r'spritePivot: \{x: [\d.]+, y: ([\d.]+)', open(f + '.meta', encoding='utf-8').read()).group(1))
    top = int(np.where(O[..., 3] > 128)[0].min()); foot = int((1 - py) * O.shape[0])
    ref[top + int(0.5 * (foot - top)):] = 0
    REG = np.zeros(ref.shape, bool); REG[:top + int(0.5 * (foot - top)), max(0, int(px * O.shape[1]) - 30):] = True
    best = None
    for s in np.arange(0.64, 0.95, 0.005):
        m = body_mask(resize(crop, s)).astype(float)
        # 元のキャンバスより大きい余白を持たせて相関
        H, W = ref.shape; pad = 120
        R = np.zeros((H + 2 * pad, W + 2 * pad)); R[pad:pad + H, pad:pad + W] = ref
        C = np.zeros_like(R); h, w = min(m.shape[0], R.shape[0]), min(m.shape[1], R.shape[1]); C[:h, :w] = m[:h, :w]
        F = np.fft.rfft2(R) * np.conj(np.fft.rfft2(C)); c = np.fft.irfft2(F, s=R.shape)
        iy, ix = np.unravel_index(np.argmax(c), c.shape)
        dy = iy if iy <= R.shape[0] // 2 else iy - R.shape[0]; dx = ix if ix <= R.shape[1] // 2 else ix - R.shape[1]
        # 一致度 = 上半身の範囲(ref の領域)の中だけで数えた Dice。倍率が違っても公平に比べられる
        Cs = np.roll(np.roll(C, iy, axis=0), ix, axis=1)
        region = np.zeros_like(R); region[pad:pad + H, pad:pad + W] = 0
        rr = np.zeros_like(R); rr[pad:pad + H, pad:pad + W] = (ref_region := (np.arange(H)[:, None] >= 0))  # 全体
        reg = R > -1
        regmask = np.zeros_like(R, bool); regmask[pad:pad + H, pad:pad + W] = REG
        sc = 2 * (R * Cs)[regmask].sum() / (R[regmask].sum() + Cs[regmask].sum() + 1e-9)
        if best is None or sc > best[0]: best = (sc, s, dx - pad, dy - pad)
    sc, s, dx, dy = best
    im = Image.fromarray(resize(crop, s))
    full = Image.new('RGBA', (O.shape[1] + 400, O.shape[0]), (0, 0, 0, 0)); full.paste(im, (dx + 400, dy), im)
    can = Image.new('RGBA', (O.shape[1], O.shape[0]), (0, 0, 0, 0)); can.paste(im, (dx, dy), im)
    name = os.path.basename(f)
    can.save(os.path.join(outdir, name)); full.save(os.path.join(outdir, name.replace('.png', '_full.png')))
    fa = np.array(full)[..., 3] > 128; ys, xs = np.where(fa)
    info[name] = dict(score=round(float(sc), 3), scale=round(float(s), 3), dx=int(dx), dy=int(dy),
                      new_left=int(xs.min() - 400), canvas_w=int(O.shape[1]), orig_left=int(np.where(O[..., 3] > 128)[1].min()))
    print(name, info[name])
json.dump(info, open(os.path.join(outdir, 'align.json'), 'w'), indent=1)
