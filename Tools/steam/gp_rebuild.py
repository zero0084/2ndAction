"""gp_rebuild.py <manifest.json> <差し替え表.json>: Google Play のスクリーンショットを作り直す(2026-10-26 布の見直しの後)。
manifest の各行の src を差し替え表で置き換え(無ければ同じ src=Steam のスクリーンショットを読み直す)、
透明度を外した 24bit PNG で書き出す。縦画面(1216x2160 などの2倍撮影)は 1080x1920 へ縮める。manifest も書き直す。
差し替え表: {"<dir>/<name>": "<新しい元の画像>"}。値が null の行は作り直さない(今のまま)"""
import sys, json, os, time
from PIL import Image
mf, rp = sys.argv[1:3]; base = os.path.dirname(os.path.abspath(mf))
M = json.load(open(mf, encoding='utf-8')); R = json.load(open(rp, encoding='utf-8'))
for r in M:
    key = r['dir'] + '/' + r['name']
    if key in R and R[key] is None: print('keep', key); continue
    src = R.get(key, r['src'])
    im = Image.open(src).convert('RGB')
    if im.size != (r['w'], r['h']): im = im.resize((r['w'], r['h']), Image.LANCZOS)
    out = os.path.join(base, r['dir'], r['name'])
    for k in range(5):
        try: im.save(out, optimize=True); break
        except OSError: time.sleep(2)
    r['src'] = src; r['bytes'] = os.path.getsize(out)
    print(key, r['bytes'])
json.dump(M, open(mf, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
