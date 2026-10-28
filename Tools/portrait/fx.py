# 立ち絵のエフェクトの層(2026-10-28、エステルと相談した「作り方B」)
#  python fx.py <id> <キャラの立ち絵.png(透明背景)> <出力.png> [--seed N]
#  ・キャラの画素は変えない。エフェクトはキャラの後ろ(外側の光・粒・軌跡)に描き、前には薄い粒だけ
#  ・顔(人物の上端から 22% まで)と、キャラの内側には前の粒を置かない = 顔/武器/衣装を隠さない
#  ・強さは3段階(エステル): 強=魔法使い/巫女/竜人、中=戦う人たち、弱=ルカ/ペンタトゥットゥルー星人
import sys, argparse, math, random
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ap = argparse.ArgumentParser()
ap.add_argument("id"); ap.add_argument("src"); ap.add_argument("out")
ap.add_argument("--seed", type=int, default=7)
ap.add_argument("--weapon", action="append", default=[], help="x0,y0,x1,y1 = 武器(柄→切っ先)。闘気/光/斬撃の軌跡をここに沿わせる")
ap.add_argument("--trail", action="append", default=[], help="tx,ty,cx,cy,ex,ey = 斬撃の軌跡(切っ先→制御点→終わり)")
a = ap.parse_args()
rnd = random.Random(a.seed)

# 色1(外側の光)/色2(粒)/粒の形/強さ/粒の場所(feet=足元, around=周り, weapon=武器側(左右), rise=上へ昇る)/特別な形
P = {
    "swordsman":    dict(glow=(150, 10, 25), glow2=(40, 0, 10), dot=(255, 120, 40), shape="ember", k=1.0, where="feet", extra="aura"),
    "noble_lady":   dict(glow=(255, 240, 200), glow2=(255, 220, 150), dot=(255, 245, 210), shape="spark", k=1.0, where="around", extra="halo"),
    "dual_blade":   dict(glow=(120, 220, 255), glow2=(60, 160, 255), dot=(200, 245, 255), shape="streak", k=1.0, where="around", extra="arcs"),
    "gunslinger":   dict(glow=(255, 110, 40), glow2=(120, 40, 20), dot=(255, 170, 90), shape="ember", k=0.8, where="weapon", extra="smoke"),
    "dragon_lancer":dict(glow=(40, 220, 190), glow2=(20, 120, 110), dot=(170, 255, 235), shape="spark", k=0.9, where="rise", extra="bolt"),
    "archer":       dict(glow=(80, 230, 140), glow2=(30, 120, 70), dot=(190, 255, 210), shape="leaf", k=0.9, where="around", extra="wind"),
    "mage":         dict(glow=(140, 110, 255), glow2=(70, 50, 200), dot=(210, 200, 255), shape="spark", k=1.25, where="around", extra="circle"),
    "fighter":      dict(glow=(255, 90, 30), glow2=(140, 30, 10), dot=(255, 170, 70), shape="ember", k=1.0, where="feet", extra="shock"),
    "ninja":        dict(glow=(200, 20, 40), glow2=(80, 0, 15), dot=(255, 110, 120), shape="streak", k=0.9, where="around", extra="afterimage"),
    "miko":         dict(glow=(255, 245, 225), glow2=(230, 60, 60), dot=(255, 230, 200), shape="petal", k=1.25, where="around", extra="halo"),
    "vampire":      dict(glow=(170, 0, 30), glow2=(60, 0, 10), dot=(220, 40, 60), shape="bat", k=1.0, where="feet", extra="mist"),
    "dragonkin":    dict(glow=(255, 120, 30), glow2=(170, 40, 10), dot=(255, 200, 90), shape="ember", k=1.25, where="rise", extra="heat"),
    "luka":         dict(glow=(255, 225, 140), glow2=(255, 190, 90), dot=(255, 235, 150), shape="paw", k=0.6, where="feet", extra="stars"),
    "pentatuttulu": dict(glow=(190, 220, 255), glow2=(255, 200, 240), dot=(255, 255, 255), shape="bubble", k=0.6, where="around", extra="stars"),
}[a.id]

ch = Image.open(a.src).convert("RGBA")
W, H = ch.size
A = np.array(ch)[:, :, 3].astype(float) / 255.0
bb = ch.getbbox(); x0, y0, x1, y1 = bb
fh = y1 - y0
faceY = y0 + fh * 0.22
k = P["k"]

def col(c, al): return (int(c[0]), int(c[1]), int(c[2]), int(max(0, min(255, al))))

