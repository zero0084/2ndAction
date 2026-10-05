"""切り抜いた立ち絵を Character Select の表示用 700x1100 キャンバスへ配置する。
- 頭の大きさをそろえる: 「頭頂〜足先」(画像の実測、武器/翼は含めない)を姿勢の係数(しゃがみ/踏み込みは低い)で「立った時の高さ」に直し、
  その高さが TARGET になる倍率にする。
- 横は左のカード一覧/右の説明欄に重ならない範囲 [XMIN, XMAX] に収める(はみ出すなら倍率を下げる = 記録する)。
- 足元はキャンバス下端から FOOT_PAD 上(浮遊は FLOAT だけさらに上)、胴体の中心を CENTER_X に置く。
usage: place.py <cut_dir> <out_dir>
"""
import sys, os, json
import numpy as np
from PIL import Image

CW, CH = 700, 1100
TARGET = 930          # 立った時の頭頂〜足先(キャンバス px)
XMIN, XMAX = 56, 694  # 表示枠 560x880(中央+60px)でカード一覧(〜790)と説明欄(1296〜)に重ならない範囲
TOPMIN = 8
FOOT_PAD = 24
CENTER_X = 380

# id: (頭頂y, 足先y, 姿勢係数, 浮かせる px, 胴体の中心x(切り抜き画像の比率、None=重心))
P = {
    'swordsman':     (100, 1330, 0.95, 0, None),
    'noble_lady':    (37, 1475, 1.00, 0, None),
    'dual_blade':    (95, 1460, 0.92, 0, None),
    'gunslinger':    (75, 1510, 1.00, 0, None),
    'dragon_lancer': (285, 1502, 1.00, 0, None),
    'archer':        (120, 1313, 0.84, 0, None),
    'mage':          (36, 1440, 1.06, 70, None),
    'fighter':       (64, 1270, 0.90, 0, None),
    'ninja':         (173, 911, 0.57, 0, None),
    'dragonkin':     (150, 1512, 0.95, 0, None),
    'vampire':       (30, 1477, 1.00, 0, None),
    'miko':          (36, 1447, 1.00, 0, None),
}
over = {}
if os.path.exists(sys.argv[2] + '/params.json'):
    over = json.load(open(sys.argv[2] + '/params.json'))
src, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)
report = {}
for cid, (top, feet, pose, lift, cx) in P.items():
    if cid in over: top, feet, pose, lift = over[cid][:4]
    im = Image.open(os.path.join(src, cid + '.png')).convert('RGBA')
    a = np.array(im)[..., 3] > 40
    body = feet - top
    s = TARGET * pose / body
    # 横/上に収まるか
    w = im.width * s
    lim = []
    if w > XMAX - XMIN: lim.append(('width', (XMAX - XMIN) / im.width))
    avail_h = CH - FOOT_PAD - lift - TOPMIN
    if feet * s > avail_h: lim.append(('height', avail_h / feet))
    s_used = min([s] + [v for _, v in lim])
    im2 = im.resize((max(1, int(im.width * s_used)), max(1, int(im.height * s_used))), Image.LANCZOS)
    a2 = np.array(im2)[..., 3] > 40
    cols = np.where(a2.any(axis=0))[0]
    mass_x = (np.where(a2)[1].mean()) if a2.any() else im2.width / 2
    x = int(CENTER_X - mass_x)
    x = max(XMIN - int(cols.min()), min(x, XMAX - int(cols.max()) - 1))
    y = int(CH - FOOT_PAD - lift - feet * s_used)
    y = max(TOPMIN, y)
    canvas = Image.new('RGBA', (CW, CH), (0, 0, 0, 0))
    canvas.paste(im2, (x, y), im2)
    canvas.save(os.path.join(out, cid + '_main.png'))
    head_px = (feet - top) / pose * s_used  # 立った時の高さ(頭の大きさの目安)
    report[cid] = dict(scale=round(s_used, 3), wanted=round(s, 3), limit=[l for l, _ in lim if _ < s], standing=round(head_px), x=x, y=y)
    print(cid, report[cid])
json.dump(report, open(os.path.join(out, 'report.json'), 'w'), indent=1)
