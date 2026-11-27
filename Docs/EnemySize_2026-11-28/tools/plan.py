# Final plan (scale + collider per enemy), comparison images per map, CSV/markdown.
import json, os, csv, re
import numpy as np
from PIL import Image, ImageDraw, ImageFont
OUT = os.path.dirname(os.path.abspath(__file__))
E = {e["enemyId"]: e for e in json.load(open(OUT + "/gather.json", encoding="utf-8"))}
M = {m["id"]: m for m in json.load(open(OUT + "/measure.json"))}
C = json.load(open(OUT + "/collider.json"))
import sys; sys.path.insert(0, OUT); import collider as CL

ROOT = "C:/GameProject/2ndAction/Unity/2ndAction/Assets"
plan = {}
for eid, m in M.items():
    if m["k"] == 1.0: continue
    e = E[eid]; c = C[eid]
    p = dict(id=eid, asset=e["asset"], oldScale=m["s"], newScale=m["newScale"], oldColScale=e["colScale"], oldColOffset=e["colOffset"],
             newColScale=[round(v, 4) for v in c["newColScale"]], newColOffset=[round(v, 4) for v in c["newColOffset"]], sprite=None)
    k = m["newScale"] / m["s"]
    if eid == "flying_wyvern":
        # show/collide on the flap frame (the only art the bird actually displays); collider = 60% x 60% of the scaled flap silhouette
        f0 = e["sets"]["run"][0]
        b = CL.opaque_rect(f0, 0)  # tight-mesh bounds ~ alpha>0 bbox
        v = CL.opaque_rect(f0, 32)
        fw, fh = b[1] - b[0], b[3] - b[2]; bc = ((b[0] + b[1]) / 2, (b[2] + b[3]) / 2)
        vs = m["newScale"]; vc = ((v[0] + v[1]) / 2 * vs, (v[2] + v[3]) / 2 * vs)
        tw, th = (v[1] - v[0]) * vs * 0.6, (v[3] - v[2]) * vs * 0.6
        p["newColScale"] = [round(tw / fw, 4), round(th / fh, 4)]
        p["newColOffset"] = [round((vc[0] - bc[0]) / fw, 4), round((vc[1] - bc[1]) / fh, 4)]
        p["sprite"] = f0["path"]
        p["colWorld"] = [vc[0] - tw / 2, vc[0] + tw / 2, vc[1] - th / 2, vc[1] + th / 2]
    elif eid == "cave_bat":
        # existing offset bug: collider from the foot-pivot static sprite, flap frames are centre-pivot -> re-centre on the flap
        b = c["bounds"]; vis = c["nvis"]; fh = b[3] - b[2]; full_c = (b[2] + b[3]) / 2
        p["newColScale"] = [round(k, 4), round(k, 4)]
        p["newColOffset"] = [0.0, round(((vis[2] + vis[3]) / 2 - full_c) / fh, 4)]
        p["colWorld"] = CL.collider(b, p["newColScale"], p["newColOffset"])
    else:
        p["colWorld"] = c["ncol"]
    plan[eid] = p
json.dump(plan, open(OUT + "/plan.json", "w"), indent=1)

# ---------------- images ----------------
MAPS = {"wasteland_road": "W", "natural_cave": "C", "sky_corridor": "S", "last_corridor": "L", "bonus_zone": "B"}
rows = list(csv.DictReader(open(OUT + "/enemy_size_audit.csv", encoding="utf-8")))
PXU = 110
try: font = ImageFont.truetype("arial.ttf", 13); big = ImageFont.truetype("arial.ttf", 18)
except Exception: font = big = ImageFont.load_default()

def frame_for(eid, after):
    e = E[eid]
    if after and eid == "flying_wyvern": return e["sets"]["run"][0]
    return (e["sets"].get("run") or e["sets"]["sprite"])[0]

