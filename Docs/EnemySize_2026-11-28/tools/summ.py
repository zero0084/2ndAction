import json, os
OUT = os.path.dirname(os.path.abspath(__file__))
E = json.load(open(OUT + "/gather.json", encoding="utf-8"))
for e in E:
    s = float(e["visualScaleMultiplier"])
    print(f"== {e['enemyId']} scale={s} mv={e['movementType']} beh={e['behaviorKind']} stages={e['stageIds']} col={e['colScale']} off={e['colOffset']}")
    for k, v in e["sets"].items():
        hs = []; ws = []; info = set()
        for i in v:
            if i.get("missing"): hs.append(None); continue
            x0, y0, x1, y1 = [int(t) for t in i["bbox"]]
            hs.append(round((y1 - y0) / i["ppu"] * s, 3)); ws.append(round((x1 - x0) / i["ppu"] * s, 3))
            info.add((os.path.basename(os.path.dirname(i["path"])), i["W"], i["H"], round(i["ppu"], 1), i["meshType"], i["alignment"], i["npot"], i["textureType"]))
        print(f"   {k:18s} n={len(v)} H={hs} W={ws} {sorted(info)}")
