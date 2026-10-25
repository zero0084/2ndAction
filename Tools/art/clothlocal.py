"""マントの局所修正の試作(2026-10-25 布表現の見直し、黒剣士の残りのアニメ)。
元のコマ(O)と、clothframe.py で元のコマへ合わせた新しい絵(N、同じキャンバス)を合成する。
  体として残す所 P = 元で「不透明・赤みが無い」 かつ 新しい絵でも体(赤くない所)の近く(--near px 以内)
  → P は元の画素、それ以外(古いマントの所・新しいマント)は新しい絵。
走りで試した「色だけで古いマントを見分ける」方法は、古いマントの黒い縁と鎧を取り違えて体が欠けた。
新しい絵の体の位置も条件に入れて、体から離れた黒い縁(古いマント)は残さない。
※ 色と位置の判定だけでは完成としない。元画像との比較(顔・手・鎧・剣の欠け、黒い縁の残り、つなぎ目)で確かめる。
usage: clothlocal.py <元のコマ.png> <合わせた新しい絵.png> <出力.png> [--near 3] [--mode cape] [--cloth 色の範囲] [--clear 多角形] [--replace 箱] [--debug <dir>]
  --cloth h0-h1,smin,smax,vmin,vmax[;...](2026-10-25 全12人へ): 布の色(色相は度、h0>h1 なら 0 をまたぐ。彩度・明度は 0〜1)。
    無ければ黒剣士の赤いマントの判定。キャラごとの値は Docs/ClothRevision_2026-10-25/chars/<id>.txt
  --mode cape(推奨): 新しい絵は新しいマントと古いマントの所だけに使い、体は元の画素のまま
  出力の画像の大きさは元と同じ。--debug に色分け(灰=元を残した/赤=新しい絵/青=元の絵を消した所)を書く。
"""
import sys, os
import numpy as np
from PIL import Image
from scipy import ndimage
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from clothcmp import body_mask, clothmask

def opt(a, n, d): return a[a.index(n) + 1] if n in a else d
op, np_, outp = sys.argv[1:4]; a = sys.argv[4:]
near = float(opt(a, '--near', '3')); dbg = opt(a, '--debug', None)
O = np.array(Image.open(op).convert('RGBA')).astype(float)
N = np.array(Image.open(np_).convert('RGBA')).astype(float)
assert O.shape == N.shape, (O.shape, N.shape)
N0 = N.copy()  # 新しい絵の全部(範囲指定の所で使う)
r, g, b, al = O[..., 0], O[..., 1], O[..., 2], O[..., 3]
CLOTH = opt(a, '--cloth', os.environ.get('CLOTH_SPEC'))
if CLOTH: warm = clothmask(O, CLOTH)
else: warm = ((r - np.maximum(g, b) > 8) & (np.abs(g - b) < 20)) | ((r - np.maximum(g, b) > 40) & (g < r * 0.42))  # 肌(緑が強い)は含めない。元のマントらしい赤み(鎧の茶色の縁は含まない。ピンク寄りの強い赤も含める)
warm0 = warm.copy()
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
    # 飛び散る赤い破片: 赤みのある小さな塊で、一番大きな赤い面(古いマント)から 6px 以上離れている物
    wa = (al > 30) & ((r - np.maximum(g, b)) > 8)
    lw, nw_ = ndimage.label(wa)
    if nw_:
        szw = ndimage.sum(wa, lw, range(1, nw_ + 1)); big_ = int(np.argmax(szw)) + 1
        nearBig = ndimage.binary_dilation(lw == big_, iterations=6)
        shards = [i + 1 for i, v in enumerate(szw) if i + 1 != big_ and v < szw.max() * 0.03 and not (nearBig & (lw == i + 1)).any()]
        effO |= np.isin(lw, shards)
