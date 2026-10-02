# YES/NOの文字(Y,E,S,N,O)に、ひび割れ3段階を文字の形で切り抜いて焼き込む。glyph_<c>_c1..c3.png
import os, sys, random, math
from PIL import Image, ImageDraw, ImageChops, ImageFilter

GL = sys.argv[1]
OUT = sys.argv[2]
os.makedirs(OUT, exist_ok=True)

def cracks(w, h, seed, n):
    rnd = random.Random(seed)
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    for _ in range(n):
        x, y = rnd.uniform(w * 0.3, w * 0.7), rnd.uniform(h * 0.25, h * 0.75)
        a = rnd.uniform(0, math.tau)
        wid = rnd.uniform(9, 13)
        for s in range(rnd.randint(6, 9)):
            L = rnd.uniform(h * 0.06, h * 0.11)
            a += rnd.uniform(-0.6, 0.6)
            nx, ny = x + math.cos(a) * L, y + math.sin(a) * L
            d.line([(x, y), (nx, ny)], fill=(26, 16, 10, 255), width=int(wid))
            d.line([(x + 2, y + 2), (nx + 2, ny + 2)], fill=(255, 236, 190, 170), width=max(2, int(wid * 0.22)))
            if rnd.random() < 0.4:
                bx, by, ba, bw = nx, ny, a + rnd.choice([-1, 1]) * rnd.uniform(0.7, 1.2), wid * 0.6
                for _ in range(rnd.randint(2, 3)):
                    BL = rnd.uniform(h * 0.04, h * 0.07)
                    ba += rnd.uniform(-0.4, 0.4)
                    ex, ey = bx + math.cos(ba) * BL, by + math.sin(ba) * BL
                    d.line([(bx, by), (ex, ey)], fill=(26, 16, 10, 235), width=max(3, int(bw)))
                    bx, by, bw = ex, ey, bw * 0.75
            x, y = nx, ny
            wid = max(4, wid * 0.9)
    return img

for c in "YESNO":
    g = Image.open(os.path.join(GL, f"glyph_{c}.png")).convert("RGBA")
    w, h = g.size
    alpha = g.split()[3]
    # 縁取りの内側だけに入れる(縁取りを少し削った形で切り抜く)
    inner = alpha.filter(ImageFilter.MinFilter(9))
    acc = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    for k, (seed, n) in enumerate([(ord(c) * 7 + 1, 2), (ord(c) * 7 + 2, 2), (ord(c) * 7 + 3, 3)]):
        layer = cracks(w, h, seed, n)
        acc = Image.alpha_composite(acc, layer)
        masked = acc.copy()
        masked.putalpha(ImageChops.multiply(acc.split()[3], inner))
        out = Image.alpha_composite(g, masked)
        # 段階が進むほど少し暗く(傷んだ石)
        dark = Image.new("RGBA", (w, h), (40, 24, 12, int(22 * (k + 1))))
        dm = dark.copy(); dm.putalpha(ImageChops.multiply(dark.split()[3], alpha))
        out = Image.alpha_composite(out, dm)
        out.save(os.path.join(OUT, f"glyph_{c}_c{k + 1}.png"))
print("cracked done")
