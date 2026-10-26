"""bootframes.py <録画フォルダ> [fps]: 起動の録画をコマに分け、各コマを「OS の起動画面/無地/ロゴ/ホーム」に分類して時間を出す(2026-10-26 起動ロゴの調査)
  ロゴ = 濃紺(#060810)の画面の中央に色の濃い画素(虹色の文字・三毛猫)がある。無地 = 濃紺だけ。ホーム = 明るい/色の多い画面
  時刻は launch.txt の LAUNCH(ホスト)と rec.txt の REC の差で、起動からの秒にする。sheet.jpg に変化のあったコマを並べる
"""
import sys, os, subprocess, glob, json
import numpy as np
from PIL import Image, ImageDraw
D = sys.argv[1]; FPS = float(sys.argv[2]) if len(sys.argv) > 2 else 10
rec = float(open(os.path.join(D, 'rec.txt')).read().split()[1]); lau = float(open(os.path.join(D, 'launch.txt')).read().split()[1])
F = os.path.join(D, 'f'); os.makedirs(F, exist_ok=True)
for f in glob.glob(os.path.join(F, '*.png')): os.remove(f)
vid = [p for p in (os.path.join(D, 'screen.webm'), os.path.join(D, 'screen.mp4')) if os.path.exists(p)][0]
subprocess.run(['ffmpeg', '-v', 'error', '-i', vid, '-vf', f'fps={FPS},scale=270:-1', os.path.join(F, '%05d.png')], check=True)
BG = np.array([6, 8, 16], float)
def classify(a):
    a = a.astype(float); h, w, _ = a.shape
    near = (np.abs(a - BG).max(2) < 14).mean()
    mx = a.max(2); mn = a.min(2); sat = (mx - mn) / (mx + 1e-6)
    c = a[int(h * .3):int(h * .7), int(w * .2):int(w * .8)]
    cmx = c.max(2); cmn = c.min(2); colorful = ((cmx - cmn) / (cmx + 1e-6) > .45) & (cmx > 110)
    lum = a.mean()
    if near > .80 and colorful.mean() > .004: k = 'logo'
    elif near > .93: k = 'plain'
    elif lum < 40 and near < .5 and a.std() < 12: k = 'os'      # OS の起動画面(灰色/黒の無地)
    else: k = 'other'
    return k, dict(near=round(near, 3), colorful=round(float(colorful.mean()), 4), lum=round(lum, 1))
fs = sorted(glob.glob(os.path.join(F, '*.png'))); rows = []
for i, f in enumerate(fs):
    t = i / FPS + (rec - lau)   # 起動(LAUNCH)からの秒
    k, m = classify(np.array(Image.open(f).convert('RGB'))); rows.append((t, k, m, f))
# 区間にまとめる
seg = []
for t, k, m, f in rows:
    if seg and seg[-1]['k'] == k: seg[-1]['t1'] = t + 1 / FPS; seg[-1]['n'] += 1
    else: seg.append(dict(k=k, t0=t, t1=t + 1 / FPS, n=1, f=f, m=m))
for s in seg: print(f"{s['k']:6s} {s['t0']:7.2f} - {s['t1']:7.2f}s  ({s['t1'] - s['t0']:.2f}s) {s['m']}")
json.dump([dict(k=s['k'], t0=round(s['t0'], 2), t1=round(s['t1'], 2)) for s in seg], open(os.path.join(D, 'segments.json'), 'w'), ensure_ascii=False, indent=1)
# 変化のあったコマ(区間の最初)を並べる
pick = [s['f'] for s in seg][:36]; ts = [s for s in seg][:36]
if pick:
    ims = [Image.open(p).convert('RGB') for p in pick]; w, h = ims[0].size; cols = min(12, len(ims)); rws = (len(ims) + cols - 1) // cols
    sh = Image.new('RGB', (cols * w, rws * (h + 18)), (60, 60, 60)); d = ImageDraw.Draw(sh)
    for i, (im, s) in enumerate(zip(ims, ts)):
        x = (i % cols) * w; y = (i // cols) * (h + 18); sh.paste(im, (x, y + 18)); d.text((x + 2, y + 3), f"{s['k']} {s['t0']:.1f}s", fill=(255, 255, 0))
    sh.save(os.path.join(D, 'sheet.jpg'), quality=85)
