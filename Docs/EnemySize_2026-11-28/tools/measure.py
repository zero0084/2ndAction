# Body-size audit vs goblin. Offline: PNG alpha + .meta PPU + EnemyDefinition.visualScaleMultiplier.
# Body box = fraction of the reference frame's opaque bbox (x0,y0,x1,y1; y from top), set by eye on grid_*.png
# to exclude weapons, wings, tails, effects, pedestals, halos. Metric: H = body height (upright humanoids),
# A = sqrt(opaque body area) (wide/long/winged/blob shapes). Both are always computed and reported.
import json, os, csv, math
import numpy as np
from PIL import Image
OUT = os.path.dirname(os.path.abspath(__file__))
E = {e["enemyId"]: e for e in json.load(open(OUT + "/gather.json", encoding="utf-8"))}
ALPHA = 32

MAPS = {  # from Resources/Encounters/Profile_*.asset + BonusZone.asset + stageIds
    "goblin": "W,S,L", "goblin_elite": "W,S,L", "flying_wyvern": "W,S,L", "irregular_imp": "W,S", "shooter_archer": "W,S,L",
    "heavy_ogre": "W,S,L", "chaser_runner": "W,L", "rusher_runner": "W,L",
    "cave_ant": "C,L", "soldier_ant": "C,L", "cave_hopper": "C,L", "cave_bat": "C,L", "burrow_worm": "C,L",
    "sky_slime": "S,L", "sky_hound": "S,L", "harpy": "S,L", "gargoyle": "S,L", "celestial_knight": "S,L",
    "ancient_sentinel": "S,L", "storm_spirit": "S,L", "sky_hunter": "S,L",
    "treasure_goblin": "B", "mimic": "B", "golden_slime": "B", "card_fairy": "B",
}
BOX = {  # (x0,y0,x1,y1, metric, note)
    "goblin": (0.10, 0, 1, 1, "H", "dagger excluded"),
    "goblin_elite": (0.10, 0, 1, 1, "H", "same art as goblin"),
    "celestial_knight": (0.25, 0.03, 1, 1, "H", "sword + crest wings excluded"),
    "ancient_sentinel": (0, 0, 1, 1, "H", "whole golem"),
    "heavy_ogre": (0, 0, 1, 1, "H", "whole"),
    "shooter_archer": (0.1, 0.05, 0.9, 1, "H", "cape/bow tips excluded"),
    "treasure_goblin": (0, 0, 1, 1, "A", "hunched goblin+sack, sack is part of silhouette (H ratio 0.80)"),
    "gargoyle": (0, 0.05, 0.85, 0.75, "A", "crouching; pedestal + wing tip excluded (H ratio 0.74)"),
    "harpy": (0.05, 0.15, 0.6, 1, "H", "humanoid body, wings excluded"),
    "air_wraith": (0.1, 0.1, 0.8, 0.95, "H", "robed body, flames excluded"),
    "card_fairy": (0, 0.3, 0.7, 0.95, "H", "girl body, wings excluded"),
    "irregular_imp": (0.3, 0.05, 1, 1, "A", "tail/cape excluded"),
    "chaser_runner": (0.2, 0.3, 1, 1, "A", "tail excluded"),
    "rusher_runner": (0.2, 0.3, 1, 1, "A", "tail excluded"),
    "cave_ant": (0, 0, 0.95, 0.6, "A", "legs/antennae excluded"),
    "soldier_ant": (0, 0, 0.95, 0.6, "A", "legs/antennae excluded"),
    "cave_hopper": (0, 0.3, 1, 0.75, "A", "hind-leg spikes/antennae excluded"),
    "burrow_worm": (0, 0, 1, 1, "A", "whole worm"),
    "cave_bat": (0.3, 0.35, 0.7, 1, "A", "torso+head, wings excluded"),
    "flying_wyvern": (0.3, 0.45, 1, 1, "A", "torso+head, wings excluded (flap frame)"),
    "sky_slime": (0, 0, 0.8, 0.9, "A", "splash excluded"),
    "golden_slime": (0, 0.2, 1, 1, "A", "halo/sparkles excluded"),
    "sky_hound": (0, 0, 0.85, 1, "A", "flame tail excluded"),
    "mimic": (0.25, 0, 1, 1, "A", "tongue excluded"),
    "storm_spirit": (0, 0, 0.8, 0.85, "A", "cloud tail excluded"),
    "sky_hunter": (0, 0.4, 0.75, 1, "A", "torso+head+legs, wings excluded"),
    "air_ancient_drone": (0, 0.45, 0.45, 0.95, "A", "core sphere, blade wings excluded"),
    "air_bat_swarm": (0.03, 0.55, 0.55, 1, "A", "torso+head, wings excluded"),
    "air_black_imp": (0.05, 0.3, 0.55, 0.8, "A", "torso+head+limbs, wings/tail excluded"),
    "air_cloud_manta": (0, 0.3, 0.8, 1, "A", "disc body, wing tips/tail excluded"),
    "air_crystal_dragonfly": (0, 0.7, 0.9, 0.95, "A", "head+thorax+abdomen, wings excluded"),
    "air_jellyfish": (0, 0, 0.7, 0.65, "A", "bell+upper tentacles"),
    "air_locust": (0.1, 0.55, 0.95, 0.95, "A", "head+thorax+abdomen, wings/legs excluded"),
    "air_mech_drake": (0, 0.45, 0.85, 0.8, "A", "hull, wings/exhaust excluded"),
    "air_moth": (0.05, 0.6, 0.55, 1, "A", "head+thorax+abdomen, wings excluded"),
    "air_reaper_familiar": (0.1, 0, 0.75, 0.65, "A", "hood+robe, scythe blade excluded"),
    "air_red_eagle": (0, 0.35, 0.65, 1, "A", "head+torso+legs, wings excluded"),
    "air_sand_spirit": (0, 0.1, 0.7, 0.9, "A", "head+core, sand trails excluded"),
    "air_sand_swallow": (0, 0.5, 0.65, 0.8, "A", "head+torso, wings/tail excluded"),
    "air_sky_swallow": (0, 0.55, 0.65, 0.8, "A", "head+torso, wings/tail excluded"),
    "air_sky_wisp": (0.2, 0.45, 0.65, 0.85, "A", "orb body, wings excluded"),
    "air_thread_spider": (0.25, 0, 0.75, 0.65, "A", "abdomen+head, legs excluded"),
    "air_vulture": (0.1, 0.5, 0.65, 0.9, "A", "head+torso, wings excluded"),
}
for k, v in E.items():
    if k.startswith("air_"): MAPS[k] = {"wasteland_road": "W", "natural_cave": "C", "sky_corridor": "S", "last_corridor": "L"}[v["stageIds"][0]]