warm &= ~effO
cand = (al >= 200) & ~warm                                          # 元の「体かもしれない」所
nb = body_mask(N.astype(np.uint8)) > 0                              # 新しい絵の体
nb = ndimage.binary_opening(nb, iterations=1)
dist = ndimage.distance_transform_edt(~nb)
P = cand & (dist <= near)
if '--mode' in a and opt(a, '--mode', '') == 'cape':
    # 逆の考え方: 新しい絵にするのは「新しいマント」と「元の古いマントらしい所」だけ。それ以外は元のまま(体の内側に新旧のつなぎ目を作らない)
    nr, ng, nbl = N[..., 0], N[..., 1], N[..., 2]
    ncape = (clothmask(N, CLOTH) if CLOTH else ((nr > 40) & (nr > ng * 1.4) & (nr > nbl * 1.4))) & (N[..., 3] > 100)
    if '--effects' in a: ncape &= ~((nr > 170) & ((ng > 70) | (nbl > 70)))  # 新しい絵のエフェクト(明るいピンクの光)はマントにしない
    ncape = ndimage.binary_opening(ncape, iterations=2)  # 細い赤(新しい絵の剣の赤い筋など)はマントにしない
    # 頭(元の中身の上から 20%)では、元の体(赤くない不透明)を優先する(新しい絵の襟や髪の位置のずれで顔を覆わない)
    ysA = np.where(al > 30)[0]; headBand = (np.arange(O.shape[0]) < ysA.min() + 0.20 * (ysA.max() - ysA.min()))[:, None]
    ncape &= ~(headBand & cand)
    lab2, n2 = ndimage.label(ncape)
    if n2:
        sz2 = ndimage.sum(ncape, lab2, range(1, n2 + 1))
        ncape = np.isin(lab2, [i + 1 for i, v in enumerate(sz2) if v >= sz2.max() * 0.05])  # 小さな赤(鎧の縁)はマントにしない
    # エフェクト扱いの所でも、新しいマントから 10px 以内は古いマントの名残として扱う(上攻撃の明るい赤のマントが破片に見えるため)
    effO &= ~ndimage.binary_dilation(ncape, iterations=10)
    warm = warm0 & ~effO
    cand = (al >= 200) & ~warm
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
if '--mode' in a and opt(a, '--mode', '') == 'agree':
    # 2026-10-25(双剣士の走り): 新しい絵を基本にして、元と新しい絵の両方で「布でない不透明」の所だけ元の画素を使う。
    # 剣と布が同じ色のキャラ(双剣士の刃の青い光)では色で布を分けられず、腕や剣が二重になったり欠けたりしたため。
    # 体の芯は元の画素、輪郭のずれた所や布の所は新しい絵になる(体の描き直しは輪郭だけ)
    if '--noadd' in a:
        # 2026-10-26(拳銃士の射撃): エステルが元に無い物(銃口の光・煙・本など)を描き足すと、元が透明な所は新しい絵になるので残る。
        # 元の輪郭から --noadd px より離れた所の、布の色(と布のすぐ近く)でない新しい画素は使わない
        nc_ = clothmask(N, CLOTH) & (N[..., 3] > 100)
        drop_ = (ndimage.distance_transform_edt(al < 30) > float(opt(a, '--noadd', '8'))) & ~ndimage.binary_dilation(nc_, iterations=4)
        N = N.copy(); N[..., 3][drop_] = 0
    if '--keephsv' in a:
        # 2026-10-26(魔法使い): 杖の宝石は布と同じ青で、エステルが杖の角度を変えると宝石が二重になった。元の宝石の近く以外の新しい宝石は使わない
        kh_ = opt(a, '--keephsv', '')
        N = N.copy(); N[..., 3][clothmask(N, kh_) & ~ndimage.binary_dilation(clothmask(O, kh_) & (al > 30), iterations=3)] = 0
    obody = (al > 128) & ~clothmask(O, CLOTH)
    nbody = (N[..., 3] > 128) & ~clothmask(N, CLOTH)
    both = ndimage.binary_opening(obody & nbody, iterations=1)
    P = ndimage.binary_erosion(both, iterations=int(opt(a, '--core', '1')))
    ncape = clothmask(N, CLOTH) & (N[..., 3] > 100)
    if '--noghost' in a:
        # 2026-10-26(魔法使い): 元の布の所にエステルの絵の脚や杖(布の色でない太い塊)がずれて描かれ、体が二重になった。
        # 元が布・新しい絵も布でない・元の体から --noghost px 以上離れた太い塊は、近くの新しい布の色で埋める(細い刺繍の線は開いて除外)
        oclG = clothmask(O, CLOTH) & (al > 30)
        g_ = oclG & ~ncape & (N[..., 3] > 100) & (ndimage.distance_transform_edt(~obody) > float(opt(a, '--noghost', '4')))
        g_ = ndimage.binary_opening(g_, iterations=3)
        if g_.any() and ncape.any():
            _, (iy_, ix_) = ndimage.distance_transform_edt(~ncape, return_indices=True)
            N = N.copy(); N[g_] = N[iy_[g_], ix_[g_]]
    if '--keepbody' in a:
        # 2026-10-26(魔法使い): エステルが杖の角度や脚を少し変えると二重になる。元の「布の色でない不透明」(体・杖・髪)はすべて元の画素
        P |= ndimage.binary_erosion(obody, iterations=1)
    if '--islands' in a:
        # 2026-10-25(弓使いの決めポーズ): 新しい布の刺繍(布の色でない)と元の刺繍が重なる所で元の画素が島のように残り、布に黒い斑点が出た。
        # 体の本体から離れた小さな島(--islands px 以下)は元の画素を使わない(そこは位置を合わせた新しい絵になる)
        imax = float(opt(a, '--islands', '300')); lbI, nI = ndimage.label(P)
        if nI:
            szI = ndimage.sum(P, lbI, range(1, nI + 1))
            P &= np.isin(lbI, [k + 1 for k, v in enumerate(szI) if v > imax])
    if '--keepgone' in a:
        # 2026-10-26(拳銃士の射撃): エステルが腕や銃の位置を変えると、元の体がある所で新しい絵が透明になり、体が欠けた。
        # 元の体(布の色でない不透明、本体につながる塊)のうち新しい絵が透明な所は元の画素を使う。古い布の近く(3px)は除く
        lbG, nG = ndimage.label(obody)
        if nG:
            szG = ndimage.sum(obody, lbG, range(1, nG + 1)); mainG = np.isin(lbG, [k + 1 for k, v in enumerate(szG) if v >= 0.05 * szG.max()])
            gone_ = mainG & (N[..., 3] < 30) & ~ndimage.binary_dilation(clothmask(O, CLOTH) & (al > 30), iterations=3)
            P |= gone_
