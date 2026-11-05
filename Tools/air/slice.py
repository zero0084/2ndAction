"""空中雑魚のシート(エステルの生成画像: 横一列にポーズ、単色の背景)を、ゲームのコマへ切り分ける(2026-11-05)。

  python slice.py <sheet.png> <out_dir> [--bg magenta|green] [--names a,b,c...] [--max 256] [--preview <jpg>]

  ・切り抜き: 背景らしさ(マゼンタ: min(R,B)-G / 緑: G-max(R,B))で透明に + 縁の色かぶりを抜く(Tools/newchar/frames.py と同じ考え方)
  ・ポーズの分け方: 不透明な画素の「列ごとの数」で横の塊に分け、大きい塊 N 個(N = 名前の数)をポーズにする。
    小さい塊(散る羽根・光の粒)はいちばん近いポーズへ足す
  ・位置合わせ: 細い所(翼・尾・脚)を削った太い塊(体)の重心を、全コマ共通の正方形キャンバスの中心へ置く。
    Unity ではピボットを中心(0.5, 0.5)にするので、羽ばたいても体が跳ねない
  ・キャンバスの一辺は 4 の倍数(圧縮が効く)。--max より大きいときは全コマ同じ倍率で縮める
  ・既定の名前: idle, fly_02, fly_03, telegraph, attack, recover, hit, death(シートの順番: 飛行1/2/3・予兆・攻撃・戻り・被弾・撃破)
    idle は飛行1 と同じ絵(move/ に fly_01 としても出す)。move/fly_04 = fly_02(羽ばたき 1-2-3-2)
"""
import os
import sys
import numpy as np
from PIL import Image

DEFAULT_NAMES = ['idle', 'fly_02', 'fly_03', 'telegraph', 'attack', 'recover', 'hit', 'death']


def cutout(rgb, bg, keepcolor=False):
    a = rgb.astype(np.float32)
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    if bg == 'green':
        m = g - np.maximum(r, b)
    else:
        m = np.minimum(r, b) - g
    alpha = np.clip(1.0 - (m - 35) / 110.0, 0, 1)
    k = np.clip(m, 0, None)
    inner = np.zeros(m.shape, bool)
    if keepcolor:
        # 紫の体など(背景の色に近い): 画像の外側から背景らしい画素をたどって届く所だけを背景/縁とみなす。
        #  輪郭に囲まれた内側の「背景らしい色」は体の色 → 不透明・色はそのまま
        cand = m > 35
        reach = np.zeros(m.shape, bool)
        reach[0, :] = cand[0, :]; reach[-1, :] = cand[-1, :]; reach[:, 0] = cand[:, 0]; reach[:, -1] = cand[:, -1]
        while True:
            n = reach.copy()
            n[1:, :] |= reach[:-1, :]; n[:-1, :] |= reach[1:, :]; n[:, 1:] |= reach[:, :-1]; n[:, :-1] |= reach[:, 1:]
            n &= cand
            if (n == reach).all():
                break
            reach = n
        inner = cand & ~reach
        alpha = np.where(inner, 1.0, alpha)
        k = np.where(inner, 0, k)
    out = a.copy()
    if bg == 'green':
        out[..., 1] = g - k
    else:
        out[..., 0] = r - k
        out[..., 2] = b - k
    alpha = np.where(alpha < 0.35, 0, alpha)
    # 半透明の所(砂ぼこり・羽の縁): 背景の色を混ぜた分を取り除く(元の色 = (見えている色 - (1-a)×背景) / a)。
    #  a は「元の色の背景らしさ ≈ -50(砂色・茶色)」と仮定して、背景らしさ m から求める(不透明な所 = 赤い羽の先なども は今のまま)
    semi = (alpha > 0) & (alpha < 0.95) & (m > 35) & ~inner
    if semi.any():
        a2 = np.clip((255.0 - m) / 305.0, 0.05, 1.0)
        bgc = np.array([0, 255, 0] if bg == 'green' else [255, 0, 255], np.float32)
        unmix = (a - (1 - a2[..., None]) * bgc) / a2[..., None]
        out[semi] = np.clip(unmix[semi], 0, 255)
        alpha = np.where(semi, np.minimum(alpha, a2 * 1.15), alpha)
        alpha = np.where(alpha < 0.12, 0, alpha)
    return np.dstack([out, alpha * 255]).clip(0, 255).astype(np.uint8)


