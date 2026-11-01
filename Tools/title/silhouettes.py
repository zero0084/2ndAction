# -*- coding: utf-8 -*-
# タイトル画面の影(2026-11-01)。出発演出(<Kit>Start_v1)から1コマずつ選び、透過を保った黒一色の絵にする。
#  ・元の絵は線画の所が少し透けている → 不透明度 48 以上は不透明にし、3px の小さな穴をふさぐ(外側の縁のなめらかさは元のまま)
#  ・足もと(下端から 3% の帯の不透明な画素の横の平均)が、絵の横の中心に来るように左右へ透明な余白を足す → 画面では「中心 = 足もと」で置ける
#  ・高さは最大 640px(縦画面でも表示は約 480px)。幅と高さは4の倍数(Windows の BC7)
#  使い方: python silhouettes.py <Unity の Assets> <出力フォルダ>
import sys, os
from PIL import Image, ImageChops, ImageFilter
PICK = [  # キャラ, フォルダ, コマ(横顔と武器の形が分かりやすい物)
    ("swordsman", "PlayerStart_v1", 0), ("dual_blade", "DualBladeStart_v1", 0), ("noble_lady", "NobleLadyStart_v1", 1),
    ("gunslinger", "GunslingerStart_v1", 1), ("dragon_lancer", "LancerStart_v1", 0), ("archer", "ArcherStart_v1", 2),
    ("mage", "MageStart_v1", 0), ("fighter", "FighterStart_v1", 2), ("ninja", "NinjaStart_v1", 0), ("miko", "MikoStart_v1", 3),
    ("vampire", "VampireStart_v1", 2), ("dragonkin", "DragonkinStart_v1", 0), ("luka", "LukaStart_v1", 0), ("pentatuttulu", "PentaStart_v1", 0),
]
MAXH = 640
def main():
    assets, out = sys.argv[1], sys.argv[2]
    os.makedirs(out, exist_ok=True)
    for cid, folder, idx in PICK:
        im = Image.open(f"{assets}/Art/{folder}/start_{idx:02d}.png").convert("RGBA")
        a = im.getchannel("A")
        a = a.point(lambda v: 255 if v >= 48 else int(v * 255 / 48))
        closed = a.filter(ImageFilter.MaxFilter(3)).filter(ImageFilter.MinFilter(3))
        a = ImageChops.lighter(a, closed)  # 線画の透けと小さな穴をふさぐ(閉じる処理は元の形を含む)
        bbox = a.getbbox(); a = a.crop(bbox)
        w, h = a.size
        band = max(2, int(h * 0.03))
        xs = [x for y in range(h - band, h) for x in range(w) if a.getpixel((x, y)) >= 128]
        fx = sum(xs) / len(xs) if xs else w / 2
        half = max(fx, w - fx)
        W = int(round(half * 2)); left = int(round(half - fx))
        canvas = Image.new("L", (W, h), 0); canvas.paste(a, (left, 0))
        s = min(1.0, MAXH / h)
        nw, nh = max(4, int(round(W * s / 4)) * 4), max(4, int(round(h * s / 4)) * 4)
        canvas = canvas.resize((nw, nh), Image.LANCZOS)
        sil = Image.new("RGBA", (nw, nh), (0, 0, 0, 0)); sil.putalpha(canvas)
        sil.save(f"{out}/{cid}.png", optimize=True)
        print(f"{cid:14s} {folder}/start_{idx:02d}  {nw}x{nh}  aspect {nw/nh:.3f}")
if __name__ == "__main__":
    main()
