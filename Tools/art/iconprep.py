"""カードのアイコン(ChatGPT 生成・真っ黒の背景)を、ゲームのカードの絵(392x589、透明)へ。
既存の91枚(Art/Icons/CardIcons)と同じ見え方: 暗い紺の輪郭は不透明のまま、外側の発光だけ薄く消えていく。
usage: iconprep.py <in.png> <out.png> [--cy 0.43] [--fill 0.98] [--preview <prev.png>]
  --cy   : 絵の中心の高さ(上から、カードの高さに対する割合)。既存は 0.41〜0.56
  --fill : 横幅に対する絵の幅(既存はほぼ 0.98)
  --preview: 40px/64px の枠(RunBuildHud と同じ切り抜き)と、3択のカードの大きさで並べた確認用の絵
"""
import sys
import numpy as np
from PIL import Image, ImageDraw

W, H = 392, 589


def arg(name, default):
    return sys.argv[sys.argv.index(name) + 1] if name in sys.argv else default


def cut(src):
    a = np.asarray(Image.open(src).convert('RGB')).astype(np.float32)
    mx = a.max(axis=2)
    # 背景(真っ黒)からの距離で不透明度。紺の輪郭(最大 20 前後)はほぼ不透明、遠い発光は薄く
    al = np.clip((mx - 5.0) / 22.0, 0.0, 1.0)
    # 半透明の所は黒を混ぜた分を戻す(黒い背景の上で光っていた色を、透明の上でも同じ色に)
    rgb = np.clip(a / np.maximum(al[..., None], 1e-3), 0, 255)
    rgb = np.where(al[..., None] > 0.999, a, rgb)
    im = Image.fromarray(np.dstack([rgb, al * 255]).astype(np.uint8), 'RGBA')
    bb = im.split()[-1].point(lambda v: 255 if v > 24 else 0).getbbox()
    return im.crop(bb)


def place(im, cy, fill):
    k = min(W * fill / im.width, H * 0.92 / im.height)
    im = im.resize((max(1, round(im.width * k)), max(1, round(im.height * k))), Image.LANCZOS)
    can = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    y = int(round(H * cy - im.height / 2))
    y = max(0, min(H - im.height, y))
    can.alpha_composite(im, ((W - im.width) // 2, y))
    return can


def preview(icons, dst):
    """icons: [(名前, 392x589 RGBA)]。40/64px の枠(RunBuildHud: 内側86%、縦長は1.14倍で上下を切る)と、3択の大きさ(高さ234)"""
    rows = []
    for slot in (40, 64):
        can = Image.new('RGB', (len(icons) * (slot + 8) + 8, slot + 16), (20, 24, 40))
        for i, (_, ic) in enumerate(icons):
            x, y = 8 + i * (slot + 8), 8
            d = ImageDraw.Draw(can)
            d.rounded_rectangle((x, y, x + slot, y + slot), radius=slot // 6, fill=(30, 40, 70), outline=(150, 130, 80))
            inner = slot * 0.86
            h = inner * 1.14
            w = h * ic.width / ic.height
            t = ic.resize((max(1, round(w)), max(1, round(h))), Image.LANCZOS)
            layer = Image.new('RGBA', can.size, (0, 0, 0, 0))
            layer.alpha_composite(t, (round(x + slot * 0.07 + (inner - w) / 2), round(y + slot * 0.07 + (inner - h) / 2)))
            m = Image.new('L', can.size, 0)
            ImageDraw.Draw(m).rectangle((x + slot * 0.07, y + slot * 0.07, x + slot * 0.93, y + slot * 0.93), fill=255)
            layer.putalpha(Image.composite(layer.split()[-1], Image.new('L', can.size, 0), m))
            can.paste(layer, (0, 0), layer)
        rows.append(can.resize((can.width * 3, can.height * 3), Image.NEAREST))
    big = Image.new('RGB', (len(icons) * 180 + 20, 260), (10, 13, 30))
    for i, (_, ic) in enumerate(icons):
        t = ic.copy()
        t.thumbnail((156, 234), Image.LANCZOS)
        big.paste(t, (20 + i * 180 + (156 - t.width) // 2, 13 + (234 - t.height) // 2), t)
    rows.append(big)
    out = Image.new('RGB', (max(r.width for r in rows), sum(r.height for r in rows) + 10 * len(rows)), (0, 0, 0))
    y = 0
    for r in rows:
        out.paste(r, (0, y))
        y += r.height + 10
    out.save(dst)


if __name__ == '__main__':
    if sys.argv[1] == 'preview':  # iconprep.py preview <out.png> <icon1.png> <icon2.png> ...
        preview([(p, Image.open(p).convert('RGBA')) for p in sys.argv[3:]], sys.argv[2])
    else:
        src, dst = sys.argv[1], sys.argv[2]
        can = place(cut(src), float(arg('--cy', '0.43')), float(arg('--fill', '0.98')))
        can.save(dst)
        if '--preview' in sys.argv:
            preview([(dst, can)], arg('--preview', ''))
        print(dst, can.size)