def erode(mask, it):
    m = mask.copy()
    for _ in range(it):
        n = m.copy()
        n[1:, :] &= m[:-1, :]
        n[:-1, :] &= m[1:, :]
        n[:, 1:] &= m[:, :-1]
        n[:, :-1] &= m[:, 1:]
        m = n
    return m


def segments(alpha, n_poses, gap=6):
    """シートは横に n 個の同じ幅の枠。枠の境目(k × 幅 / n)の前後 ±30% で、不透明な画素がいちばん少ない列で切る
    (ポーズどうしが触れていても切れる。散った羽根などは列の位置でその枠に入る)"""
    col = (alpha > 60).sum(axis=0).astype(np.float64)
    W = len(col)
    cw = W / n_poses
    cuts = [0]
    for k in range(1, n_poses):
        c = k * cw
        lo, hi = int(c - cw * 0.3), int(c + cw * 0.3)
        sm = np.convolve(col, np.ones(5) / 5, mode='same')
        cuts.append(lo + int(np.argmin(sm[lo:hi])))
    cuts.append(W)
    groups = []
    for k in range(n_poses):
        seg = col[cuts[k]:cuts[k + 1]]
        nz = np.nonzero(seg)[0]
        if len(nz) == 0:
            raise SystemExit(f'枠 {k + 1} が空')
        groups.append([cuts[k] + int(nz[0]), cuts[k] + int(nz[-1]) + 1])
    return groups


def segments_by_gap(alpha, n_poses, gap=6):
    col = (alpha > 60).sum(axis=0)
    on = col > 0
    runs = []
    x = 0
    W = len(col)
    while x < W:
        if on[x]:
            s = x
            last = x
            while x < W and (on[x] or x - last <= gap):
                if on[x]:
                    last = x
                x += 1
            runs.append([s, last + 1, int(col[s:last + 1].sum())])
        else:
            x += 1
    # 大きい塊 = いちばん大きい塊の 2 割以上。ポーズどうしが触れて 1 つになっている時は、いちばん幅の広い塊を
    #  真ん中あたりの「いちばん細い列」で 2 つに分ける(数がそろうまで)
    mx = max(r[2] for r in runs)
    big = [r for r in runs if r[2] >= mx * 0.2]
    small = [r for r in runs if r[2] < mx * 0.2]
    while len(big) < n_poses:
        w = max(big, key=lambda r: r[1] - r[0])
        s0, s1 = w[0], w[1]
        lo, hi = s0 + int((s1 - s0) * 0.2), s1 - int((s1 - s0) * 0.2)
        if hi - lo < 4:
            break
        cut = lo + int(np.argmin(col[lo:hi]))
        big.remove(w)
        big += [[s0, cut, int(col[s0:cut].sum())], [cut, s1, int(col[cut:s1].sum())]]
    big.sort(key=lambda r: -r[2])
    small += big[n_poses:]
    big = sorted(big[:n_poses], key=lambda r: r[0])
    groups = [[r[0], r[1]] for r in big]
    for s in small:
        c = (s[0] + s[1]) / 2
        i = min(range(len(groups)), key=lambda j: max(0, groups[j][0] - c, c - groups[j][1]))
        groups[i][0] = min(groups[i][0], s[0])
        groups[i][1] = max(groups[i][1], s[1])
    if len(big) < n_poses:
        raise SystemExit(f'ポーズが {len(big)} 個しか見つからない(期待 {n_poses})')
    return groups


