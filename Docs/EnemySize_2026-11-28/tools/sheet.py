# Contact sheet: every frame set of each enemy at its current world scale, pivot on a line.
import json, os, sys
from PIL import Image, ImageDraw
OUT = os.path.dirname(os.path.abspath(__file__))
E = json.load(open(OUT + "/gather.json", encoding="utf-8"))
PXU = 90  # pixels per world unit
only = sys.argv[1:] or None
for e in E:
    if only and e["enemyId"] not in only: continue
    s = float(e["visualScaleMultiplier"])
    items = []
    for k, v in e["sets"].items():
        for j, i in enumerate(v[:5]):
            if i.get("missing"): continue
            items.append((f"{k}[{j}]", i))
    cellW, cellH = int(3.0 * PXU), int(3.2 * PXU)
    img = Image.new("RGBA", (cellW * len(items), cellH + 20), (60, 60, 70, 255))
    d = ImageDraw.Draw(img)
    for n, (lab, i) in enumerate(items):
        im = Image.open(i["path"]).convert("RGBA")
        f = PXU * s / i["ppu"]
        w, h = max(1, int(im.width * f)), max(1, int(im.height * f))
        im2 = im.resize((w, h), Image.LANCZOS)
        px, py = i["pivotEff"]
        gx, gy = n * cellW + cellW // 2, int(cellH * 0.75)
        ox, oy = int(gx - px * w), int(gy - (1 - py) * h)
        img.alpha_composite(im2, (max(0, ox), max(0, oy)) if ox >= 0 and oy >= 0 else (0, 0)) if False else None
        canvas = Image.new("RGBA", img.size, (0, 0, 0, 0)); canvas.paste(im2, (ox, oy)); img = Image.alpha_composite(img, canvas); d = ImageDraw.Draw(img)
        x0, y0, x1, y1 = i["bbox"]
        d.rectangle([ox + x0 * f, oy + y0 * f, ox + x1 * f, oy + y1 * f], outline=(255, 80, 80, 255))
        d.line([n * cellW, gy, (n + 1) * cellW, gy], fill=(120, 255, 120, 255))
        for yy in range(0, cellH, PXU // 2): d.line([n * cellW, gy - yy, n * cellW + 6, gy - yy], fill=(200, 200, 200, 255))
        d.text((n * cellW + 4, cellH + 4), lab, fill=(255, 255, 255, 255))
    img.convert("RGB").save(f"{OUT}/sheet_{e['enemyId']}.png")
print("ok")
