"""エステルの生成画像(マゼンタの背景)を、ゲームのコマ(同じ大きさのキャンバス・足元が下端・頭の位置がそろう)にする(2026-10-27、新キャラ)。

  python frames.py <out_dir> <name> [--anchor head|center] [--lift N,N,...] <frame1.png> <frame2.png> ...

  ・切り抜き: マゼンタらしさ(min(R,B) - G)で透明に + 縁の色かぶり(ピンク)を抜く
  ・縮尺はそのまま(エステルに同じ大きさで描いてもらう。基準絵の立ち姿の高さ = CharacterDatabaseBuilder.LukaStandPx / PentaStandPx)
  ・横の位置: --anchor head = 頭(右上の部分)の位置をそろえる(走り: 体は動いても頭が前後に揺れない)/ center = 体の中心
  ・縦: 足元をキャンバスの下端へ。--lift で、コマごとに下端から浮かせる px(空中のコマ)
  ・幅・高さは 4 の倍数(圧縮が効く)。<out_dir>/<name>_00.png, _01.png ... と、確認の一覧 <out_dir>/../<name>_sheet.jpg
"""
import os
import sys
import numpy as np
from PIL import Image


def despeck(alpha, keep=0.04):
    """体から離れた小さな塊(エステルが描き足した揺れの線・渦の印など)を消す。いちばん大きな塊の keep 倍より小さい塊が対象"""
    from scipy import ndimage
    lab, n = ndimage.label(alpha > 0.1)  # 薄い線も塊として数える(0.5 だと薄い効果線が残った)
    if n <= 1:
        return alpha
    sizes = ndimage.sum(np.ones_like(alpha), lab, range(1, n + 1))
    big = np.isin(lab, [i + 1 for i, s in enumerate(sizes) if s >= sizes.max() * keep])
    small = (lab > 0) & ~big
    # 半透明の縁まで消す(塊の周り2px)。体の側の画素は残す
    return np.where(ndimage.binary_dilation(small, iterations=2) & ~big, 0, alpha)


DESPECK = False


def body_area(im):
    """細い線(手足・触角)を削った後の太い塊(体)の面積。つぶれ/伸びの絵でも面積はほぼ変わらないので、大きさをそろえる目安にする"""
    from scipy import ndimage
    a = np.asarray(im.getchannel('A')) > 128
    return float(ndimage.binary_erosion(a, iterations=12).sum())


def cutout(path):
    a = np.asarray(Image.open(path).convert('RGB')).astype(np.float32)
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    mag = np.minimum(r, b) - g
    alpha = np.clip(1.0 - (mag - 35) / 110.0, 0, 1)
    # 色かぶり: 赤と青が緑より強い分(マゼンタの成分)を取り除く。茶色(R>G>B)・白(R=G=B)は min(R,B) <= G なので変わらない
    k = np.clip(np.minimum(r, b) - g, 0, None)
    out = a.copy()
    out[..., 0] = r - k
    out[..., 2] = b - k
    # 半透明の縁は少しだけ削る(1px 分の色の残り)
    alpha = np.where(alpha < 0.35, 0, alpha)
    if DESPECK:
        alpha = despeck(alpha)
    im = Image.fromarray(np.dstack([out, alpha * 255]).clip(0, 255).astype(np.uint8), 'RGBA')
    bb = im.getchannel('A').point(lambda v: 255 if v > 8 else 0).getbbox()
    return im.crop(bb)


def head_x(im):
    """右(進む方向)の上の方 = 頭のあたりの不透明な画素の横の中心"""
    a = np.asarray(im.getchannel('A')) > 128
    h, w = a.shape
    top = a[: int(h * 0.45), int(w * 0.5):]
    ys, xs = np.nonzero(top)
    if len(xs) == 0:
        return w * 0.75
    return float(np.median(xs)) + int(w * 0.5)


def body_x(im):
    """太い塊(体)の横の中心。細い線(伸びる腕・脚・触角)は列ごとの画素数が少ないので数えない(宇宙人: 腕が伸びても体が跳ばない)"""
    a = np.asarray(im.getchannel('A')) > 128
    cols = a.sum(axis=0).astype(np.float32)
    thick = cols >= cols.max() * 0.35
    xs = np.nonzero(thick)[0]
    if len(xs) == 0:
        return im.width / 2
    return float(np.average(xs, weights=cols[xs]))


