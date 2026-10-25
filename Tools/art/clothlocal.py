"""マントの局所修正の試作(2026-10-25 布表現の見直し、黒剣士の残りのアニメ)。
元のコマ(O)と、clothframe.py で元のコマへ合わせた新しい絵(N、同じキャンバス)を合成する。
  体として残す所 P = 元で「不透明・赤みが無い」 かつ 新しい絵でも体(赤くない所)の近く(--near px 以内)
  → P は元の画素、それ以外(古いマントの所・新しいマント)は新しい絵。
走りで試した「色だけで古いマントを見分ける」方法は、古いマントの黒い縁と鎧を取り違えて体が欠けた。
新しい絵の体の位置も条件に入れて、体から離れた黒い縁(古いマント)は残さない。
※ 色と位置の判定だけでは完成としない。元画像との比較(顔・手・鎧・剣の欠け、黒い縁の残り、つなぎ目)で確かめる。
usage: clothlocal.py <元のコマ.png> <合わせた新しい絵.png> <出力.png> [--near 3] [--mode cape] [--clear 多角形] [--replace 箱] [--debug <dir>]
  --mode cape(推奨): 新しい絵は新しいマントと古いマントの所だけに使い、体は元の画素のまま
  出力の画像の大きさは元と同じ。--debug に色分け(灰=元を残した/赤=新しい絵/青=元の絵を消した所)を書く。
"""
import sys, os
import numpy as np
from PIL import Image
from scipy import ndimage
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from clothcmp import body_mask

def opt(a, n, d): return a[a.index(n) + 1] if n in a else d
op, np_, outp = sys.argv[1:4]; a = sys.argv[4:]
near = float(opt(a, '--near', '3')); dbg = opt(a, '--debug', None)
O = np.array(Image.open(op).convert('RGBA')).astype(float)
N = np.array(Image.open(np_).convert('RGBA')).astype(float)
assert O.shape == N.shape, (O.shape, N.shape)
N0 = N.copy()  # 新しい絵の全部(範囲指定の所で使う)
r, g, b, al = O[..., 0], O[..., 1], O[..., 2], O[..., 3]
warm = (r - np.maximum(g, b) > 8) & (np.abs(g - b) < 20)          # 元のマントらしい赤み(鎧の茶色の縁は含まない)
# 斬撃などのエフェクト(明るいピンク〜白の光)。古いマントの赤(暗い赤)と区別して、元の画素のまま残す
# --effects の時だけ(斬撃のある攻撃のコマ)。ピンクの光(赤が強く、緑/青も少しある)から、まわりの明るい所(白い芯)へ広げる。
# 白っぽいだけの所(古いマントの縁の白い線)は起点にしない
effO = np.zeros(al.shape, bool)
if '--effects' in a:
    pink = (al > 10) & (r > 170) & (r - np.maximum(g, b) > 50) & ((g > 40) | (b > 40))
    lp, npk = ndimage.label(pink)
    if npk:
        szp = ndimage.sum(pink, lp, range(1, npk + 1))
        pink = np.isin(lp, [i + 1 for i, v in enumerate(szp) if v >= 15])
    bright = (al > 10) & (np.maximum(np.maximum(r, g), b) > 150)
    effO = pink.copy()
    for _ in range(4): effO = ndimage.binary_dilation(effO) & bright