MAPNAME = {"W": "wasteland_road", "C": "natural_cave", "S": "sky_corridor", "L": "last_corridor", "B": "bonus_zone"}

def ref_frame(e):
    return (e["sets"].get("run") or e["sets"]["sprite"])[0]

def body(i, box, s):
    a = np.asarray(Image.open(i["path"]).convert("RGBA"))[:, :, 3] > ALPHA
    x0, y0, x1, y1 = i["bbox"]; W, H = x1 - x0, y1 - y0
    bx0, by0, bx1, by1 = int(x0 + box[0] * W), int(y0 + box[1] * H), int(x0 + box[2] * W), int(y0 + box[3] * H)
    sub = a[by0:by1, bx0:bx1]
    ys, xs = np.nonzero(sub)
    k = s / i["ppu"]
    return dict(H=(ys.max() - ys.min() + 1) * k, W=(xs.max() - xs.min() + 1) * k, A=math.sqrt(sub.sum()) * k,
                fullH=H * k, fullW=W * k)

def set_size(i, s):  # pose-robust per-frame size: sqrt(total opaque area) in world units
    a = np.asarray(Image.open(i["path"]).convert("RGBA"))[:, :, 3] > ALPHA
    return math.sqrt(a.sum()) * s / i["ppu"]

rows = []
for eid, e in E.items():
    if eid not in BOX: continue
    s = float(e["visualScaleMultiplier"])
    b = body(ref_frame(e), BOX[eid], s)
    # consistency: median sqrt-area per frame set; compare displayed sets (sprite only if it is actually shown)
    per = {}
    for k, v in e["sets"].items():
        vals = [set_size(i, s) for i in v if not i.get("missing")]
        if vals: per[k] = float(np.median(vals))
    rows.append(dict(id=eid, e=e, s=s, b=b, per=per))

