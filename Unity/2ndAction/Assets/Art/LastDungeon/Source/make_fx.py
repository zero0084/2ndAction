# エンドロール/ONE MORE MILE?用の補助素材: ひび割れ(3段階、重ねて使う)と石板。
import os, sys, random, math
from PIL import Image, ImageDraw, ImageFilter

OUT = sys.argv[1] if len(sys.argv) > 1 else "fx"
os.makedirs(OUT, exist_ok=True)

def crack_layer(seed, n_main, size=512):
    rnd = random.Random(seed)
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    for _ in range(n_main):
        # 端の近くから中心へ向かう稲妻状の割れ目 + 枝分かれ
        ang = rnd.uniform(0, math.tau)
        x, y = size / 2 + math.cos(ang) * size * 0.08, size / 2 + math.sin(ang) * size * 0.08
        a = ang + rnd.uniform(-0.4, 0.4)
        w = rnd.uniform(9, 14)
        steps = rnd.randint(7, 11)
        for s in range(steps):
            L = rnd.uniform(size * 0.035, size * 0.07)
            a += rnd.uniform(-0.55, 0.55)
            nx, ny = x + math.cos(a) * L, y + math.sin(a) * L
            d.line([(x, y), (nx, ny)], fill=(22, 14, 10, 255), width=int(w))
            d.line([(x - 2, y - 2), (nx - 2, ny - 2)], fill=(255, 240, 200, 150), width=max(1, int(w * 0.25)))
            if rnd.random() < 0.35:
                ba = a + rnd.choice([-1, 1]) * rnd.uniform(0.6, 1.1)
                bx, by = nx, ny
                bw = w * 0.55
                for _ in range(rnd.randint(2, 4)):
                    BL = rnd.uniform(size * 0.03, size * 0.05)
                    ba += rnd.uniform(-0.4, 0.4)
                    ex, ey = bx + math.cos(ba) * BL, by + math.sin(ba) * BL
                    d.line([(bx, by), (ex, ey)], fill=(22, 14, 10, 230), width=max(2, int(bw)))
                    bx, by = ex, ey
                    bw *= 0.75
            x, y = nx, ny
            w = max(3, w * 0.88)
    return img.filter(ImageFilter.GaussianBlur(0.6))

for i, (seed, n) in enumerate([(11, 2), (23, 3), (37, 4)]):
    crack_layer(seed, n).save(os.path.join(OUT, f"crack_{i + 1}.png"))

# 石板(THANK YOU FOR PLAYINGの壁): 暗い石の面+面取り+ざらつき
W, H = 1024, 512
rnd = random.Random(5)
slab = Image.new("RGBA", (W, H), (0, 0, 0, 0))
d = ImageDraw.Draw(slab)
d.rounded_rectangle([0, 0, W - 1, H - 1], radius=28, fill=(58, 60, 74, 255))
noise = Image.new("L", (W // 4, H // 4))
npx = noise.load()
for yy in range(H // 4):
    for xx in range(W // 4):
        npx[xx, yy] = rnd.randint(0, 255)
noise = noise.resize((W, H), Image.BICUBIC).filter(ImageFilter.GaussianBlur(2))
tex = Image.new("RGBA", (W, H), (96, 100, 122, 255))
mask = noise.point(lambda v: int(max(0, v - 110) * 0.55))
slab.paste(tex, (0, 0), Image.composite(mask, Image.new("L", (W, H), 0), slab.split()[3]))
d = ImageDraw.Draw(slab)
d.rounded_rectangle([8, 8, W - 9, H - 9], radius=22, outline=(150, 156, 180, 255), width=6)   # 面取りの明るい縁
d.rounded_rectangle([22, 22, W - 23, H - 23], radius=16, outline=(28, 28, 36, 255), width=5)   # 内側の溝
# 上端の光
for k in range(10):
    d.line([(40, 30 + k), (W - 40, 30 + k)], fill=(200, 205, 225, int(60 * (1 - k / 10))))
slab.save(os.path.join(OUT, "slab.png"))
print("fx done")
