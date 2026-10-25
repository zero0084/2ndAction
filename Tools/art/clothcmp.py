"""布の修正の前後比較(2026-10-25 布表現の見直し)。
エステルが元画像を編集した絵(マゼンタ背景)を、元画像と同じ位置・縮尺へ合わせ、
キャラ選択の立ち絵(700x1100)と同じ置き方で並べる。元画像・ゲーム用素材は書き換えない。

usage: clothcmp.py <元のraw.png> <修正のraw.png> <出力フォルダ> <名前> [--main <今の_main.png>]
  - 位置合わせ: 布以外(赤くない所 = 顔・鎧・剣)の形で、倍率 0.90〜1.10 と移動量を探す
  - 置き方: 元の raw → 今の _main.png への変換(倍率・移動)を、元の raw と今の _main の照合で求めて、修正版にも同じ変換を掛ける
出力: <名前>_main.png(700x1100 透過、試作) / <名前>_aligned_raw.png / align.json
"""
import sys, os, json
import numpy as np
from PIL import Image
from scipy import ndimage

def cut(path):
    im = np.array(Image.open(path).convert('RGB')).astype(float)
    r, g, b = im[..., 0], im[..., 1], im[..., 2]
    m = np.minimum(r, b) - g
    alpha = np.clip((95 - m) / (95 - 35), 0, 1)
    spill = np.clip(np.minimum(r, b) - g, 0, None) * (1 - alpha * 0.6)
    r2 = r - spill; b2 = b - spill
    solid = alpha > 0.5
    lab, n = ndimage.label(solid)
    if n:
        sizes = ndimage.sum(solid, lab, range(1, n + 1))
        keep = np.zeros(n + 1, bool)
        for i, sz in enumerate(sizes, 1):
            if sz >= max(400, sizes.max() * 0.002): keep[i] = True
        mask = ndimage.binary_dilation(keep[lab], iterations=3)
    else:
        mask = solid
    a = alpha * mask
    return np.dstack([np.dstack([r2, g, b2]).clip(0, 255), a * 255]).astype(np.uint8)

def body_mask(rgba):
    """布(赤)以外の不透明な所。顔・鎧・剣の位置合わせに使う"""
    rgb = rgba[..., :3].astype(float); a = rgba[..., 3] > 128
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    red = (r > 90) & (r > g * 1.8) & (r > b * 1.8)
    return (a & ~red).astype(float)

def resize(rgba, s):
    im = Image.fromarray(rgba)
    return np.array(im.resize((max(1, round(im.width * s)), max(1, round(im.height * s))), Image.LANCZOS))

def best_shift(ref, mov):
    """ref と mov(同じ大きさ)の相互相関の山 → (dy, dx), 一致度"""
    H, W = ref.shape
    F = np.fft.rfft2(ref - ref.mean()) * np.conj(np.fft.rfft2(mov - mov.mean()))
    c = np.fft.irfft2(F, s=(H, W))
    i = np.unravel_index(np.argmax(c), c.shape)
    dy, dx = [v if v <= n // 2 else v - n for v, n in zip(i, (H, W))]
    return dy, dx, c[i] / (np.linalg.norm(ref - ref.mean()) * np.linalg.norm(mov - mov.mean()) + 1e-9)

def fit_transform(ref_rgba, mov_rgba, scales, down=4):
    """mov を倍率 s・移動 (dx, dy) で ref の座標へ。ref の大きさのキャンバスで探す"""
    ref = body_mask(ref_rgba)[::down, ::down]
    best = None
    for s in scales:
        m = body_mask(resize(mov_rgba, s))[::down, ::down]
        can = np.zeros_like(ref)
        h, w = min(m.shape[0], ref.shape[0]), min(m.shape[1], ref.shape[1])
        can[:h, :w] = m[:h, :w]
        dy, dx, sc = best_shift(ref, can)
        if best is None or sc > best[3]: best = (s, dx * down, dy * down, sc)
    # 細かく(down=1 の近く)
    s, dx, dy, sc = best
    ref1 = body_mask(ref_rgba)
    m1 = body_mask(resize(mov_rgba, s))
    bestf = best
    for ddy in range(-down, down + 1):
        for ddx in range(-down, down + 1):
            can = np.zeros_like(ref1)
            ox, oy = dx + ddx, dy + ddy
            ys, xs = max(0, oy), max(0, ox)
            ye, xe = min(ref1.shape[0], oy + m1.shape[0]), min(ref1.shape[1], ox + m1.shape[1])
            if ye <= ys or xe <= xs: continue
            can[ys:ye, xs:xe] = m1[ys - oy:ye - oy, xs - ox:xe - ox]
            sc2 = (ref1 * can).sum() / (np.sqrt((ref1 ** 2).sum() * (can ** 2).sum()) + 1e-9)
            if bestf is best or sc2 > bestf[3]: bestf = (s, ox, oy, sc2)
    return bestf

def place(rgba, s, ox, oy, size):
    out = Image.new('RGBA', size, (0, 0, 0, 0))
    im = Image.fromarray(resize(rgba, s))
    out.paste(im, (int(round(ox)), int(round(oy))), im)
    return out

if __name__ == '__main__':
    a = sys.argv[1:]
    orig, edit, outdir, name = a[:4]
    main = a[a.index('--main') + 1] if '--main' in a else None
    os.makedirs(outdir, exist_ok=True)
    O = cut(orig); E = cut(edit)
    # 修正版 → 元の raw の座標
    s1, x1, y1, sc1 = fit_transform(O, E, np.arange(0.90, 1.101, 0.005))
    aligned = np.array(place(E, s1, x1, y1, (O.shape[1], O.shape[0])))
    Image.fromarray(aligned).save(os.path.join(outdir, name + '_aligned_raw.png'))
    info = dict(edit_to_orig=dict(scale=round(float(s1), 4), dx=int(x1), dy=int(y1), score=round(float(sc1), 4)))
    if main:
        M = np.array(Image.open(main).convert('RGBA'))
        # 元の raw → 今の _main(倍率はおよそ 0.5〜0.8)
        s2, x2, y2, sc2 = fit_transform(M, O, np.arange(0.40, 0.90, 0.0025), down=2)
        info['orig_to_main'] = dict(scale=round(float(s2), 4), dx=int(x2), dy=int(y2), score=round(float(sc2), 4))
        place(aligned, s2, x2, y2, (M.shape[1], M.shape[0])).save(os.path.join(outdir, name + '_main.png'))
        place(O, s2, x2, y2, (M.shape[1], M.shape[0])).save(os.path.join(outdir, name + '_orig_replaced_check.png'))
    json.dump(info, open(os.path.join(outdir, name + '_align.json'), 'w'), indent=1)
    print(json.dumps(info))
