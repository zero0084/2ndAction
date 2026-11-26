import sys, random
from PIL import Image
random.seed(7)
src, dst = sys.argv[1], sys.argv[2]
im = Image.open(src).convert('RGBA'); px = im.load()
Y0, Y1 = 478, 558            # 帯の内側(上の金の線の下〜下の金の線の上)
def med(vals):
    vals = sorted(vals); return vals[len(vals)//2]
rows = {}
for y in range(Y0, Y1 + 1):
    L = [px[x, y] for x in range(819, 843)]
    R = [px[x, y] for x in range(1294, 1312)]
    rows[y] = (tuple(med([p[c] for p in L]) for c in range(3)), tuple(med([p[c] for p in R]) for c in range(3)))
def fill(x, y):
    l, r = rows[y]; t = max(0.0, min(1.0, (x - 738) / (1369 - 738)))
    c = [l[i] + (r[i] - l[i]) * t + random.gauss(0, 1.2) for i in range(3)]
    a = px[x, y][3]
    px[x, y] = tuple(max(0, min(255, int(round(v)))) for v in c) + (a,)
for x in range(722, 1386):
    if 818 <= x <= 1312: ys = range(Y0, Y1 + 1)        # 文字の部分は帯の高さ全体
    else: ys = range(504, 528)                           # 両側の飾りは飾りの高さだけ
    for y in ys: fill(x, y)
im.save(dst)