back = Image.new("RGBA", (W, H), (0, 0, 0, 0))   # キャラの後ろ
front = Image.new("RGBA", (W, H), (0, 0, 0, 0))  # キャラの前(薄い粒だけ)

# 1) 外側の光(輪郭を太らせてぼかす)= 立ち絵が背景から浮く
m = Image.fromarray((A * 255).astype(np.uint8), "L")
for rad, al, c in ((26, 0.55, P["glow2"]), (12, 0.75, P["glow"])):
    g = m.filter(ImageFilter.MaxFilter(9)).filter(ImageFilter.GaussianBlur(rad))
    ga = np.array(g).astype(float) / 255.0 * al * k
    layer = np.zeros((H, W, 4), np.uint8); layer[:, :, 0], layer[:, :, 1], layer[:, :, 2] = c
    layer[:, :, 3] = np.clip(ga * 255, 0, 255).astype(np.uint8)
    back.alpha_composite(Image.fromarray(layer, "RGBA"))

d = ImageDraw.Draw(back)
df = ImageDraw.Draw(front)

def inside(x, y): return 0 <= x < W and 0 <= y < H and A[int(y), int(x)] > 0.05

# 2) 特別な形(背中側)
ex = P["extra"]
cx = (x0 + x1) / 2
if ex == "halo":
    r = fh * 0.13; hy = y0 + fh * 0.07; hx = cx + (x1 - cx) * 0.05
    hx = np.average(np.arange(W), weights=A[int(y0 + fh * 0.06)] + 1e-6)
    for w_, al in ((10, 50), (5, 110), (2, 200)):
        d.ellipse([hx - r, hy - r * 0.32, hx + r, hy + r * 0.32], outline=col(P["glow"], al * k), width=w_)
elif ex == "circle":
    r = fh * 0.2; ccx, ccy = (x0 + fh * 0.12, y0 + fh * 0.36)
    for rr, al in ((r, 140), (r * 0.78, 110), (r * 0.5, 90)):
        d.ellipse([ccx - rr, ccy - rr * 0.35, ccx + rr, ccy + rr * 0.35], outline=col(P["glow"], al * k), width=3)
    for i in range(12):
        t = i / 12 * 2 * math.pi
        d.text((ccx + math.cos(t) * r * 0.9, ccy + math.sin(t) * r * 0.31), "✦", fill=col(P["dot"], 200 * k))
elif ex == "arcs" and False:
    for i, (sx, sy, rr, a0, a1) in enumerate(((cx, y0 + fh * 0.42, fh * 0.42, 190, 330), (cx, y0 + fh * 0.5, fh * 0.32, 20, 160))):
        for w_, al in ((14, 40), (6, 110), (2, 230)):
            d.arc([sx - rr, sy - rr * 0.55, sx + rr, sy + rr * 0.55], a0, a1, fill=col(P["glow"], al * k), width=w_)
elif ex == "aura":
    for i in range(24):
        px = rnd.uniform(x0, x1); py = rnd.uniform(y0 + fh * 0.25, y1)
        if not inside(px, py): continue
        L = rnd.uniform(30, 90)
        d.line([(px, py), (px + rnd.uniform(-10, 10), py - L)], fill=col(P["glow"], 70 * k), width=int(rnd.uniform(4, 10)))
elif ex == "shock":
    fy = y1 - 6
    for rr, al in ((fh * 0.28, 90), (fh * 0.2, 130)):
        d.ellipse([cx - rr, fy - rr * 0.12, cx + rr, fy + rr * 0.12], outline=col(P["glow"], al * k), width=4)
elif ex in ("mist", "smoke"):
    mist = Image.new("RGBA", (W, H), (0, 0, 0, 0)); md = ImageDraw.Draw(mist)
    for i in range(18):
        px = rnd.uniform(x0 - 30, x1 + 30); py = rnd.uniform(y1 - fh * 0.25, y1 + 10) if ex == "mist" else rnd.uniform(y0 + fh * 0.3, y0 + fh * 0.6)
        rr = rnd.uniform(25, 60)
        md.ellipse([px - rr, py - rr * 0.6, px + rr, py + rr * 0.6], fill=col(P["glow2"] if ex == "mist" else (120, 110, 105), 70 * k))
    back.alpha_composite(mist.filter(ImageFilter.GaussianBlur(14)))
elif ex == "wind":
    for i in range(7):
        sy = rnd.uniform(y0 + fh * 0.25, y1 - fh * 0.1); sx = rnd.uniform(x0 - 20, cx)
        d.arc([sx, sy - 18, sx + rnd.uniform(140, 260), sy + 18], 200, 340, fill=col(P["glow"], 120 * k), width=2)
