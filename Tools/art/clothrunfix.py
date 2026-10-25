"""走りのコマ: 新しいマントだけを元のコマに合成する(2026-10-25 布表現の見直し)。
シートで描き直された体は脚の形などが元と違うことがあるので、体・鎧・剣・足元は元の画素を使い、マントだけ新しくする。

usage: clothrunfix.py <位置合わせ済みのフォルダ(clothrun.py の出力)> <元のコマのフォルダ> <出力フォルダ> [--debug <dir>]
  新しいマント Cn = 位置合わせ済みのコマの「赤い布」(色相が赤・彩度あり)の大きな塊(ふさぎ処理あり)
  元の体 B  = 元のコマの不透明な所 − 元のマント(赤みのある所。暗い赤も含む)
  結果 = Cn(下)+ B(上)。元のマントの残り(Cn の外にある赤み)は消す。ただし体の前側(ピボットより右)は消さない(前垂れ・鎧の赤い縁)
出力は元のコマと同じ大きさ・同じ名前。キャンバスからはみ出した布は切れる(はみ出し量は表示する)。
"""
import sys, os, glob, re
import numpy as np
from PIL import Image
from scipy import ndimage

src, origdir, outdir = sys.argv[1:4]
dbg = sys.argv[sys.argv.index('--debug') + 1] if '--debug' in sys.argv else None
os.makedirs(outdir, exist_ok=True)
if dbg: os.makedirs(dbg, exist_ok=True)

def reddish(a, k=1.15, vmin=25):
    r, g, b = [a[..., i].astype(float) for i in range(3)]
    return (r > vmin) & (r > g * k) & (r > b * k)

