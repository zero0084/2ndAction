"""死神三姉妹の姿勢のコマ(ChatGPT の生成画像)を、ゲームの待機の絵と同じ縮尺・位置へそろえて書き出す。
usage:
  reaperpose.py measure <png>                      顔の大きさと位置を測る
  reaperpose.py place <in.png> <out.png> <idle.png> [--scale S] [--flip]
     マゼンタを抜き、顔の大きさを待機の絵にそろえ(--scale があればその倍率)、顔の横位置を待機の絵と同じ「中心からのずれ」にして、
     いちばん下の不透明な点を下端に置く(下端中央が足元のピボット)。
"""
import sys
import numpy as np
from PIL import Image
from scipy import ndimage

def key(im):
    a = np.asarray(im.convert('RGB')).astype(np.int16)
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    # マゼンタ(高い r と b、低い g)からの距離
    mag = (r > 150) & (b > 150) & (g < 110) & (np.abs(r - b) < 90)
    soft = (r > 120) & (b > 120) & (g < 140) & (np.abs(r - b) < 110) & ~mag
    alpha = np.full(r.shape, 255, np.uint8)
    alpha[mag] = 0
    alpha[soft] = 110
    rgba = np.dstack([a.astype(np.uint8), alpha])
    # 縁の色かぶり(despill): 半透明の所の赤/青を緑へ寄せる
    sp = soft
    rgba[sp, 0] = np.minimum(rgba[sp, 0], rgba[sp, 1] + 40)
    rgba[sp, 2] = np.minimum(rgba[sp, 2], rgba[sp, 1] + 40)
    # 小さな点を消す(いちばん大きい塊と、その近くの大きめの塊だけ残す)
    m = alpha > 0
    lab, n = ndimage.label(m)
    if n > 1:
        sizes = ndimage.sum(m, lab, range(1, n + 1))
        keep = np.zeros(n + 1, bool)
        big = sizes.max()
        for i, s in enumerate(sizes, 1):
            if s >= max(300, big * 0.004): keep[i] = True
        rgba[~keep[lab]] = 0
    return Image.fromarray(rgba, 'RGBA')

def face(im):
    a = np.asarray(im).astype(np.int16)
    r, g, b, al = a[..., 0], a[..., 1], a[..., 2], a[..., 3]
    skin = (al > 200) & (r > 200) & (g > 165) & (b > 140) & (r >= g) & (g >= b - 5) & (r - b < 90) & (r - g < 60)
    ys, xs = np.nonzero(al > 0)
    top, bottom = ys.min(), ys.max()
    lab, n = ndimage.label(skin)
    best = None
    for i in range(1, n + 1):
        yy, xx = np.nonzero(lab == i)
        if len(yy) < 400: continue
        cy = yy.mean()
        w = xx.max() - xx.min() + 1; h = yy.max() - yy.min() + 1
        if cy > top + (bottom - top) * 0.6 or not (0.45 < w / h < 2.0): continue  # 顔は上の方の丸い塊(脚/腕を除く)
        score = len(yy) - (cy - top) * 2.0   # 上の方の大きな肌の塊 = 顔
        if best is None or score > best[0]:
            best = (score, xx.min(), xx.max(), yy.min(), yy.max(), xx.mean(), cy, len(yy))
    if best is None: return None
    _, x0, x1, y0, y1, cx, cy, area = best
    return dict(x0=x0, x1=x1, y0=y0, y1=y1, cx=cx, cy=cy, area=area, w=x1 - x0, h=y1 - y0)

def main():
    cmd = sys.argv[1]
    if cmd == 'measure':
        im = Image.open(sys.argv[2])
        if im.mode != 'RGBA' or np.asarray(im)[..., 3].min() == 255: im = key(im)
        f = face(im); bb = im.split()[-1].getbbox()
        print(sys.argv[2], im.size, 'bbox', bb, 'face', f)
        return
    if cmd == 'place':
        src, out, idlep = sys.argv[2], sys.argv[3], sys.argv[4]
        scale = None; flip = '--flip' in sys.argv
        if '--scale' in sys.argv: scale = float(sys.argv[sys.argv.index('--scale') + 1])
        idle = Image.open(idlep).convert('RGBA')
        fi = face(idle)
        im = key(Image.open(src))
        if flip: im = im.transpose(Image.FLIP_LEFT_RIGHT)
        f = face(im)
        if scale is None: scale = (fi['area'] / f['area']) ** 0.5
        im = im.resize((round(im.width * scale), round(im.height * scale)), Image.LANCZOS)
        f = face(im)
        bb = im.split()[-1].getbbox()
        if '--clip-foot' in sys.argv:
            # 足元より下へ出た大鎌の刃を地面の線で切る(刃が地面に食い込んで見える)。足元 = 体の側(左 frac)の最も下の点
            frac = float(sys.argv[sys.argv.index('--clip-foot') + 1])
            al = np.asarray(im)[..., 3]
            x1 = int(bb[0] + (bb[2] - bb[0]) * frac)
            ys = np.nonzero(al[:, bb[0]:x1].max(axis=1) > 0)[0]
            bb = (bb[0], bb[1], bb[2], int(ys.max()) + 1)
        if '--foot-y' in sys.argv:
            # 足元の高さ(元画像のピクセル)を指定: それより下(地面へ食い込む刃)を切る
            fy = float(sys.argv[sys.argv.index('--foot-y') + 1]) * scale
            bb = (bb[0], bb[1], bb[2], int(round(fy)) + 1)
        im = im.crop(bb)
        if '--facex' in sys.argv:  # 顔の中心の横位置を手で指定(元画像のピクセル。肌の多い長女などで自動が外れる時)
            fx = float(sys.argv[sys.argv.index('--facex') + 1]) * scale
            if flip: fx = im.width + bb[0] - fx  # (反転は使っていない前提の簡易)
            f = dict(f); f['cx'] = fx
        fcx = f['cx'] - bb[0]
        # 待機の絵: 顔の中心は画像の中心から dx(ピボット = 下端中央)
        dx = fi['cx'] - idle.width / 2
        half = int(max(fcx - dx, im.width - (fcx - dx))) + 8
        W = half * 2
        can = Image.new('RGBA', (W, im.height + 4), (0, 0, 0, 0))
        can.alpha_composite(im, (int(round(W / 2 + dx - fcx)), 0))
        can.save(out)
        print(f'{out}: scale {scale:.3f} size {can.size} face {f["w"]}x{f["h"]} (idle {fi["w"]}x{fi["h"]})')

main()
