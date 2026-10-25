"""立ち絵(キャラ選択の _main.png)の布だけを差し替える(2026-10-25 布表現の見直し、全12人)。
アニメのコマと同じ「体は元の画素、新しい絵は布の所だけ」(clothlocal.py --mode cape)を、元の raw の座標で行ってから、
今の _main.png と同じ置き方(clothcmp.py の orig_to_main)で 700x1100 へ置く。
usage: clothportrait.py <元のraw.png> <エステルの編集raw.png> <今の_main.png> <作業フォルダ> <id> [clothlocal の追加の引数 ...]
  布の色は環境変数 CLOTH_SPEC(clothcmp.clothmask の書式)。
  出力: <作業フォルダ>/<id>_main.png(ゲーム用と同じ 700x1100)、<id>_aligned_raw.png、<id>_orig_cut.png、<id>_local_raw.png、dbg/
"""
import sys, os, json, subprocess
import numpy as np
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from clothcmp import cut, fit_transform, place

raw, edit, main, wd, cid = sys.argv[1:6]; extra = sys.argv[6:]
os.makedirs(wd, exist_ok=True)
here = os.path.dirname(os.path.abspath(__file__)); py = sys.executable
subprocess.run([py, os.path.join(here, 'clothcmp.py'), raw, edit, wd, cid, '--main', main], check=True)
info = json.load(open(os.path.join(wd, cid + '_align.json')))
O = cut(raw); Image.fromarray(O).save(os.path.join(wd, cid + '_orig_cut.png'))
loc = os.path.join(wd, cid + '_local_raw.png')
subprocess.run([py, os.path.join(here, 'clothlocal.py'), os.path.join(wd, cid + '_orig_cut.png'), os.path.join(wd, cid + '_aligned_raw.png'), loc,
                '--near', '6', '--mode', os.environ.get('CLOTH_MODE', 'cape'), '--despeck', '3000', '--debug', os.path.join(wd, 'dbg')] + extra, check=True)
t = info['orig_to_main']; M = Image.open(main)
place(np.array(Image.open(loc).convert('RGBA')), t['scale'], t['dx'], t['dy'], M.size).save(os.path.join(wd, cid + '_main.png'))
print('->', os.path.join(wd, cid + '_main.png'), info)
