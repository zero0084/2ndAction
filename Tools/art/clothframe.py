"""走りの1コマ: 4倍に拡大して編集してもらった絵を、元のコマと同じ大きさ・位置へ戻す(2026-10-25 布表現の見直し)。
usage: clothframe.py <編集された絵.png> <元のコマ.png> <crop.json> <出力.png> [--head 0.2] [--debug <dir>]
  1. マゼンタを抜く → 2. 元のコマ(4倍)と布以外の形で倍率・位置を合わせる → 3. 1/4 に縮めて元のキャンバスへ
  4. 頭(中身の上から --head の割合、ピボットより右)だけ元の画素に戻す(コマ間で顔がぶれないように)
  元のコマのキャンバスの外へ出た布は切れるので、はみ出し量を表示する。
"""
import sys, os, json, re
import numpy as np
from PIL import Image
from scipy import ndimage
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from clothcmp import cut, fit_transform, place

def opt(a, n, d): return a[a.index(n) + 1] if n in a else d
edit, origp, cropj, outp = sys.argv[1:5]; a = sys.argv[5:]
head_frac = float(opt(a, '--head', '0.2')); dbg = opt(a, '--debug', None)
name = os.path.basename(origp)
_cj = json.load(open(cropj)); _k2 = os.path.basename(os.path.dirname(origp)) + '__' + name
ci = _cj[_k2] if _k2 in _cj else _cj[name]  # clothprep.py は「フォルダ名__名前」で書く
O = Image.open(origp).convert('RGBA')
S = ci['scale']; y0, y1 = ci['y0'], ci['y1']
ref = np.array(O.crop((0, y0, O.width, y1)).resize((O.width * S, (y1 - y0) * S), Image.LANCZOS))
E = cut(edit)
def _cands():
    from clothcmp import body_mask
    out = []
    # (1) 同じ比率で返ってきた時は、そのまま元の大きさへ縮める(エステルは入力の絵を編集しているので、これが一番正確なことが多い)
    if abs(E.shape[1] / E.shape[0] - ref.shape[1] / ref.shape[0]) < 0.01:
        out.append(('resize', ref.shape[1] / E.shape[1], 0, 0))
    # (2) 体の形の相関
    s2, x2, y2, _ = fit_transform(ref, E, np.arange(0.50, 1.40, 0.005)); out.append(('corr', s2, x2, y2))
    # (3) 頭のてっぺんと足元
    def ext(img):
        al = img[..., 3] > 128; bm = ndimage.binary_opening(body_mask(img) > 0, iterations=2)
        ys, xs = np.where(bm); t = ys.min(); b = np.where(al)[0].max()
        return t, b, xs[ys < t + 0.12 * (b - t)].mean()
    rt, rb, rhx = ext(ref); et, eb, ehx = ext(E); s3 = (rb - rt) / (eb - et)
    out.append(('height', s3, int(round(rhx - ehx * s3)), int(round(rb - eb * s3))))
    rb_ = ndimage.binary_opening(body_mask(ref) > 0, iterations=1)
    best = None
    for name_, s_, x_, y_ in out:
        pb = ndimage.binary_opening(body_mask(np.array(place(E, s_, x_, y_, (ref.shape[1], ref.shape[0])))) > 0, iterations=1)
        dice = 2 * (rb_ & pb).sum() / (rb_.sum() + pb.sum() + 1e-9)
        print(f'  fit {name_}: scale {s_:.3f} dx {x_} dy {y_} body-dice {dice:.3f}')
        if best is None or dice > best[4]: best = (name_, s_, x_, y_, dice)
    return best
if '--fit-auto' in a:
    _b = _cands(); s, dx, dy, sc = _b[1], _b[2], _b[3], _b[4]
    print(f'  -> {_b[0]}')
elif '--fit-height' in a:
    # 頭のてっぺんと足元の高さを元とそろえる(コマごとに頭が上下しないように)。横は頭(上 15%)の中心をそろえる
    from clothcmp import body_mask
    def ext(img):
        # 上端は髪(赤くない所)で測る。マントが頭より上へなびくコマがあるため。下端は足(一番下の不透明)
        al = img[..., 3] > 128; bm = body_mask(img) > 0
        bm = ndimage.binary_opening(bm, iterations=2)
        ys, xs = np.where(bm); t = ys.min(); b = np.where(al)[0].max()
        band = ys < t + 0.12 * (b - t)
        hx = xs[band].mean()
        return t, b, hx
    rt, rb, rhx = ext(ref); et, eb, ehx = ext(E)
    s = (rb - rt) / (eb - et)
    dx = int(round(rhx - ehx * s)); dy = int(round(rb - eb * s)); sc = float('nan')
