# Grid overview: first move frame of each enemy, current world scale, with 0.25u grid lines, for eyeballing body extents.
import json, os
from PIL import Image, ImageDraw
OUT = os.path.dirname(os.path.abspath(__file__))
E = json.load(open(OUT + "/gather.json", encoding="utf-8"))
PXU = 120; C = 9; cw, ch = int(2.8 * PXU), int(2.8 * PXU)
rows = (len(E) + C - 1) // C
img = Image.new("RGBA", (cw * C, (ch + 16) * rows), (70, 70, 80, 255))
for n, e in enumerate(E):
    s = float(e["visualScaleMultiplier"])
    i = (e["sets"].get("run") or e["sets"]["sprite"])[0]
    im = Image.open(i["path"]).convert("RGBA")
    x0, y0, x1, y1 = i["bbox"]; im = im.crop((x0, y0, x1, y1))
    f = PXU * s / i["ppu"]
    im = im.resize((max(1, int(im.width * f)), max(1, int(im.height * f))), Image.LANCZOS)
    cx, cy = (n % C) * cw, (n // C) * (ch + 16)
    cell = Image.new("RGBA", (cw, ch + 16), (70, 70, 80, 255)); d = ImageDraw.Draw(cell)
    gy = ch - 10
    for k in range(12): d.line([0, gy - k * PXU / 4, cw, gy - k * PXU / 4], fill=(95, 95, 110, 255) if k % 4 else (140, 140, 160, 255))
    cell.alpha_composite(im, (max(0, (cw - im.width) // 2), max(0, gy - im.height)))
    d.text((3, 3), f"{e['enemyId']} {im.height/PXU:.2f}u", fill=(255, 255, 0, 255))
    img.alpha_composite(cell, (cx, cy))
img.convert("RGB").save(OUT + "/overview_current.png")