if '--mode' in a and opt(a, '--mode', '') == 'patch':
    # 2026-10-26(吸血鬼): マントの表地が服と同じ黒で、色で消せず、エステルの絵は体が少しずれる。体は元のまま一切使わず、
    #  ① 元の不透明のうち新しい絵の外形(少し広げた)の外 = 破れた房・ほつれ → 消す
    #  ② 元が透明で新しい絵が布(赤)かその近くの所 = 穴・欠けた縁 → 新しい絵で埋める
    #  ③ 元も新しい絵も赤い布の所 = マントの裏地 → 新しい絵(点々の傷みを消す)
    nsil = ndimage.binary_dilation(N[..., 3] > 100, iterations=int(opt(a, '--grow', '3')))
    ncl = clothmask(N, CLOTH) & (N[..., 3] > 100)
    nearCl = ndimage.binary_dilation(ncl, iterations=int(opt(a, '--near_cl', '8'))) & (N[..., 3] > 100)
    ocl = clothmask(O, CLOTH) & (al > 30)
    keepO = (al > 30) & nsil
    fillN = (al <= 30) & nearCl
    swapN = ndimage.binary_erosion(ocl, iterations=1) & ncl
    P = keepO & ~swapN
    ncape = fillN | swapN
    N = N * ncape[..., None]