else:
    s, dx, dy, sc = fit_transform(ref, E, np.arange(0.50, 1.40, 0.005))
big = np.array(place(E, s, dx, dy, (ref.shape[1], ref.shape[0])))
# はみ出し(左右上下)
full = np.array(place(E, s, dx + 400, dy + 400, (ref.shape[1] + 800, ref.shape[0] + 800)))[..., 3] > 128
ys, xs = np.where(full)
over = dict(left=max(0, 400 - xs.min()) / S, right=max(0, xs.max() - 400 - ref.shape[1] + 1) / S,
            top=max(0, 400 - ys.min()) / S, bottom=max(0, ys.max() - 400 - ref.shape[0] + 1) / S)
if '--fit-left' in a and over['left'] > 0:
    # 左へはみ出した布を、腰より上の行だけ・体の後ろ(ピボットの 30px 左)より外を横に寄せて収める
    PADL = 400
    padded = np.array(place(E, s, dx + PADL, dy, (ref.shape[1] + PADL, ref.shape[0]))).astype(float)
    al = padded[..., 3] > 128
    pxv = float(re.search(r'spritePivot: \{x: ([\d.]+)', open(origp + '.meta', encoding='utf-8').read()).group(1))
    xa = (pxv * O.width - 30) * S + PADL
    tipx = np.where(al.any(axis=0))[0].min()
    target = PADL + 2 * S
    k = (xa - tipx) / (xa - target)
    ys_, _ = np.where(al); t_, b_ = ys_.min(), ys_.max()
    waist = t_ + 0.55 * (b_ - t_)
    yy, xx = np.mgrid[0:padded.shape[0], 0:ref.shape[1]].astype(float)
    w = np.clip((waist + 30 - yy) / 60.0, 0, 1)  # 腰の上で1、腰の下で0(なめらかに)
    xxp = xx + PADL
    sx = np.where(xxp < xa, xa - (xa - xxp) * (1 + (k - 1) * w), xxp)
    big = np.dstack([ndimage.map_coordinates(padded[..., c], [yy, sx], order=1, mode='constant', cval=0) for c in range(4)]).clip(0, 255).astype(np.uint8)
    over['left_fixed_squeeze'] = round(1 / k, 3)
small = Image.fromarray(big).resize((O.width, y1 - y0), Image.LANCZOS)
# 足元を元のコマの接地位置にそろえる(--foot-snap): 一番下の不透明な行どうしの差だけ上下に動かす
if '--foot-snap' in a:
    oy = int(np.where(np.array(O)[..., 3] > 128)[0].max())
    sa = np.array(small)[..., 3] > 128
    ny = int(np.where(sa)[0].max()) + y0
    y0 += oy - ny
    foot_shift = oy - ny
else:
    foot_shift = 0
out = Image.new('RGBA', O.size, (0, 0, 0, 0)); out.paste(small, (0, y0))
out = np.array(out).astype(float); Oa = np.array(O).astype(float)
# 頭を元へ
px = float(re.search(r'spritePivot: \{x: ([\d.]+)', open(origp + '.meta', encoding='utf-8').read()).group(1))
oa = Oa[..., 3] > 30
top = int(np.where(oa)[0].min()); bot = int(np.where(oa)[0].max())
hb = np.zeros(oa.shape, bool); hb[:top + int(head_frac * (bot - top)), int(px * O.width) - 4:] = True
m = ndimage.gaussian_filter((hb & oa).astype(float), 1.0)[..., None]
m = np.where((hb & oa)[..., None], 1.0, m)
if head_frac <= 0: m = np.zeros_like(m)
res = out * (1 - m) + Oa * m
res[..., 3] = np.maximum(out[..., 3] * (1 - m[..., 0]) + Oa[..., 3] * m[..., 0], 0)
Image.fromarray(res.clip(0, 255).astype(np.uint8)).save(outp)
print(name, dict(scale=round(float(s), 3), dx=int(dx), dy=int(dy), score=round(float(sc), 3), foot_shift=foot_shift, overflow_px={k: round(v, 1) for k, v in over.items()}))
if dbg:
    os.makedirs(dbg, exist_ok=True)
    # 元(赤)と新(青緑)の重ね
    o = Oa[..., :3].mean(2) * (Oa[..., 3] / 255); n = res[..., :3].mean(2) * (res[..., 3] / 255)
    Image.fromarray(np.dstack([o * 2, n * 2, n * 2]).clip(0, 255).astype(np.uint8)).save(os.path.join(dbg, name))