def render(mapname, after):
    ids = [r["enemyId"] for r in rows if mapname in r["maps"].split(";") and r["enemyId"] != "goblin"]
    ids = ["goblin"] + sorted(ids, key=lambda i: M[i]["ratio"])
    tiles = []
    for eid in ids:
        i = frame_for(eid, after); s = (M[eid]["newScale"] if after else M[eid]["s"])
        im = Image.open(i["path"]).convert("RGBA").crop(tuple(i["bbox"]))
        f = PXU * s / i["ppu"]; im = im.resize((max(1, round(im.width * f)), max(1, round(im.height * f))), Image.LANCZOS)
        tiles.append((eid, im))
    W = sum(max(t[1].width, 150) + 16 for t in tiles) + 20
    Hmax = max(t[1].height for t in tiles)
    H = Hmax + 120
    img = Image.new("RGBA", (W, H), (52, 56, 66, 255)); d = ImageDraw.Draw(img)
    gy = Hmax + 40
    gob = PXU * M["goblin"]["b"]["H"] if False else None
    # goblin height reference line
    gH = tiles[0][1].height
    d.line([0, gy - gH, W, gy - gH], fill=(255, 210, 0, 120), width=1)
    d.text((6, gy - gH - 16), "goblin height", fill=(255, 210, 0, 255), font=font)
    x = 20
    for eid, im in tiles:
        cw = max(im.width, 150)
        img.alpha_composite(im, (x + (cw - im.width) // 2, gy - im.height)); d = ImageDraw.Draw(img)
        m = M[eid]; r = m["ratio"] * ((m["newScale"] / m["s"]) if after else 1)
        tag = "air" if E[eid]["movementType"] == "1" else "gnd"
        col = (255, 120, 120, 255) if r < 0.97 else (220, 255, 220, 255)
        d.text((x, gy + 8), eid, fill=(255, 255, 255, 255), font=font)
        d.text((x, gy + 26), f"{tag} x{r:.2f} ({m['metric']})", fill=col, font=font)
        d.text((x, gy + 44), f"scale {m['newScale'] if after else m['s']}", fill=(200, 200, 200, 255), font=font)
        x += cw + 16
    d.line([0, gy, W, gy], fill=(120, 255, 120, 255), width=2)
    d.text((10, 8), f"{mapname} - {'AFTER (proposed)' if after else 'BEFORE (current)'} - first move frame, world scale {PXU}px/u, bottom-aligned; x = body ratio vs goblin (H=height, A=sqrt area)", fill=(255, 255, 255, 255), font=big)
    img.convert("RGB").save(f"{OUT}/compare_{mapname}_{'after' if after else 'before'}.png")

for mp in MAPS:
    render(mp, False); render(mp, True)

# ---------------- CSV with proposals + markdown ----------------
for r in rows:
    p = plan.get(r["enemyId"])
    r["proposed_visualScale"] = p["newScale"] if p else r["visualScale_now"]
    r["new_bodyColliderScale"] = f"{p['newColScale'][0]};{p['newColScale'][1]}" if p else ""
    r["new_bodyColliderOffset"] = f"{p['newColOffset'][0]};{p['newColOffset'][1]}" if p else ""
fields = list(rows[0].keys())
with open(OUT + "/enemy_size_audit.csv", "w", newline="", encoding="utf-8") as f:
    w = csv.DictWriter(f, fields); w.writeheader(); w.writerows(rows)
with open(OUT + "/enemy_size_audit.md", "w", encoding="utf-8") as f:
    f.write("| enemy | maps | g/a | scale now | metric | body H u | body W u | sqrtA u | ratio | sets min-max u (max/min) | proposed scale | after |\n|---|---|---|---|---|---|---|---|---|---|---|---|\n")
    for r in rows:
        f.write(f"| {r['enemyId']} | {r['maps'].replace('_road','').replace('_corridor','').replace('natural_','')} | {r['ground_air']} | {r['visualScale_now']} | {r['metric']} | {r['body_H_u']} | {r['body_W_u']} | {r['sqrtArea_u']} | {r['ratio_vs_goblin']} | {r['frameset_min_u']}-{r['frameset_max_u']} ({r['frameset_max_over_min']}) | {r['proposed_visualScale']} | {r['after_ratio']} |\n")
for k, p in plan.items():
    print(k, p["oldScale"], "->", p["newScale"], "col", p["oldColScale"], p["oldColOffset"], "->", p["newColScale"], p["newColOffset"], "world", [round(v, 2) for v in p["colWorld"]])
