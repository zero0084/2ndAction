# 背景を控えめにして敵を見やすく(2026-11-01)の前後比較を数字と画像にする。
#   python -I measure.py <QA の出力フォルダ(-qaBgTone)> <書き出し先>
# QA は同じ瞬間を「legacy(今までの見た目)/new」×「敵あり(_e)/敵なし(_n)」で撮り、敵の画面上の四角をログに書く。
# 敵の画素 = 敵あり と 敵なし で色が違う所(四角の中)。その画素について:
#   dE   … 敵の色 と その後ろの背景の色 の差(CIE76 ΔE、Lab)。大きいほど背景から浮いて見える
#   dL   … 明るさ(L*)だけの差
#   clutter … 敵の周り(四角を広げた枠)の背景の明るさのばらつき(L* の標準偏差)。小さいほど背景が落ち着いている
#   chroma  … 敵の周りの背景の彩度(Lab の a*b* の長さの平均)
import sys, os, re, glob, csv
import numpy as np
from PIL import Image, ImageDraw

def srgb_to_lab(rgb):
    c = rgb.astype(np.float64) / 255.0
    c = np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
    m = np.array([[0.4124, 0.3576, 0.1805], [0.2126, 0.7152, 0.0722], [0.0193, 0.1192, 0.9505]])
    xyz = c @ m.T
    xyz /= np.array([0.95047, 1.0, 1.08883])
    f = np.where(xyz > 0.008856, np.cbrt(xyz), 7.787 * xyz + 16 / 116)
    L = 116 * f[..., 1] - 16
    a = 500 * (f[..., 0] - f[..., 1])
    b = 200 * (f[..., 1] - f[..., 2])
    return np.stack([L, a, b], -1)

