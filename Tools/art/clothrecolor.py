"""合成したコマの布の色を、キャラ全体の中央値へ寄せる(2026-10-25 布表現の見直し)。
エステルが1コマずつ描くと緑が青緑/黄緑に振れ、動作の中で色がちらつくため。布の色の所(CLOTH_SPEC)だけ、
色相のずれを戻し、彩度・明度の中央値を全体へ合わせる。usage: clothrecolor.py <id> [dh=6] [--dry]
変えたコマと量は chars/<id>/recolor.txt へ(合成し直したら再び実行する。clothrecompose.sh が最後に呼ぶ)"""
import sys, os, glob
import numpy as np
from PIL import Image

def rgb_to_hsv(x):  # x: (...,3) 0..1 → (...,3) 0..1(PIL の HSV 変換を使う)
    sh = x.shape; a = Image.fromarray(np.clip(x.reshape(-1, 1, 3) * 255, 0, 255).astype(np.uint8), 'RGB').convert('HSV')
    return (np.array(a).astype(float) / 255.0).reshape(sh)
def hsv_to_rgb(x):
    sh = x.shape; a = Image.fromarray(np.clip(x.reshape(-1, 1, 3) * 255, 0, 255).astype(np.uint8), 'HSV').convert('RGB')
    return (np.array(a).astype(float) / 255.0).reshape(sh)
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from clothcmp import clothmask
id = sys.argv[1]; dh = float(sys.argv[2]) if len(sys.argv) > 2 and not sys.argv[2].startswith('-') else 6; dry = '--dry' in sys.argv
W = f'Docs/ClothRevision_2026-10-25/chars/{id}'
spec = [l.split('=', 1)[1].strip() for l in open(W + '.txt', encoding='utf-8') if l.startswith('CLOTH_SPEC=')][0]
def stats(im, m):
    hsv = rgb_to_hsv(im[..., :3][m] / 255.0); h = np.degrees(np.angle(np.mean(np.exp(2j * np.pi * hsv[:, 0])))) % 360
    return h, np.median(hsv[:, 1]), np.median(hsv[:, 2])
fs = sorted(glob.glob(W + '/local/**/*.png', recursive=True)); data = []
for f in fs:
    im = np.array(Image.open(f).convert('RGBA')).astype(float); m = clothmask(im[..., :3], spec) & (im[..., 3] > 200)
    if m.sum() >= 200: data.append((f, im, m) + stats(im, m))
H = np.degrees(np.angle(np.mean([np.exp(1j * np.radians(d[3])) for d in data]))) % 360
S = np.median([d[4] for d in data]); V = np.median([d[5] for d in data]); log = [f'median h={H:.0f} s={S:.2f} v={V:.2f}']
for f, im, m, h, s, v in data:
    d = (h - H + 180) % 360 - 180; ks = S / max(s, 1e-3); kv = V / max(v, 1e-3)
    if abs(d) <= dh and abs(ks - 1) < 0.15 and abs(kv - 1) < 0.12: continue
    ks = float(np.clip(ks ** 0.6, 0.75, 1.3)); kv = float(np.clip(kv ** 0.6, 0.88, 1.12))  # 半分ほど寄せる(寄せ切ると布の陰影が平たくなる)
    hsv = rgb_to_hsv(im[..., :3] / 255.0); sel = ndm = m
    # 布の縁は1px ぼかして混ぜる(色の段差を作らない)
    from scipy import ndimage
    w = ndimage.gaussian_filter(m.astype(float), 1.0) * (im[..., 3] > 0)
    nh = hsv.copy(); nh[..., 0] = (hsv[..., 0] - d / 360.0) % 1.0; nh[..., 1] = np.clip(hsv[..., 1] * ks, 0, 1); nh[..., 2] = np.clip(hsv[..., 2] * kv, 0, 1)
    rgb = hsv_to_rgb(nh) * 255.0; out = im.copy(); out[..., :3] = im[..., :3] * (1 - w[..., None]) + rgb * w[..., None]
    log.append(f'{os.path.relpath(f, W + "/local").replace(chr(92), "/")}\tdh={-d:+.0f}\ts*{ks:.2f}\tv*{kv:.2f}')
    if not dry: Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), 'RGBA').save(f)
print('\n'.join(log)); 
if not dry: open(W + '/recolor.txt', 'w', encoding='utf-8').write('\n'.join(log) + '\n')