elif ex == "afterimage":
    sh = ch.copy(); arr = np.array(sh); arr[:, :, 0], arr[:, :, 1], arr[:, :, 2] = P["glow"]; arr[:, :, 3] = (arr[:, :, 3] * 0.22).astype(np.uint8)
    ghost = Image.fromarray(arr, "RGBA")
    for off, al in ((-34, 0.9), (-64, 0.5)):
        g2 = Image.new("RGBA", (W, H)); g2.alpha_composite(ghost, (0, 0)); g2 = g2.transform((W, H), Image.AFFINE, (1, 0, -off, 0, 1, 0))
        ga = np.array(g2); ga[:, :, 3] = (ga[:, :, 3] * al).astype(np.uint8); back.alpha_composite(Image.fromarray(ga, "RGBA"))
elif ex == "bolt":
    for i in range(3):
        px, py = rnd.uniform(x0, x0 + fh * 0.25), rnd.uniform(y0, y0 + fh * 0.3); pts = [(px, py)]
        for j in range(6): px += rnd.uniform(-18, 18); py += rnd.uniform(14, 30); pts.append((px, py))
        d.line(pts, fill=col(P["glow"], 160 * k), width=2)
elif ex == "heat":
    for i in range(14):
        px = rnd.uniform(x0, x1); py = rnd.uniform(y0 + fh * 0.2, y1)
        d.arc([px - 30, py - 50, px + 30, py + 50], 240, 300, fill=col(P["glow"], 90 * k), width=3)
elif ex == "stars":
    pass

# 2b) 武器に沿った闘気/光(後ろ。武器そのものは前のキャラの画素なので隠れない)
for w in a.weapon:
    wx0, wy0, wx1, wy1 = [float(v) for v in w.split(",")]
    aura = Image.new("RGBA", (W, H), (0, 0, 0, 0)); ad = ImageDraw.Draw(aura)
    for wid, c, al in ((90, P["glow2"], 150), (52, P["glow"], 190), (22, P["dot"], 170)):
        ad.line([(wx0, wy0), (wx1, wy1)], fill=col(c, al * k), width=wid)
    if P["shape"] == "ember":  # 闘気の炎の舌(上へ揺らぐ)
        for i in range(26):
            t = rnd.uniform(0.15, 1.0); px = wx0 + (wx1 - wx0) * t; py = wy0 + (wy1 - wy0) * t
            L = rnd.uniform(40, 110)
            ad.line([(px, py), (px + rnd.uniform(-18, 18), py - L)], fill=col(rnd.choice((P["glow"], P["dot"])), rnd.uniform(140, 220) * k), width=int(rnd.uniform(7, 16)))
    back.alpha_composite(aura.filter(ImageFilter.GaussianBlur(10)))
    back.alpha_composite(aura.filter(ImageFilter.GaussianBlur(3)))
    # 刃の上にも薄く(武器が見える濃さ = 25%)。光をまとって見える
    fa = np.array(aura.filter(ImageFilter.GaussianBlur(6))); fa[:, :, 3] = (fa[:, :, 3] * 0.25).astype(np.uint8)
    front.alpha_composite(Image.fromarray(fa, "RGBA"))
for tr in a.trail:
    tx, ty, qx, qy, ex_, ey_ = [float(v) for v in tr.split(",")]
    trl = Image.new("RGBA", (W, H), (0, 0, 0, 0)); td = ImageDraw.Draw(trl)
    pts = [((1 - t) ** 2 * tx + 2 * (1 - t) * t * qx + t * t * ex_, (1 - t) ** 2 * ty + 2 * (1 - t) * t * qy + t * t * ey_) for t in np.linspace(0, 1, 40)]
    for i in range(len(pts) - 1):
        f = 1 - i / (len(pts) - 1)
        for wid, c, al in ((int(44 * f) + 2, P["glow2"], 110), (int(20 * f) + 1, P["glow"], 200), (max(1, int(6 * f)), (255, 255, 255), 240)):
            td.line([pts[i], pts[i + 1]], fill=col(c, al * f * k), width=wid)
    back.alpha_composite(trl.filter(ImageFilter.GaussianBlur(2)))