def main_set():
    """--set <art_dir> <list.txt> [--anchor head|body] [--despeck] [--norm]: 1キャラ分の全部のコマを、同じキャンバス・同じ頭の位置で書き出す(状態が変わっても横に跳ばない)。
    list.txt の各行: <出力の相対パス>	<元の画像>[	<浮かせる px>[	keep]](keep = --norm で大きさを変えない。縮む/膨らむ演出の絵)   例: LukaRun_v1/run_00.png	C:/.../nc_luka_run_01.png"""
    art, lst = sys.argv[2], sys.argv[3]
    rows = []
    for line in open(lst, encoding='utf-8'):
        line = line.strip()
        if not line or line.startswith('#'):
            continue
        p = line.split('	')
        rows.append((p[0], p[1], int(p[2]) if len(p) > 2 and p[2] else 0, len(p) > 3 and p[3] == 'keep'))
    global DESPECK
    DESPECK = '--despeck' in sys.argv
    anchor = sys.argv[5] if len(sys.argv) > 5 and sys.argv[4] == '--anchor' else 'head'
    cuts = [cutout(r[1]) for r in rows]
    if '--norm' in sys.argv:
        # 体の大きさを1行目(走りの1枚目)にそろえる(エステルの絵は技のポーズで体が一回り小さくなることがある)。差が4%より小さい絵はそのまま。
        # 倒れる絵(Death)の「しぼむ」演出と keep の印の絵は対象外
        ref = body_area(cuts[0])
        for n, c in enumerate(cuts):
            if 'Death_' in rows[n][0] or rows[n][3]:
                continue
            k = (ref / max(1.0, body_area(c))) ** 0.5
            if abs(k - 1) > 0.04:
                cuts[n] = c.resize((round(c.width * k), round(c.height * k)), Image.LANCZOS)
                print(f'  {rows[n][0]}: x{k:.2f}')
    ax = [body_x(c) if anchor == 'body' else head_x(c) for c in cuts]
    left = max(ax)
    right = max(c.width - a for c, a in zip(cuts, ax))
    H = max(c.height + r[2] for c, r in zip(cuts, rows)) + 4
    # そろえる点(体の中心)をキャンバスの横の中央に置く(ゲームは下端中央を足元の基準にするので、体と当たり判定がずれない)
    left = right = max(left, right)
    W = int(left + right) + 8
    W, H = (W + 3) // 4 * 4, (int(H) + 3) // 4 * 4
    cleared = set()
    for (rel, _, lift, _k), c, a in zip(rows, cuts, ax):
        d = os.path.join(art, os.path.dirname(rel))
        if d not in cleared:
            os.makedirs(d, exist_ok=True)
            for f in os.listdir(d):
                if f.endswith('.png'):
                    os.remove(os.path.join(d, f))  # .meta は残す(同じ名前の絵は同じ ID のまま。使わなくなった meta は Unity が片付ける)
            cleared.add(d)
        canvas = Image.new('RGBA', (W, H), (0, 0, 0, 0))
        canvas.alpha_composite(c, (int(round(left - a)) + 4, H - c.height - lift))
        canvas.save(os.path.join(art, rel))
    print(f'{len(rows)} frames on a {W}x{H} canvas (anchor {anchor} at x={int(left) + 4}) -> {art}')


def main():
    args = sys.argv[1:]
    if args and args[0] == '--set':
        return main_set()
    out_dir, name = args[0], args[1]
    anchor, lifts, files = 'head', None, []
    i = 2
    while i < len(args):
        if args[i] == '--anchor':
            anchor = args[i + 1]; i += 2
        elif args[i] == '--lift':
            lifts = [int(v) for v in args[i + 1].split(',')]; i += 2
        else:
            files.append(args[i]); i += 1
    cuts = [cutout(f) for f in files]
    ax = [head_x(c) if anchor == 'head' else c.width / 2 for c in cuts]
    lifts = lifts or [0] * len(cuts)
    left = max(a for a in ax)
    right = max(c.width - a for c, a in zip(cuts, ax))
    top = max(c.height + l for c, l in zip(cuts, lifts))
    W = int(left + right) + 8
    H = int(top) + 4
    W = (W + 3) // 4 * 4
    H = (H + 3) // 4 * 4
    os.makedirs(out_dir, exist_ok=True)
    for f in os.listdir(out_dir):
        if f.endswith('.png'):
            os.remove(os.path.join(out_dir, f))  # 前の仮の絵(meta は Unity が片付ける)
    sheet = Image.new('RGB', (W * len(cuts) // 3 + 10, H // 3 + 10), (200, 205, 215))
    for n, (c, a, l) in enumerate(zip(cuts, ax, lifts)):
        canvas = Image.new('RGBA', (W, H), (0, 0, 0, 0))
        x = int(round(left - a)) + 4
        y = H - c.height - l
        canvas.alpha_composite(c, (x, y))
        p = os.path.join(out_dir, f'{name}_{n:02d}.png')
        canvas.save(p)
        t = canvas.resize((W // 3, H // 3), Image.LANCZOS)
        sheet.paste(t, (n * (W // 3), 5), t)
    sp = os.path.join(os.path.dirname(out_dir.rstrip('/\\')), f'{name}_sheet.jpg')
    sheet.save(sp, quality=85)
    print(f'{len(cuts)} frames {W}x{H} -> {out_dir}  sheet {sp}')


if __name__ == '__main__':
    main()
