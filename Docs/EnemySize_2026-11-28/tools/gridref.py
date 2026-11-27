# Reference frame of each enemy cropped to its opaque bbox with a 10% grid, for defining body boxes.
import json, os, sys
from PIL import Image, ImageDraw
OUT = os.path.dirname(os.path.abspath(__file__))
E = json.load(open(OUT + "/gather.json", encoding="utf-8"))
ids = sys.argv[2:]; name = sys.argv[1]
S = 330; C = 4
sel = [e for e in E if e["enemyId"] in ids]
rows = (len(sel) + C - 1) // C
img = Image.new("RGBA", (S * C, (S + 18) * rows), (40, 40, 50, 255))
for n, e in enumerate(sel):
    i = (e["sets"].get("run") or e["sets"]["sprite"])[0]
    im = Image.open(i["path"]).convert("RGBA").crop(tuple(i["bbox"]))
    f = (S - 20) / max(im.width, im.height)
    im = im.resize((int(im.width * f), int(im.height * f)), Image.LANCZOS)
    cell = Image.new("RGBA", (S, S + 18), (40, 40, 50, 255)); d = ImageDraw.Draw(cell)
    cell.alpha_composite(im, (10, 28)); d = ImageDraw.Draw(cell)
    for k in range(11):
        c = (255, 60, 60, 255) if k % 5 == 0 else (120, 120, 140, 255)
        d.line([10 + im.width * k / 10, 28, 10 + im.width * k / 10, 28 + im.height], fill=c)
        d.line([10, 28 + im.height * k / 10, 10 + im.width, 28 + im.height * k / 10], fill=c)
    d.text((4, 4), e["enemyId"], fill=(255, 255, 0, 255))
    img.alpha_composite(cell, ((n % C) * S, (n // C) * (S + 18)))
img.convert("RGB").save(f"{OUT}/grid_{name}.png")