# 3) 粒(後ろに多め、前は顔より下・キャラの外側に少しだけ)
def particle(dr, x, y, s, al):
    c = col(P["dot"], al)
    sh = P["shape"]
    if sh == "ember": dr.ellipse([x - s, y - s, x + s, y + s], fill=c); dr.ellipse([x - s * 2.2, y - s * 2.2, x + s * 2.2, y + s * 2.2], fill=col(P["glow"], al * 0.25))
    elif sh == "spark":
        dr.line([(x - s * 2.5, y), (x + s * 2.5, y)], fill=c, width=max(1, int(s * 0.6))); dr.line([(x, y - s * 2.5), (x, y + s * 2.5)], fill=c, width=max(1, int(s * 0.6)))
        dr.ellipse([x - s * 0.7, y - s * 0.7, x + s * 0.7, y + s * 0.7], fill=c)
    elif sh == "streak": dr.line([(x - s * 6, y + s * 1.5), (x + s * 6, y - s * 1.5)], fill=c, width=max(1, int(s * 0.5)))
    elif sh == "leaf": dr.ellipse([x - s * 2, y - s * 0.8, x + s * 2, y + s * 0.8], fill=c)
    elif sh == "petal": dr.ellipse([x - s * 1.6, y - s, x + s * 1.6, y + s], fill=c); dr.ellipse([x - s * 0.6, y - s * 0.4, x + s * 0.6, y + s * 0.4], fill=col(P["glow2"], al))
    elif sh == "bat": dr.polygon([(x - s * 3, y - s), (x - s, y), (x, y - s * 0.6), (x + s, y), (x + s * 3, y - s), (x, y + s)], fill=col(P["glow2"], al))
    elif sh == "paw":
        dr.ellipse([x - s * 1.2, y - s * 0.6, x + s * 1.2, y + s * 1.4], fill=c)
        for dx_, dy_ in ((-1.4, -1.4), (-0.5, -2), (0.5, -2), (1.4, -1.4)): dr.ellipse([x + dx_ * s - s * 0.45, y + dy_ * s - s * 0.45, x + dx_ * s + s * 0.45, y + dy_ * s + s * 0.45], fill=c)
    elif sh == "bubble": dr.ellipse([x - s * 2, y - s * 2, x + s * 2, y + s * 2], outline=c, width=2); dr.ellipse([x - s * 1.2, y - s * 1.4, x - s * 0.4, y - s * 0.6], fill=c)

where = P["where"]
def spot():
    for _ in range(40):
        if where == "feet": x, y = rnd.uniform(x0 - 20, x1 + 20), rnd.uniform(y1 - fh * 0.28, y1 + 4)
        elif where == "rise": x, y = rnd.uniform(x0 - 20, x1 + 20), rnd.uniform(y0 + fh * 0.1, y1)
        elif where == "weapon": x, y = rnd.choice((rnd.uniform(x0 - 30, x0 + fh * 0.2), rnd.uniform(x1 - fh * 0.2, x1 + 30))), rnd.uniform(y0 + fh * 0.15, y0 + fh * 0.65)
        else: x, y = rnd.uniform(x0 - 40, x1 + 40), rnd.uniform(y0, y1)
        if 4 < x < W - 4 and 4 < y < H - 4: return x, y
    return cx, y1
n_back = int(60 * k); n_front = int(14 * k)
for i in range(n_back):
    x, y = spot(); particle(d, x, y, rnd.uniform(1.5, 3.8), rnd.uniform(110, 230) * min(1, k))
if P["extra"] == "stars":
    for i in range(int(14 * k) + 6):
        x, y = spot(); s = rnd.uniform(3, 6)
        d.polygon([(x, y - s * 2), (x + s * 0.5, y - s * 0.5), (x + s * 2, y), (x + s * 0.5, y + s * 0.5), (x, y + s * 2), (x - s * 0.5, y + s * 0.5), (x - s * 2, y), (x - s * 0.5, y - s * 0.5)], fill=col((255, 240, 160), 220))
placed = 0
for i in range(n_front * 6):
    if placed >= n_front: break
    x, y = spot()
    if y < faceY or inside(x, y): continue  # 顔の高さ・キャラの上には置かない
    particle(df, x, y, rnd.uniform(1.2, 2.6), rnd.uniform(90, 170)); placed += 1

out = Image.new("RGBA", (W, H), (0, 0, 0, 0))
out.alpha_composite(back.filter(ImageFilter.GaussianBlur(0.6)))
out.alpha_composite(ch)
out.alpha_composite(front)
# 画面の端で切れないように: 端の 6px は消す
o = np.array(out); o[:, :6, 3] = 0; o[:, -6:, 3] = 0; o[:6, :, 3] = 0
Image.fromarray(o, "RGBA").save(a.out)
print("fx", a.id, "front particles", placed)