if '--mode' in a and opt(a, '--mode', '') == 'erase':
    # 2026-10-25(忍者): 古い布を消した絵をエステルに渡す方式の合成。新しい絵の体は使わない(ポーズや剣の位置が変わっても元のまま)。
    #  ① 元の古い布 = 布の色の大きく太い塊(clotherase.py と同じ考え。細い房飾りなどは残る)を消す
    #  ② 体の本体から離れて浮いた元の切れ端も消す
    #  ③ 新しい絵の布(布の色の大きな塊)を、消した所と透明な所にだけ置く(体の上には重ねない)
    minsz = float(opt(a, '--minsz', '150')); thick = float(opt(a, '--thick', '3.5'))
    oc_all = clothmask(O, CLOTH) & (al > 30)
    # 布の色の塊ごとに、大きく太い物(=古い布)をまるごと消す。房飾りが古い布に触れていると一緒に消えるので、③で新しい絵の房飾りを戻す
    lb_, nb_ = ndimage.label(oc_all); old = np.zeros(al.shape, bool)
    if nb_:
        sz_ = ndimage.sum(oc_all, lb_, range(1, nb_ + 1)); dt_ = ndimage.distance_transform_edt(oc_all)
        mx_ = ndimage.maximum(dt_, lb_, range(1, nb_ + 1))
        ids_ = [i + 1 for i, (v, t) in enumerate(zip(sz_, mx_)) if v >= minsz and t > thick]
        ef_ = float(opt(a, '--edgefrac', '0'))
        if ef_ > 0:  # 竜騎士など、布と同じ色の鎧(胸の鱗)がある時: 輪郭のうち背景(透明)に触れる割合が ef_ 以上の塊だけを古い布とする
            bgn_ = ndimage.binary_dilation(al <= 30, iterations=2); keep_ids = []
            for i in ids_:
                cm_ = ndimage.binary_fill_holes(lb_ == i); edge_ = cm_ & ~ndimage.binary_erosion(cm_)
                if (edge_ & bgn_).sum() >= ef_ * max(1, edge_.sum()): keep_ids.append(i)
            ids_ = keep_ids
        old = np.isin(lb_, ids_)
    belowR = np.ones(al.shape, bool)
    if '--below' in a:
        # 2026-10-26(拳銃士): コート・髪・肌が同じ茶色。直すのは腰から下の裾だけなので、古い布として消すのも新しい布を置くのも、
        # 外接の箱の上から --below の割合(ベルトの辺り)より下だけにする。上半身は元のまま
        ysB = np.where((al > 30).any(axis=1))[0]
        if len(ysB): belowR[: int(ysB[0] + float(opt(a, '--below', '0.45')) * (ysB[-1] - ysB[0]))] = False
        old &= belowR
    old = ndimage.binary_dilation(old, iterations=2) & (al > 0) & belowR
    base = (al > 30) & ~old
    lb2, nb2 = ndimage.label(base, structure=np.ones((3, 3)))
    if nb2 > 1:
        sz2_ = ndimage.sum(base, lb2, range(1, nb2 + 1)); main_ = int(np.argmax(sz2_)) + 1
        base = np.isin(lb2, [i + 1 for i, v in enumerate(sz2_) if i + 1 == main_ or v >= sz2_.max() * float(opt(a, '--keepfrac', '0.08'))])
    nc_all = clothmask(N, CLOTH) & (N[..., 3] > 100)
    lb3, nb3 = ndimage.label(nc_all)
    if nb3:
        sz3_ = ndimage.sum(nc_all, lb3, range(1, nb3 + 1)); nc_all = np.isin(lb3, [i + 1 for i, v in enumerate(sz3_) if v >= float(opt(a, '--newmin', '25'))])
    ncape = nc_all & belowR
    P = base
    takeNew = ncape & ~ndimage.binary_erosion(base, iterations=1)
    N = N * takeNew[..., None]