warm &= ~effO
cand = (al >= 200) & ~warm                                          # 元の「体かもしれない」所
nb = body_mask(N.astype(np.uint8)) > 0                              # 新しい絵の体
nb = ndimage.binary_opening(nb, iterations=1)
dist = ndimage.distance_transform_edt(~nb)
P = cand & (dist <= near)
if '--mode' in a and opt(a, '--mode', '') == 'cape':
    # 逆の考え方: 新しい絵にするのは「新しいマント」と「元の古いマントらしい所」だけ。それ以外は元のまま(体の内側に新旧のつなぎ目を作らない)
    nr, ng, nbl = N[..., 0], N[..., 1], N[..., 2]
    ncape = (nr > 40) & (nr > ng * 1.4) & (nr > nbl * 1.4) & (N[..., 3] > 100)
    if '--effects' in a: ncape &= ~((nr > 170) & ((ng > 70) | (nbl > 70)))  # 新しい絵のエフェクト(明るいピンクの光)はマントにしない
    ncape = ndimage.binary_opening(ncape, iterations=2)  # 細い赤(新しい絵の剣の赤い筋など)はマントにしない
    # 頭(元の中身の上から 20%)では、元の体(赤くない不透明)を優先する(新しい絵の襟や髪の位置のずれで顔を覆わない)
    ysA = np.where(al > 30)[0]; headBand = (np.arange(O.shape[0]) < ysA.min() + 0.20 * (ysA.max() - ysA.min()))[:, None]
    ncape &= ~(headBand & cand)
    lab2, n2 = ndimage.label(ncape)
    if n2:
        sz2 = ndimage.sum(ncape, lab2, range(1, n2 + 1))
        ncape = np.isin(lab2, [i + 1 for i, v in enumerate(sz2) if v >= sz2.max() * 0.05])  # 小さな赤(鎧の縁)はマントにしない
    distC = ndimage.distance_transform_edt(~ncape)
    dark = O[..., :3].max(axis=2) < 70
    # 古いマントの近く = 大きな赤い面(マント)の近くだけ。手袋や鎧の小さな赤い縁の近くは含めない
    wl, wn = ndimage.label(warm & (al > 30))
    if wn:
        wsz = ndimage.sum(warm & (al > 30), wl, range(1, wn + 1))
        bigWarm = np.isin(wl, [i + 1 for i, v in enumerate(wsz) if v >= max(30, wsz.max() * 0.08)])
    else:
        bigWarm = np.zeros_like(warm)
    nearWarm = ndimage.binary_dilation(bigWarm, iterations=6)
    capeLikeO = (al > 30) & (warm | (((al < 200) | dark) & (dist > near) & nearWarm))  # 体の近くの半透明(輪郭)は体として残す。暗い所は古いマントの近くだけ
    trimsInBody = warm & (distC > 4) & (dist <= 1.5) & (N[..., 3] >= 60)  # 体の中の赤い縁・前垂れは元のまま(新しい絵でも何かある所だけ)
    # 黒い体・剣に接した細い赤(剣の刃の赤い筋・鎧の赤い縁)も残す。古いマントの赤は面として広いので、ここには入らない
    thinWarm = warm & (ndimage.distance_transform_edt(~cand) <= 2.0) & (distC > 2)
    trimsInBody = trimsInBody | thinWarm
    # 腰より下(中身の高さの --waist の割合より下)は、新しいマントが描かれている所しか元の画素を消さない
    # (剣・脚・ブーツの赤い留め具を古いマントと取り違えないため。腰より下に残った古いマントは --clear で範囲指定)
    ys_ = np.where(al > 30)[0]; top_, bot_ = ys_.min(), ys_.max()
    upper = (np.arange(O.shape[0]) < top_ + float(opt(a, '--waist', '0.45')) * (bot_ - top_))[:, None]
    # 腰より下でも、体より後ろ(左)にある大きな赤い面は古いマント(立ち姿の攻撃では腿まで垂れている)。体の中心の前垂れは残す
    backCape = np.zeros_like(warm)
    if wn:
        bodyX = np.median(np.where(cand)[1]) if cand.any() else O.shape[1] / 2
        cms = ndimage.center_of_mass(warm & (al > 30), wl, range(1, wn + 1))
        keepIds = [i + 1 for i, (v, c) in enumerate(zip(wsz, cms)) if v >= max(30, wsz.max() * 0.08) and c[1] < bodyX - 0.08 * O.shape[1]]
        backCape = np.isin(wl, keepIds)
    nearBack = ndimage.binary_dilation(backCape, iterations=2)
    lowerOld = backCape | (nearBack & ((al < 200) | dark) & (dist > near))
    takeNew = ndimage.binary_dilation(ncape, iterations=1) | (capeLikeO & ~trimsInBody & upper) | (lowerOld & ~trimsInBody)
    takeNew &= ~effO
    P = (al > 30) & ~takeNew
    # 新しい絵の画素は、マントの所と古いマントの所だけで使う(体のはみ出し分を足すと、元の輪郭の白い縁が内側に取り残されて線になる)
    N = N * takeNew[..., None]