def head_point(sub, cx, cy):
    """左向きの頭: 体の中心の高さ ±25% の帯で、いちばん左の不透明な所(くちばしの先)から少し右"""
    m = sub[..., 3] > 128
    h = m.shape[0]
    y0, y1 = max(0, int(cy - h * 0.25)), min(h, int(cy + h * 0.25))
    band = m[y0:y1]
    cols = np.nonzero(band.any(axis=0))[0]
    if len(cols) == 0:
        return cx, cy
    x = cols[0]
    ys = np.nonzero(band[:, x:x + 6].any(axis=1))[0]
    return float(x + 0.06 * m.shape[1]), float(y0 + ys.mean())


def gray(sub):
    a = sub[..., 3:4].astype(np.float32) / 255.0
    return (sub[..., :3].astype(np.float32).mean(axis=2, keepdims=True) * a + 80 * (1 - a))[..., 0]


def head_align(frames):
    """各コマの頭を、待機の絵(1枚目)の頭の模様に合わせる(羽ばたきで翼の位置が変わっても頭がぶれない)。
    合わない(向きが大きく違う: 急降下・撃破など)コマは体の中心のまま"""
    P = 64  # 端の頭でも模様を切り出せるよう、透明の余白を足す
    frames = [(np.pad(sb, ((P, P), (P, P), (0, 0))), x + P, y + P) for sb, x, y in frames]
    sub0, cx0, cy0 = frames[0]
    hx, hy = head_point(sub0, cx0, cy0)
    r = max(10, int(min(sub0.shape[:2]) * 0.11))
    g0 = gray(sub0)
    y0, x0 = int(hy) - r, int(hx) - r
    if y0 < 0 or x0 < 0 or y0 + 2 * r > g0.shape[0] or x0 + 2 * r > g0.shape[1]:
        return frames
    tpl = g0[y0:y0 + 2 * r, x0:x0 + 2 * r]
    off = (hx - cx0, hy - cy0)  # 頭から体の中心まで
    out = [frames[0]]
    for sub, cx, cy in frames[1:]:
        g = gray(sub)
        px, py = head_point(sub, cx, cy)  # この絵の頭の見当(ここの周りを探す)
        best, bx, by = None, px, py
        rng = int(r * 1.6)
        for dy in range(-rng, rng + 1, 2):
            for dx in range(-rng, rng + 1, 2):
                yy, xx = int(py) - r + dy, int(px) - r + dx
                if yy < 0 or xx < 0 or yy + 2 * r > g.shape[0] or xx + 2 * r > g.shape[1]:
                    continue
                d = float(((g[yy:yy + 2 * r, xx:xx + 2 * r] - tpl) ** 2).mean())
                if best is None or d < best:
                    best, bx, by = d, xx + r, yy + r
        if best is not None and best < 900:  # 似ている(灰色の差 30 以内)= 頭の向きがほぼ同じ
            out.append((sub, bx - off[0], by - off[1]))
        else:
            out.append((sub, cx, cy))
    return out


