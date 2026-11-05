# 背景の絵の「敵が来る中央の帯」(上から 30〜65%)の明るさ・ばらつき・明るい点の割合を測る / 中央の帯だけ少し落とす(2026-11-05)。
#   測る:  python -I bandtone.py stat <png> [<png> ...]
#   落とす: python -I bandtone.py tone <入力png> <出力png> [強さ 0〜1 (既定 0.5)] [寄せる色 r,g,b (既定 0.60,0.68,0.80)]
#   中央の帯の明るい所ほど強く落とし(白い雲/光る結晶の点)、帯の上下はなめらかに戻す。帯の外は変えない。
import sys
import numpy as np
from PIL import Image

def lum(a):
    c = a / 255.0
    return 0.2126 * c[..., 0] + 0.7152 * c[..., 1] + 0.0722 * c[..., 2]

def band_mask(h, top=0.30, bottom=0.65, feather=0.08):
    y = (np.arange(h) + 0.5) / h
    m = np.clip(np.minimum((y - (top - feather)) / feather, ((bottom + feather) - y) / feather), 0, 1)
    return m[:, None]

def stat(paths):
    for p in paths:
        a = np.asarray(Image.open(p).convert('RGB'), dtype=np.float64)
        h = a.shape[0]
        band = a[int(h * 0.30):int(h * 0.65)]
        L = lum(band)
        print(f"{p.split('/')[-1]:40s} band mean {L.mean():.3f} std {L.std():.3f} bright(>0.85) {np.mean(L > 0.85) * 100:5.1f}%  whole mean {lum(a).mean():.3f}")

def tone(src, dst, k=0.5, col=(0.60, 0.68, 0.80)):
    im = Image.open(src).convert('RGB')
    a = np.asarray(im, dtype=np.float64) / 255.0
    h = a.shape[0]
    m = band_mask(h)[..., None] if False else band_mask(h)
    L = lum(a * 255.0)
    w = (m * k * np.clip((L - 0.45) / 0.45, 0, 1))[..., None]   # 明るい所ほど強く
    target = np.array(col)[None, None, :]
    out = a * (1 - w) + (a * 0.82 * 0.5 + target * 0.5) * w
    Image.fromarray(np.clip(out * 255.0 + 0.5, 0, 255).astype(np.uint8)).save(dst)

if __name__ == '__main__':
    if sys.argv[1] == 'stat': stat(sys.argv[2:])
    else:
        k = float(sys.argv[4]) if len(sys.argv) > 4 else 0.5
        col = tuple(float(x) for x in sys.argv[5].split(',')) if len(sys.argv) > 5 else (0.60, 0.68, 0.80)
        tone(sys.argv[2], sys.argv[3], k, col)