lab, n = ndimage.label(P)
if n:
    sz = ndimage.sum(P, lab, range(1, n + 1))
    small = ~np.isin(lab, [i + 1 for i, v in enumerate(sz) if v >= max(12, sz.max() * 0.004)]) & (lab > 0)
    # 小さな塊を消すのは腰より上だけ(足元の土ぼこりなどの演出は残す)
    ys2 = np.where(al > 30)[0]; up2 = (np.arange(O.shape[0]) < ys2.min() + float(opt(a, '--waist', '0.45')) * (ys2.max() - ys2.min()))[:, None]
    P = P & ~(small & up2)
P = ndimage.binary_closing(P, iterations=2) & (al > 30)
P |= effO  # エフェクトは元のまま(半透明の光も)
# 範囲指定(--replace x0,y0,x1,y1、何回でも): 範囲の中は新しい絵をそのまま使う(色と位置の判定で残った古いマントの切れ端用。剣なども新しい絵の物になる)
EB = np.zeros(P.shape, bool)
for i_, t_ in enumerate(a):
    if t_ == '--replace':
        x0, y0, x1, y1 = map(int, a[i_ + 1].split(','))
        box = np.zeros(P.shape, bool); box[y0:y1, x0:x1] = True
        EB |= box
P &= ~EB
# 範囲指定(--clear x1,y1,x2,y2,...の多角形、何回でも): 範囲の中の元の画素は使わない(古いマントの切れ端を消す。新しいマントはそのまま)
from PIL import ImageDraw as _ID
CL = np.zeros(P.shape, bool)
for i_, t_ in enumerate(a):
    if t_ == '--clear':
        v_ = list(map(float, a[i_ + 1].split(',')))
        im_ = Image.new('L', (P.shape[1], P.shape[0]), 0); _ID.Draw(im_).polygon(list(zip(v_[0::2], v_[1::2])), fill=255)
        CL |= np.array(im_) > 0
P &= ~CL
# 体の本体とつながらず、新しいマントのすぐ近く(8px)にある小さな塊は、古いマントの名残(cape モードだけ)
if '--mode' in a and opt(a, '--mode', '') == 'cape':
    solidP = P & (al > 100)
    lab3, n3 = ndimage.label(solidP, structure=np.ones((3, 3)))
    if n3 > 1:
        sz3 = ndimage.sum(solidP, lab3, range(1, n3 + 1)); main = int(np.argmax(sz3)) + 1
        nearCape = ndimage.binary_dilation(ncape, iterations=8)
        drop = [i + 1 for i, v in enumerate(sz3) if i + 1 != main and v < sz3.max() * 0.02 and (nearCape & (lab3 == i + 1)).any()]
        if drop:
            dm = np.isin(lab3, drop); dm = ndimage.binary_dilation(dm, iterations=1) & ~(lab3 == main)
            P &= ~dm
m = ndimage.gaussian_filter(P.astype(float), 0.6)
m = np.where(P, 1.0, m * (al > 30))[..., None]
out = N * (1 - m) + O * m
out[..., 3] = np.maximum(N[..., 3] * (1 - m[..., 0]), O[..., 3] * m[..., 0])
out[EB] = N0[EB]
out[CL & ~EB] = N[CL & ~EB]  # 新しいマント(無ければ透明)
Image.fromarray(out.clip(0, 255).astype(np.uint8)).save(outp)
kept = int(P.sum()); oa = int((al > 128).sum()); removed = int(((al > 128) & ~P & (N[..., 3] < 60)).sum())
print(os.path.basename(op), dict(kept_original_px=kept, original_px=oa, new_px=int((N[..., 3] > 128).sum()), old_removed_px=removed))
if dbg:
    os.makedirs(dbg, exist_ok=True)
    v = np.zeros(O.shape[:2] + (3,), np.uint8)
    v[N[..., 3] > 128] = (220, 50, 50); v[P] = (200, 200, 200); v[(al > 128) & ~P & (N[..., 3] < 60)] = (40, 150, 255)
    Image.fromarray(v).save(os.path.join(dbg, os.path.basename(op)))