def main():
    args = sys.argv[1:]
    src, out = args[0], args[1]
    bg, names, mx, preview, align = 'magenta', DEFAULT_NAMES, 256, None, 'core'
    depink = cool = keepcolor = False
    i = 2
    while i < len(args):
        if args[i] == '--bg': bg = args[i + 1]; i += 2
        elif args[i] == '--names': names = args[i + 1].split(','); i += 2
        elif args[i] == '--max': mx = int(args[i + 1]); i += 2
        elif args[i] == '--preview': preview = args[i + 1]; i += 2
        elif args[i] == '--align': align = args[i + 1]; i += 2
        elif args[i] == '--depink': depink = True; i += 1
        elif args[i] == '--cool': cool = True; i += 1
        elif args[i] == '--keepcolor': keepcolor = True; i += 1
        else: raise SystemExit('unknown ' + args[i])
    rgb = np.asarray(Image.open(src).convert('RGB'))
    rgba = cutout(rgb, bg, keepcolor)
    if cool:  # 青い敵: 半透明の縁に残る紫(赤の混ざり)を抜く(赤 <= 緑)
        f = rgba[..., :3].astype(np.float32)
        semi = (rgba[..., 3] > 0) & (f[..., 2] > f[..., 1] - 10)
        f[..., 0][semi] = np.minimum(f[..., 0][semi], f[..., 1][semi] * 0.85)
        rgba[..., :3] = f.astype(np.uint8)
    if depink:  # 背景のマゼンタが移ったピンクの砂ぼこりを砂色へ(Tools/art/depink.py と同じ考え方)
        f = rgba[..., :3].astype(np.float32); r_, g_, b_ = f[..., 0], f[..., 1], f[..., 2]
        pink = (rgba[..., 3] > 0) & (r_ > g_ + 20) & (b_ > g_ - 15)  # 茶色・砂色は 青 << 緑(差 -40 以下)。ピンク・サーモンは 青 ≈ 緑
        L = 0.3 * r_ + 0.59 * g_ + 0.11 * b_
        for c, k in ((0, 1.08), (1, 0.93), (2, 0.74)):
            f[..., c][pink] = np.clip(L[pink] * k, 0, 255)
        rgba[..., :3] = f.astype(np.uint8)
    alpha = rgba[..., 3]
    groups = segments(alpha, len(names))
    frames = []
    for (x0, x1) in groups:
        sub = rgba[:, x0:x1].copy()
        # この列の範囲にある他のポーズの画素は無い前提(横一列)
        ys, xs = np.nonzero(sub[..., 3] > 8)
        y0, y1 = ys.min(), ys.max() + 1
        sub = sub[y0:y1, xs.min():xs.max() + 1]
        m = sub[..., 3] > 128
        # 厚み(何回削ると消えるか)の 3 乗で重みを付けた重心 = 胴体。翼・尾・脚のような薄い所はほとんど効かない
        depth = np.zeros(m.shape, np.float32)
        cur = m.copy()
        for _ in range(60):
            if not cur.any():
                break
            depth += cur
            cur = erode(cur, 1)
        w = depth ** 3
        ys, xs = np.mgrid[0:m.shape[0], 0:m.shape[1]]
        cy, cx = float((w * ys).sum() / w.sum()), float((w * xs).sum() / w.sum())
        frames.append((sub, cx, cy))
    if align == 'head':
        frames = head_align(frames)
    # 共通の正方形キャンバス: 体の重心から各辺までの最大距離
    half = 0
    for sub, cx, cy in frames:
        h, w = sub.shape[:2]
        half = max(half, cx, w - cx, cy, h - cy)
    side = int(np.ceil(half * 2 + 8))
    scale = min(1.0, mx / side)
    side_out = int(np.ceil(side * scale / 4.0) * 4)
    os.makedirs(os.path.join(out, 'move'), exist_ok=True)
    outs = []
    for (sub, cx, cy), name in zip(frames, names):
        can = Image.new('RGBA', (side, side), (0, 0, 0, 0))
        can.paste(Image.fromarray(sub, 'RGBA'), (int(round(side / 2 - cx)), int(round(side / 2 - cy))))
        if side_out != side:
            can = can.resize((side_out, side_out), Image.LANCZOS)
        p = os.path.join(out, (name + '.png') if not name.startswith('fly_') else os.path.join('move', name + '.png'))
        can.save(p)
        outs.append(can)
        if name == 'idle':
            can.save(os.path.join(out, 'move', 'fly_01.png'))
        if name == 'fly_02':
            can.save(os.path.join(out, 'move', 'fly_04.png'))  # 羽ばたきは 1-2-3-2 で一巡
    print(f'{len(outs)} frames, canvas {side_out}px (scale {scale:.3f})')
    if preview:
        n = len(outs)
        sheet = Image.new('RGB', (side_out * n, side_out), (70, 80, 90))
        for k, c in enumerate(outs):
            sheet.paste(c, (k * side_out, 0), c)
        from PIL import ImageDraw
        d = ImageDraw.Draw(sheet)
        for k in range(n):
            cx = k * side_out + side_out // 2
            d.line([(cx - 6, side_out // 2), (cx + 6, side_out // 2)], fill=(0, 255, 0))
            d.line([(cx, side_out // 2 - 6), (cx, side_out // 2 + 6)], fill=(0, 255, 0))
        sheet.save(preview, quality=90)


if __name__ == '__main__':
    main()