if '--mode' in a and opt(a, '--mode', '') == 'color':
    # 布の色が体(鎧・肌・髪)とはっきり違うキャラ用(双剣士の青緑など、2026-10-25): 腰の上下の規則を使わず、色だけで決める。
    # 新しい絵にする所 = 元の布の色の所(大きな塊 + その縁の半透明・暗い縁) + 新しい絵の布の色の所。それ以外は元の画素
    minsz = float(opt(a, '--minsz', '40'))
    def bigparts(m):
        lb, nb_ = ndimage.label(m)
        if not nb_: return m
        sz_ = ndimage.sum(m, lb, range(1, nb_ + 1)); return np.isin(lb, [i + 1 for i, v in enumerate(sz_) if v >= minsz])
    oc = bigparts(clothmask(O, CLOTH) & (al > 30)) & ~effO
    nc = bigparts(clothmask(N, CLOTH) & (N[..., 3] > 100))
    darkEdge = ((al < 200) | (O[..., :3].max(axis=2) < 60)) & ndimage.binary_dilation(oc, iterations=int(opt(a, '--edge', '3')))
    takeNew = ndimage.binary_dilation(oc, iterations=1) | darkEdge | ndimage.binary_dilation(nc, iterations=1)
    keepBody = ndimage.binary_erosion((al > 200) & ~clothmask(O, CLOTH), iterations=int(opt(a, '--core', '4')))  # 体の芯は必ず元のまま
    takeNew &= ~keepBody & ~effO
    # 体の輪郭の内側(腰の短いスカートなど、体に重なる布)は元のまま。新しい絵は、元も新しい絵も布の所だけ使う
    # (新しい絵の体の位置が少しずれていると、内側の布を外した所に別の体が見えるため)
    interior = ndimage.binary_closing(ndimage.binary_opening((al > 128) & ~clothmask(O, CLOTH), iterations=1), iterations=int(opt(a, '--interior', '5')))
    takeNew &= ~interior | (nc & oc)
    # 新しい絵の「布でない画素」(体・剣)は、元の体のすぐ近くだけで使う。離れた所は透明に
    # (新しい絵では腕や剣が少しずれて描かれることがあり、古い布の所でそれを拾うと腕が二重に見えるため)
    obody = (al > 128) & ~clothmask(O, CLOTH)
    nearBody = ndimage.distance_transform_edt(~obody) <= float(opt(a, '--bodynear', '3'))
    strayNew = takeNew & ~nc & (N[..., 3] > 30) & ~nearBody
    N[strayNew] = 0
    ncape = nc
    P = (al > 30) & ~takeNew
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
    if t_ in ('--clear', '--clearred'):
        v_ = list(map(float, a[i_ + 1].split(',')))
        im_ = Image.new('L', (P.shape[1], P.shape[0]), 0); _ID.Draw(im_).polygon(list(zip(v_[0::2], v_[1::2])), fill=255)
        poly_ = np.array(im_) > 0
        if t_ == '--clearred': poly_ &= (r - np.maximum(g, b) > 20) | ((al > 0) & (al < 160) & (r > np.maximum(g, b)))  # 範囲の中の赤い所だけ(白い光の筋は残す)
        CL |= poly_
P &= ~CL
# 範囲指定(--keep 多角形、何回でも): 範囲の中は元の絵をそのまま使う(透明なすき間も元のまま。赤い柄の巻きなど、色でマントと見なされた体の一部)
KP = np.zeros(P.shape, bool)
for i_, t_ in enumerate(a):
    if t_ == '--keep':
        v_ = list(map(float, a[i_ + 1].split(',')))
        im_ = Image.new('L', (P.shape[1], P.shape[0]), 0); _ID.Draw(im_).polygon(list(zip(v_[0::2], v_[1::2])), fill=255)
        KP |= np.array(im_) > 0
