# ラストダンジョンのエンドロール用: 巨大文字のグリフ画像(1文字=1枚)を作る。
# 高さは全グリフ共通(キャップハイト基準)、ベースライン=画像の下端(縁取りぶんの余白)。Unity側はピボット=下端中央。
import os, sys
from PIL import Image, ImageDraw, ImageFont, ImageFilter, ImageChops

OUT = sys.argv[1] if len(sys.argv) > 1 else "glyphs"
FONT = r"C:/Windows/Fonts/palab.ttf"
SIZE = 300          # フォントサイズ(px)
STROKE = 12         # 縁取り
PAD = STROKE + 6
CHARS = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789?!/&.-',:+"
NAMES = {"?": "q", "!": "ex", "/": "slash", "&": "amp", ".": "dot", "-": "dash", "'": "apos", ",": "comma", ":": "colon", "+": "plus"}

os.makedirs(OUT, exist_ok=True)
font = ImageFont.truetype(FONT, SIZE)
# キャップハイト(Hの上端〜ベースライン)
ascent, descent = font.getmetrics()
hb = font.getbbox("H")
cap_top = hb[1]
baseline = ascent
cap_h = baseline - cap_top
H = cap_h + PAD * 2 + int(SIZE * 0.08)   # 少し上に余裕(Q/Jの飾り、?の上端など)
print("cap height", cap_h, "canvas height", H)

def gradient(w, h):
    top = (252, 236, 178); mid = (226, 186, 104); bot = (150, 104, 52)
    g = Image.new("RGB", (w, h))
    px = g.load()
    for y in range(h):
        t = y / max(1, h - 1)
        if t < 0.45:
            k = t / 0.45; c = tuple(int(top[i] + (mid[i] - top[i]) * k) for i in range(3))
        else:
            k = (t - 0.45) / 0.55; c = tuple(int(mid[i] + (bot[i] - mid[i]) * k) for i in range(3))
        for x in range(w):
            px[x, y] = c
    return g

for ch in CHARS:
    bb = font.getbbox(ch)
    gw = bb[2] - bb[0]
    W = gw + PAD * 2
    top_y = H - PAD - baseline  # 文字の原点(描画位置)のy。ベースラインが下端からPADの所に来る
    ox = PAD - bb[0]
    # 縁取り(暗い石色)
    outline = Image.new("L", (W, H), 0)
    ImageDraw.Draw(outline).text((ox, top_y), ch, font=font, fill=255, stroke_width=STROKE, stroke_fill=255)
    face = Image.new("L", (W, H), 0)
    ImageDraw.Draw(face).text((ox, top_y), ch, font=font, fill=255)
    # 面: 金のグラデーション+上側の明るい縁(面取り)
    grad = gradient(W, H)
    inner = face.filter(ImageFilter.MinFilter(5))
    bevel = ImageChops.subtract(face, ImageChops.offset(face, 0, 4))  # 上端のふち
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    dark = Image.new("RGBA", (W, H), (38, 26, 18, 255))
    img.paste(dark, (0, 0), outline)
    face_rgba = grad.convert("RGBA")
    img.paste(face_rgba, (0, 0), face)
    hi = Image.new("RGBA", (W, H), (255, 250, 222, 255))
    img.paste(hi, (0, 0), bevel.point(lambda v: int(v * 0.8)))
    # 内側を少し暗く(彫りの陰影)
    shade = ImageChops.subtract(face, inner)
    sh = Image.new("RGBA", (W, H), (120, 80, 36, 255))
    img.paste(sh, (0, 0), shade.point(lambda v: int(v * 0.35)))
    # 横幅を詰める(縁取りの外の透明)
    bbox = img.getbbox()
    if bbox:
        l = max(0, bbox[0] - 2); r = min(W, bbox[2] + 2)
        img = img.crop((l, 0, r, H))
    name = NAMES.get(ch, ch)
    img.save(os.path.join(OUT, f"glyph_{name}.png"))
print("done", len(CHARS))
