"""recut.py <旧EDL> <新EDL> <旧の素材フォルダ> <新の素材フォルダ> <出力の一覧.jpg>
撮り直した素材に合わせて、各カットの開始位置(in)を選び直す(2026-10-26 布の見直しの後の撮り直し)。
旧のカットの3コマ(長さの 10/50/90%)に一番似た所を、新しい撮影の同じショットから探す(縮小した画面の差の小ささ)。
結果を新EDLへ書き、旧(上)と新(下)を並べた一覧を出力する。開始位置を手で直す時は新EDLの in を書き換えて
--check だけで一覧を作り直す。
"""
import sys, os, json, subprocess
import numpy as np
from PIL import Image, ImageDraw
FF = os.environ.get('FFMPEG', 'ffmpeg')
oldedl, newedl, olddir, newdir, outjpg = sys.argv[1:6]; check = '--check' in sys.argv
O = json.load(open(oldedl, encoding='utf-8')); N = json.load(open(newedl, encoding='utf-8'))
def vid(root, shot):
    for ext in ('.mkv', '.mp4'):
        p = os.path.join(root, shot + ext)
        if os.path.exists(p): return p
    raise FileNotFoundError(root + '/' + shot)
def dur(p):
    return float(subprocess.run(['ffprobe', '-v', 'error', '-show_entries', 'format=duration', '-of', 'csv=p=0', p], capture_output=True, text=True).stdout.strip())
def grab(p, t, w=64, h=36):
    r = subprocess.run([FF, '-v', 'error', '-ss', f'{max(0, t):.3f}', '-i', p, '-frames:v', '1', '-vf', f'scale={w}:{h}', '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-'], capture_output=True)
    a = np.frombuffer(r.stdout, np.uint8)
    return a.reshape(h, w, 3).astype(float) if a.size == w * h * 3 else np.zeros((h, w, 3))
cache = {}
def strip(p, step=0.25):
    if p in cache: return cache[p]
    r = subprocess.run([FF, '-v', 'error', '-i', p, '-vf', f'fps={1/step},scale=64:36', '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-'], capture_output=True)
    a = np.frombuffer(r.stdout, np.uint8).reshape(-1, 36, 64, 3).astype(float); cache[p] = a; return a
fr = (0.1, 0.5, 0.9)
rows = []
for i, (oc, nc) in enumerate(zip(O['cuts'], N['cuts'])):
    op = vid(olddir, oc['shot'].replace('/raw/', '/raw/', 1)); np_ = vid(newdir, nc['shot'])
    ref = [grab(op, oc['in'] + oc['dur'] * f) for f in fr]
    if not check:
        S = strip(np_); L = dur(np_); step = 0.25; best = None
        for k in range(len(S)):
            t = k * step
            if t + nc['dur'] > L - 0.05: break
            idx = [min(len(S) - 1, int(round((t + nc['dur'] * f) / step))) for f in fr]
            d = sum(((S[j] - r) ** 2).mean() for j, r in zip(idx, ref))
            if best is None or d < best[0]: best = (d, t)
        nc['in'] = round(best[1], 2)
    rows.append((i, oc, nc, op, np_))
if not check: json.dump(N, open(newedl, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
W, H = 192, 108
sheet = Image.new('RGB', (W * 6 + 260, len(rows) * (H + 6) + 10), (30, 30, 30)); d = ImageDraw.Draw(sheet)
for r, (i, oc, nc, op, np_) in enumerate(rows):
    y = r * (H + 6) + 5
    d.text((4, y + 4), f"{i:02d} {nc['shot'].split('/')[-1]}", fill=(255, 255, 0)); d.text((4, y + 24), f"old in {oc['in']}", fill=(200, 200, 200)); d.text((4, y + 44), f"new in {nc['in']}", fill=(120, 255, 120)); d.text((4, y + 64), f"dur {nc['dur']}", fill=(200, 200, 200))
    for j, f in enumerate(fr):
        a = grab(op, oc['in'] + oc['dur'] * f, W, H); b = grab(np_, nc['in'] + nc['dur'] * f, W, H)
        sheet.paste(Image.fromarray(a.astype(np.uint8)), (260 + j * 2 * W, y)); sheet.paste(Image.fromarray(b.astype(np.uint8)), (260 + (j * 2 + 1) * W, y))
sheet.save(outjpg, quality=85); print('wrote', outjpg, len(rows))