if '--keeptop' in a:
    # 2026-10-26(拳銃士): コート・髪・肌・ベルトがどれも茶色で、色では布と体を分けられず、顔や手が欠けたり銃が二重になった。
    # 直すのはコートの裾だけなので、元の絵の上から --keeptop の割合(外接の箱の高さ)までは元の絵をそのまま使う(透明な所も元のまま)
    ys_ = np.where((al > 30).any(axis=1))[0]
    if len(ys_): KP[: int(ys_[0] + float(opt(a, '--keeptop', '0.45')) * (ys_[-1] - ys_[0]))] = True
if '--keephead' in a:
    # 2026-10-26(吸血鬼): 新しい絵の顔が少しずれて二重になる。頭(上から --keephead の割合の高さ、頭の横幅+余白)だけは元の絵のまま。
    # 上の行を丸ごと守る --keeptop と違い、頭の横に広がる肩のマントは描き直しを使える
    ys_ = np.where((al > 30).any(axis=1))[0]
    if len(ys_):
        h_ = ys_[-1] - ys_[0]; y1_ = int(ys_[0] + float(opt(a, '--keephead', '0.2')) * h_)
        hb_ = (al[ys_[0]: y1_] > 30) & clothmask(O[ys_[0]: y1_], opt(a, '--headspec', '0-360,0,0.3,0.55,1'))  # 頭の横幅は明るい髪・肌で測る(黒いマントの先を含めない)
        lbh_, nh_ = ndimage.label(ndimage.binary_closing(hb_, iterations=2))
        if nh_: hb_ = lbh_ == (int(np.argmax(ndimage.sum(hb_, lbh_, range(1, nh_ + 1)))) + 1)  # 一番大きな明るい塊(髪)。マントの銀の飾りは含めない
        xs_ = np.where(hb_.any(axis=0))[0]
        if len(xs_): mg_ = int(0.04 * O.shape[1]); KP[: y1_, max(0, xs_[0] - mg_): xs_[-1] + mg_] = True
if '--band' in a:
    # 2026-10-26(竜人): 翼・鱗・尾も布と同じ赤。直すのは腰の布だけなので、外接の箱の高さの割合 a〜b の帯の外は元の絵のまま
    ys_ = np.where((al > 30).any(axis=1))[0]
    if len(ys_):
        b0_, b1_ = [float(v) for v in opt(a, '--band', '0.35,0.8').split(',')]; h_ = ys_[-1] - ys_[0]
        KP[: int(ys_[0] + b0_ * h_)] = True; KP[int(ys_[0] + b1_ * h_):] = True
if '--keephsv' in a:
    # 2026-10-26(魔法使いの杖の宝石・魔法の光): 布と同じ青紫で色では布と分けられず、エステルの絵の宝石の色がコマごとに変わった。
    # 元の絵で明るく鮮やかな所(この色の範囲)は、少し広げて元の画素を使う
    KP |= ndimage.binary_dilation(clothmask(O, opt(a, '--keephsv', '')) & (al > 30), iterations=3) & (al > 0)
P |= KP & (al > 30)
# 体の本体とつながらず、新しいマントのすぐ近く(8px)にある小さな塊は、古いマントの名残(cape モードだけ)
if '--mode' in a and opt(a, '--mode', '') in ('cape', 'color', 'erase'):
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
# 合成でできた小さな穴(まわりを絵に囲まれた透明な所、400px 未満)を、まわりの色で埋める(cape モード)。元の絵にあった穴は埋めない
if '--mode' in a and opt(a, '--mode', '') in ('cape', 'color', 'erase'):
    solid = out[..., 3] > 128
    holes = ndimage.binary_fill_holes(solid) & ~solid & ((al > 128) | (N0[..., 3] > 128))  # 元か新しい絵のどちらかで埋まっていた所だけ(元の絵の透明なすき間は埋めない)
    lh, nh = ndimage.label(holes)
    if nh:
        szh = ndimage.sum(holes, lh, range(1, nh + 1))
        holes = np.isin(lh, [i + 1 for i, v in enumerate(szh) if v < 400])
        wgt = ndimage.gaussian_filter(solid.astype(float), 3)
        for c in range(3):
            col = ndimage.gaussian_filter(out[..., c] * solid, 3) / np.maximum(wgt, 1e-3)
            out[..., c] = np.where(holes, col, out[..., c])
        out[..., 3] = np.where(holes, 255, out[..., 3])