for f in sorted(glob.glob(os.path.join(origdir, 'run_*.png'))):
    name = os.path.basename(f)
    O = np.array(Image.open(f).convert('RGBA')).astype(float)
    Nf = np.array(Image.open(os.path.join(src, name.replace('.png', '_full.png'))).convert('RGBA'))
    H, W = O.shape[:2]
    px = float(re.search(r'spritePivot: \{x: ([\d.]+)', open(f + '.meta', encoding='utf-8').read()).group(1))
    pxp = px * W
    # 画像の幅を変えないため、左へはみ出した布は「体の後ろ(ピボットの 30px 左)より外」だけを横に寄せて収める
    PAD = 400
    red_full = reddish(Nf.astype(float), 1.4, 40) & (Nf[..., 3] > 100)
    xs_red = np.where(red_full.any(axis=0))[0]
    tip = (xs_red.min() - PAD) if len(xs_red) else 0
    xa = pxp - 30
    if tip < 2:
        k = (xa - tip) / (xa - 2)  # 出力の1px = 元の k px
        yy, xx = np.mgrid[0:H, 0:W].astype(float)
        sx = np.where(xx < xa, xa - (xa - xx) * k, xx) + PAD
        N = np.dstack([ndimage.map_coordinates(Nf[..., c].astype(float), [yy, sx], order=1, mode='constant', cval=0) for c in range(4)])
        squeeze = 1 / k
    else:
        N = Nf[:, PAD:PAD + W].astype(float); squeeze = 1.0
    # 新しいマント: 赤い布(新しい絵の赤は鮮やかで、影も赤みがある)
    cn = reddish(N, 1.4, 40) & (N[..., 3] > 100)
    cn = ndimage.binary_closing(cn, iterations=2)
    lab, n = ndimage.label(cn)
    if n:
        sz = ndimage.sum(cn, lab, range(1, n + 1))
        keep = [i + 1 for i, v in enumerate(sz) if v >= max(60, sz.max() * 0.05)]
        cn = np.isin(lab, keep)
    # 穴埋めはしない(マントと髪に囲まれた顔まで新しい絵になってしまうため)
    # 元のコマ: 暖かい赤み(赤 − 緑青の大きい方 > 8、緑と青が近い)= マントらしい色。鎧の茶色の縁(緑 > 青)は含めない
    oa = O[..., 3] > 30
    r, g, b = O[..., 0], O[..., 1], O[..., 2]
    warm = oa & ((r - np.maximum(g, b)) > 8) & (np.abs(g - b) < 20)
    # 体として守る所 = 不透明で赤みの無い所(黒い鎧・剣・顔・髪)。本体につながる塊だけ(古いマントの黒い切れ端は除く)
    protect = (O[..., 3] >= 200) & ~warm
    protect = ndimage.binary_opening(protect, iterations=1)
    # 頭(顔・髪)は必ず元のまま: 一番上から体の高さの 22% まで、ピボットより右。肌の色(赤>緑>青で緑と青の差が大きい)も守る
    py0 = float(re.search(r'spritePivot: \{x: [\d.]+, y: ([\d.]+)', open(f + '.meta', encoding='utf-8').read()).group(1))
    top0 = int(np.where(O[..., 3] > 30)[0].min()); foot0 = int((1 - py0) * H)
    head = np.zeros((H, W), bool); head[:top0 + int(0.22 * (foot0 - top0)), int(pxp) - 10:] = True
    skin = (r > 120) & (g > b + 15) & (r > g)
    protect |= (O[..., 3] > 30) & (head | skin)
    lab, n = ndimage.label(protect)
    if n:
        sz = ndimage.sum(protect, lab, range(1, n + 1))
        protect = np.isin(lab, [i + 1 for i, v in enumerate(sz) if v >= sz.max() * 0.02])
    # 古いマント = 赤み or 半透明 の、守る所以外。その中で「新しいマントの範囲につながる塊」で、ピボットより後ろ(左)にある所
    capeish = oa & ~protect & (warm | (O[..., 3] < 200))
    lab, n = ndimage.label(capeish)
    touch = set(np.unique(lab[ndimage.binary_dilation(cn, iterations=2) & capeish])) - {0}
    old = np.isin(lab, list(touch))
    back = np.zeros((H, W), bool); back[:, :int(pxp)] = True
    # 新しいマントの下端(列ごと)より下は触らない(剣・脚を守る)。新しいマントは腰より上だけ使う(新しい絵の体の赤を混ぜない)
    py = float(re.search(r'spritePivot: \{x: [\d.]+, y: ([\d.]+)', open(f + '.meta', encoding='utf-8').read()).group(1))
    top = int(np.where(oa)[0].min()); foot = int((1 - py) * H)
    waist = top + int(0.55 * (foot - top))
    rows = np.arange(H)[:, None]
    cn &= rows < waist
    low = np.where(cn.any(axis=0), H - 1 - np.argmax(cn[::-1], axis=0), -1)  # 列ごとの新しいマントの一番下
    above = rows <= (low[None, :] + 3)
    hip = rows < top + int(0.72 * (foot - top))  # 古いマントの裾は腰の下まで垂れていることがある
    old &= (ndimage.binary_dilation(cn, iterations=3) | (back & (above | hip)))
    # 合成: 元 → 古いマントを消す → 新しいマント(守る所の下)
    out = O.copy()
    out[old] = 0
    put = cn & ~protect
    out[put] = N[put]
    # 守る所の縁を少しなじませる(新しいマントと体の境目)
    edge = ndimage.binary_dilation(protect, iterations=1) & ~protect & cn
    out[edge] = (N[edge] * 0.5 + O[edge] * 0.5)
    Image.fromarray(out.clip(0, 255).astype(np.uint8)).save(os.path.join(outdir, name))
    # はみ出し
    fa = Nf[..., 3] > 128
    cnf = reddish(Nf.astype(float), 1.4, 40) & fa
    xs = np.where(cnf)[1] - 400
    print(name, 'cape px', int(cn.sum()), 'body px', int(protect.sum()), 'cape tip', int(tip), 'squeeze(後ろの布の横の縮め)', round(squeeze, 3))
    if dbg:
        v = np.zeros((H, W, 3), np.uint8); v[oa] = (60, 60, 60); v[protect] = (200, 200, 200); v[put] = (220, 40, 40); v[old & ~cn] = (40, 160, 255)
        Image.fromarray(v).save(os.path.join(dbg, name))
