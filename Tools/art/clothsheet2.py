"""数コマを1枚に並べてエステルに編集してもらい、また1コマずつに分ける(2026-10-25 布表現の見直し、全12人)。
体は後で元の画素に戻す(clothlocal.py)ので、並べて小さくなった人物が描き直されても使える。布の構造がコマ間でそろいやすい。
usage:
  clothsheet2.py make <出力.png> <cols> <in_x4.png> ...      … マゼンタの余白を入れて並べる。並びは <出力>.json
  clothsheet2.py split <エステルの絵.png> <並び.json> <outフォルダ> [suffix=a]  … 並びの割合で切り分け、<outフォルダ>/<key>_<suffix>.png
"""
import sys, os, json
from PIL import Image

cmd = sys.argv[1]
if cmd == 'make':
    outp, cols, files = sys.argv[2], int(sys.argv[3]), sys.argv[4:]
    ims = [Image.open(f).convert('RGB') for f in files]
    cw = max(i.width for i in ims); ch = max(i.height for i in ims); gap = int(0.06 * max(cw, ch))
    rows = (len(ims) + cols - 1) // cols
    W = cols * cw + (cols + 1) * gap; H = rows * ch + (rows + 1) * gap
    S = Image.new('RGB', (W, H), (255, 0, 255)); lay = []
    for k, (f, im) in enumerate(zip(files, ims)):
        c, r = k % cols, k // cols
        x = gap + c * (cw + gap) + (cw - im.width) // 2; y = gap + r * (ch + gap) + (ch - im.height) // 2
        S.paste(im, (x, y))
        lay.append(dict(key=os.path.basename(f).replace('_x4.png', ''), box=[x / W, y / H, (x + im.width) / W, (y + im.height) / H], size=[im.width, im.height]))
    s = min(1.0, 2048 / max(W, H))  # 送る絵は長い辺 2048px まで
    S.resize((int(W * s), int(H * s)), Image.LANCZOS).save(outp)
    json.dump(dict(size=[W, H], cells=lay), open(os.path.splitext(outp)[0] + '.json', 'w'), indent=1)
    print(outp, (int(W * s), int(H * s)), len(lay), 'cells')
elif cmd == 'split':
    src, layp, outd = sys.argv[2:5]; suf = sys.argv[5] if len(sys.argv) > 5 else 'a'
    E = Image.open(src).convert('RGB'); lay = json.load(open(layp)); os.makedirs(outd, exist_ok=True)
    # 2026-10-25: エステルが人物を区画の中で動かしたり大きくしたりすることがあるので、区画で切らずに
    # 「マゼンタでない塊」を見つけ、重心が入る区画へ割り当てて、その外接の箱で切る(倍率は区画→元の4倍の比のまま)
    import numpy as np
    from scipy import ndimage
    A = np.array(E).astype(int)
    fg = ~((A[..., 0] > 190) & (A[..., 1] < 90) & (A[..., 2] > 190))
    fg = ndimage.binary_opening(fg, iterations=2)
    lb, nb = ndimage.label(ndimage.binary_dilation(fg, iterations=6))
    cms = ndimage.center_of_mass(fg, lb, range(1, nb + 1)); szs = ndimage.sum(fg, lb, range(1, nb + 1))
    for c in lay['cells']:
        x0, y0, x1, y1 = c['box']; px = 0.02
        cx0, cy0, cx1, cy1 = (x0 - 0.03) * E.width, (y0 - 0.03) * E.height, (x1 + 0.03) * E.width, (y1 + 0.03) * E.height
        ids = [i + 1 for i, (cm, sz) in enumerate(zip(cms, szs)) if sz > 400 and cx0 <= cm[1] <= cx1 and cy0 <= cm[0] <= cy1]
        if ids and os.environ.get('SPLIT_MODE') != 'cell':  # SPLIT_MODE=cell: 区画の割合で切る(エステルが配置を保った時はこちらが安定)
            ys, xs = np.where(np.isin(lb, ids) & fg); m = 12
            bx = (max(0, xs.min() - m), max(0, ys.min() - m), min(E.width, xs.max() + m), min(E.height, ys.max() + m))
            k = c['size'][0] / ((x1 - x0) * E.width)  # 区画 → 元の4倍
            tile = E.crop(bx); tile.resize((max(1, int(tile.width * k)), max(1, int(tile.height * k))), Image.LANCZOS).save(os.path.join(outd, f"{c['key']}_{suf}.png"))
            continue
        bx = (int((x0 - px) * E.width), int((y0 - px) * E.height), int((x1 + px) * E.width), int((y1 + px) * E.height))
        tile = E.crop(bx)
        # 元の x4 の大きさへ(余白を含めた比率のまま)
        sx = c['size'][0] * (x1 - x0 + 2 * px) / (x1 - x0); sy = c['size'][1] * (y1 - y0 + 2 * px) / (y1 - y0)
        tile.resize((int(sx), int(sy)), Image.LANCZOS).save(os.path.join(outd, f"{c['key']}_{suf}.png"))
    print('split', len(lay['cells']), '->', outd)
