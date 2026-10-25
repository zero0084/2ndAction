"""エステルへ渡す1コマの入力を作る(2026-10-25 布表現の見直し)。中身の上下 24px を残して切り、4倍にしてマゼンタの上へ。
切った位置は <出力フォルダ>/crop.json に足す(clothframe.py が戻す時に使う)。
usage: clothprep.py <出力フォルダ> <コマ.png> [<コマ.png> ...]
  出力: <出力フォルダ>/<フォルダ名>__<名前>_x4.png。crop.json のキーは <フォルダ名>__<名前>.png
"""
import sys, os, json
from PIL import Image

out = sys.argv[1]; os.makedirs(out, exist_ok=True)
jp = os.path.join(out, 'crop.json')
info = json.load(open(jp)) if os.path.exists(jp) else {}
for f in sys.argv[2:]:
    im = Image.open(f).convert('RGBA'); bb = im.getbbox()
    y0 = max(0, bb[1] - 24); y1 = min(im.height, bb[3] + 24)
    c = im.crop((0, y0, im.width, y1)); S = 4
    big = c.resize((c.width * S, c.height * S), Image.LANCZOS)
    m = Image.new('RGBA', big.size, (255, 0, 255, 255)); m.alpha_composite(big)
    key = os.path.basename(os.path.dirname(f)) + '__' + os.path.basename(f)
    m.convert('RGB').save(os.path.join(out, key.replace('.png', '_x4.png')))
    info[key] = dict(y0=y0, y1=y1, scale=S, size=list(big.size))
    print(key, big.size)
json.dump(info, open(jp, 'w'), indent=1)