out[KP] = O[KP]
# --smooth x0,y0,x1,y1: 範囲の中を周りの色でなじませる(新しい絵に残った四角い段差・小さな透明の点用。人物の画素には使わない)
for i_, t_ in enumerate(a):
    if t_ == '--smooth':
        x0_, y0_, x1_, y1_ = map(int, a[i_ + 1].split(','))
        so_ = (out[..., 3] > 128).astype(float)
        w_ = ndimage.gaussian_filter(so_, 2.5)
        sm_ = np.dstack([ndimage.gaussian_filter(out[..., c] * so_, 2.5) / np.maximum(w_, 1e-3) for c in range(3)])
        bx_ = np.zeros(so_.shape, bool); bx_[y0_:y1_, x0_:x1_] = True
        fe_ = ndimage.gaussian_filter(bx_.astype(float), 1.5)[..., None]  # 範囲の縁はなめらかに
        out[..., :3] = np.where((w_ > 0.5)[..., None], out[..., :3] * (1 - fe_) + sm_ * fe_, out[..., :3])
        out[..., 3] = np.where(bx_ & (w_ > 0.5), 255, out[..., 3])
# --erase 多角形: 範囲の中を透明にする(穴埋めの後に。合成の名残で、元にも新しい絵にも要らない所)
for i_, t_ in enumerate(a):
    if t_ == '--erase':
        v_ = list(map(float, a[i_ + 1].split(',')))
        im_ = Image.new('L', (P.shape[1], P.shape[0]), 0); _ID.Draw(im_).polygon(list(zip(v_[0::2], v_[1::2])), fill=255)
        out[np.array(im_) > 0] = 0
# --despeck N: 合成の結果ちぎれて浮いた小さな粒(N px 未満)を消す。元の絵か新しい絵では本体とつながっていた所だけ(元から離れていた火花などは残す)
if '--despeck' in a:
    lim = float(opt(a, '--despeck', '300'))
    def _main(alpha):
        lb, nb = ndimage.label(alpha > 20, structure=np.ones((3, 3)))
        return lb == (int(np.argmax(ndimage.sum(alpha > 20, lb, range(1, nb + 1)))) + 1) if nb else alpha > 255
    attached = _main(al) | _main(N0[..., 3])
    so = out[..., 3] > 20
    lab4, n4 = ndimage.label(so, structure=np.ones((3, 3)))
    if n4 > 1:
        sz4 = ndimage.sum(so, lab4, range(1, n4 + 1)); main4 = int(np.argmax(sz4)) + 1
        drop4 = [i + 1 for i, v in enumerate(sz4) if i + 1 != main4 and v < lim and attached[lab4 == i + 1].mean() > 0.5]
        if drop4: out[np.isin(lab4, drop4)] = 0
        print('  despeck', len(drop4), 'of', n4 - 1)
Image.fromarray(out.clip(0, 255).astype(np.uint8)).save(outp)
kept = int(P.sum()); oa = int((al > 128).sum()); removed = int(((al > 128) & ~P & (N[..., 3] < 60)).sum())
print(os.path.basename(op), dict(kept_original_px=kept, original_px=oa, new_px=int((N[..., 3] > 128).sum()), old_removed_px=removed))
if dbg:
    os.makedirs(dbg, exist_ok=True)
    v = np.zeros(O.shape[:2] + (3,), np.uint8)
    v[N[..., 3] > 128] = (220, 50, 50); v[P] = (200, 200, 200); v[(al > 128) & ~P & (N[..., 3] < 60)] = (40, 150, 255)
    Image.fromarray(v).save(os.path.join(dbg, os.path.basename(op)))