def main(src, out):
    os.makedirs(out, exist_ok=True)
    log = os.path.join(src, 'qa_bgtone.txt')
    rects = {}  # (base, tag) -> [(id, x0,y0,x1,y1, spawned)]
    for line in open(log, encoding='utf-8', errors='replace'):
        m = re.search(r'\[enemy\] (\S+)_(legacy|new) (\S+) rect=(-?\d+),(-?\d+),(-?\d+),(-?\d+) spawned=(\d)', line)
        if m:
            rects.setdefault((m.group(1), m.group(2)), []).append((m.group(3), *map(int, m.group(4, 5, 6, 7)), int(m.group(8))))
    files = {}
    for f in glob.glob(os.path.join(src, '*.png')):
        m = re.match(r'\d+_(.+)_(legacy|new)_(e|n)\.png$', os.path.basename(f))
        if m: files[(m.group(1), m.group(2), m.group(3))] = f
    rows = []
    bases = sorted({k[0] for k in files})
    for base in bases:
        for tag in ('legacy', 'new'):
            fe, fn = files.get((base, tag, 'e')), files.get((base, tag, 'n'))
            if not fe or not fn: continue
            E = np.asarray(Image.open(fe).convert('RGB')); N = np.asarray(Image.open(fn).convert('RGB'))
            H, W = E.shape[:2]
            LabE, LabN = None, None
            for (eid, x0, y0, x1, y1, sp) in rects.get((base, tag), []):
                x0c, y0c, x1c, y1c = max(0, x0), max(0, y0), min(W, x1), min(H, y1)
                if x1c - x0c < 6 or y1c - y0c < 6: continue
                diff = np.abs(E[y0c:y1c, x0c:x1c].astype(int) - N[y0c:y1c, x0c:x1c].astype(int)).max(-1)
                mask = diff > 18
                if mask.sum() < 30: continue
                if LabE is None: LabE, LabN = srgb_to_lab(E), srgb_to_lab(N)
                le, ln = LabE[y0c:y1c, x0c:x1c][mask], LabN[y0c:y1c, x0c:x1c][mask]
                dE = np.sqrt(((le - ln) ** 2).sum(-1)).mean()
                dL = np.abs(le[:, 0] - ln[:, 0]).mean()
                mw, mh = max(10, int((x1 - x0) * 0.6)), max(10, int((y1 - y0) * 0.6))
                rx0, ry0, rx1, ry1 = max(0, x0 - mw), max(0, y0 - mh), min(W, x1 + mw), min(H, y1 + mh)
                ring = np.ones((ry1 - ry0, rx1 - rx0), bool)
                ring[y0c - ry0:y1c - ry0, x0c - rx0:x1c - rx0] = False
                rl = LabN[ry0:ry1, rx0:rx1][ring]
                clutter = rl[:, 0].std() if len(rl) else float('nan')
                chroma = np.sqrt(rl[:, 1] ** 2 + rl[:, 2] ** 2).mean() if len(rl) else float('nan')
                rows.append(dict(scene=base, tag=tag, enemy=eid, spawned=sp, px=int(mask.sum()), dE=round(dE, 2), dL=round(dL, 2), clutter=round(clutter, 2), chroma=round(chroma, 2),
                                 bgL=round(rl[:, 0].mean(), 1) if len(rl) else float('nan')))
    with open(os.path.join(out, 'metrics.csv'), 'w', newline='', encoding='utf-8') as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0].keys()) if rows else ['scene']); w.writeheader(); w.writerows(rows)
    # 場面ごとの平均(前→後)。同じ敵が両方で測れた物だけを比べる
    lines = ['| 場面 | 敵の数 | 浮き上がり ΔL*/ごちゃつき 前→後 | 敵と後ろの差 ΔE 前→後 | 明るさの差 ΔL* 前→後 | 周りのごちゃつき 前→後 | 周りの彩度 前→後 | 周りの明るさ L* 前→後 |', '|---|---|---|---|---|---|---|---|']
    summary = []
    for base in bases:
        lg = {r['enemy'] + str(i): r for i, r in enumerate([r for r in rows if r['scene'] == base and r['tag'] == 'legacy'])}
        nw = {r['enemy'] + str(i): r for i, r in enumerate([r for r in rows if r['scene'] == base and r['tag'] == 'new'])}
        keys = [k for k in lg if k in nw]
        if not keys: continue
        def avg(d, k): return float(np.nanmean([d[x][k] for x in keys]))
        s = dict(scene=base, n=len(keys), **{f'{k}_{t}': avg(d, k) for k in ('dE', 'dL', 'clutter', 'chroma', 'bgL') for t, d in (('legacy', lg), ('new', nw))})
        summary.append(s)
        rl_, rn_ = s['dL_legacy'] / max(0.01, s['clutter_legacy']), s['dL_new'] / max(0.01, s['clutter_new'])
        s['sal_legacy'], s['sal_new'] = rl_, rn_
        lines.append(f"| {base} | {len(keys)} | {rl_:.2f} → {rn_:.2f} ({(rn_ / max(0.01, rl_) - 1) * 100:+.0f}%) | {s['dE_legacy']:.1f} → {s['dE_new']:.1f} ({(s['dE_new'] / max(0.01, s['dE_legacy']) - 1) * 100:+.0f}%) | {s['dL_legacy']:.1f} → {s['dL_new']:.1f} | {s['clutter_legacy']:.1f} → {s['clutter_new']:.1f} ({(s['clutter_new'] / max(0.01, s['clutter_legacy']) - 1) * 100:+.0f}%) | {s['chroma_legacy']:.1f} → {s['chroma_new']:.1f} | {s['bgL_legacy']:.0f} → {s['bgL_new']:.0f} |")
    open(os.path.join(out, 'summary.md'), 'w', encoding='utf-8').write('\n'.join(lines) + '\n')
    print('\n'.join(lines))
    # 前後の比較画像(敵ありの2枚を上下に、敵の四角つき)と、敵の拡大
    for base in bases:
        fl, fn_ = files.get((base, 'legacy', 'e')), files.get((base, 'new', 'e'))
        if not fl or not fn_: continue
        A, B = Image.open(fl).convert('RGB'), Image.open(fn_).convert('RGB')
        W, H = A.size
        sheet = Image.new('RGB', (W, H * 2 + 40), (16, 16, 20)); d = ImageDraw.Draw(sheet)
        d.text((8, 4), base + '  BEFORE (legacy)', fill=(255, 200, 80)); sheet.paste(A, (0, 20))
        d.text((8, H + 22), 'AFTER', fill=(120, 255, 140)); sheet.paste(B, (0, H + 40))
        sheet.save(os.path.join(out, f'compare_{base}.jpg'), quality=88)
        crops = []
        for (eid, x0, y0, x1, y1, sp) in rects.get((base, 'legacy'), []):
            mw, mh = max(24, int((x1 - x0) * 0.8)), max(24, int((y1 - y0) * 0.8))
            box = (max(0, x0 - mw), max(0, y0 - mh), min(W, x1 + mw), min(H, y1 + mh))
            if box[2] - box[0] < 20 or box[3] - box[1] < 20: continue
            ca, cb = A.crop(box), B.crop(box)
            s = 220 / max(ca.size[1], 1)
            sz = (max(1, int(ca.size[0] * s)), 220)
            crops.append((eid, ca.resize(sz), cb.resize(sz)))
        if crops:
            tw = sum(c[1].size[0] for c in crops) + 8 * (len(crops) + 1)
            cs = Image.new('RGB', (tw, 220 * 2 + 50), (16, 16, 20)); d = ImageDraw.Draw(cs); x = 8
            for eid, ca, cb in crops:
                d.text((x, 2), eid[:24], fill=(230, 230, 230)); cs.paste(ca, (x, 16)); cs.paste(cb, (x, 16 + 220 + 14)); x += ca.size[0] + 8
            d.text((2, 16 + 220), 'before / after', fill=(160, 160, 160))
            cs.save(os.path.join(out, f'enemies_{base}.jpg'), quality=90)

if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