G = next(r for r in rows if r["id"] == "goblin")["b"]
for r in rows:
    m = BOX[r["id"]][4]
    r["metric"] = m
    r["ratio"] = r["b"][m] / G[m]
    r["ratioH"] = r["b"]["H"] / G["H"]; r["ratioA"] = r["b"]["A"] / G["A"]
small = [r for r in rows if r["ratio"] < 0.97]
rmin = min(r["ratio"] for r in small)
BAND = 0.06  # smallest -> 1.00x goblin, the one just under the baseline -> 1.06x
for r in rows:
    if r in small:
        target = 1.0 + BAND * (r["ratio"] - rmin) / (0.97 - rmin)
        r["k"] = target / r["ratio"]
        r["newScale"] = round(r["s"] * r["k"], 2)
        r["target"] = target
    else:
        r["k"] = 1.0; r["newScale"] = r["s"]; r["target"] = r["ratio"]
json.dump([dict(id=r["id"], s=r["s"], newScale=r["newScale"], k=r["k"], metric=r["metric"], ratio=r["ratio"], b=r["b"], per=r["per"]) for r in rows],
          open(OUT + "/measure.json", "w"), indent=1)

with open(OUT + "/enemy_size_audit.csv", "w", newline="", encoding="utf-8") as f:
    w = csv.writer(f)
    w.writerow(["enemyId", "maps", "ground_air", "scale_source", "visualScale_now", "metric", "body_H_u", "body_W_u", "sqrtArea_u",
                "ratio_vs_goblin", "ratioH", "ratioA", "frameset_min_u", "frameset_max_u", "frameset_max_over_min", "frameset_detail",
                "proposed_visualScale", "after_ratio", "body_note"])
    for r in sorted(rows, key=lambda r: r["ratio"]):
        e = r["e"]; per = r["per"]
        shown = {k: v for k, v in per.items() if not (e["movementType"] == "1" and k == "sprite" and "run" in per)}
        mn, mx = min(shown.values()), max(shown.values())
        src = f"Resources/Enemies/{r['id']}.asset visualScaleMultiplier x PNG .meta PPU"
        w.writerow([r["id"], ";".join(MAPNAME[m] for m in MAPS[r["id"]].split(",")), "air" if e["movementType"] == "1" else "ground", src,
                    r["s"], r["metric"], round(r["b"]["H"], 3), round(r["b"]["W"], 3), round(r["b"]["A"], 3), round(r["ratio"], 3),
                    round(r["ratioH"], 3), round(r["ratioA"], 3), round(mn, 3), round(mx, 3), round(mx / mn, 2),
                    " ".join(f"{k}={v:.2f}" for k, v in per.items()), r["newScale"], round(r["target"], 3), BOX[r["id"]][5]])
for r in sorted(rows, key=lambda r: r["ratio"]):
    per = r["per"]
    print(f"{r['id']:22s} {MAPS[r['id']]:6s} s={r['s']:.2f} {r['metric']} H={r['b']['H']:.2f} A={r['b']['A']:.2f} ratio={r['ratio']:.2f} (H {r['ratioH']:.2f} A {r['ratioA']:.2f}) -> scale {r['newScale']}  sets " +
          " ".join(f"{k}={v:.2f}" for k, v in per.items()))
print("goblin ref", G)
