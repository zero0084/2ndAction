# Gather every regular enemy's sprites, import settings and opaque bounds (offline, no Unity).
import os, re, json, glob
import numpy as np
from PIL import Image

ROOT = r"C:/GameProject/2ndAction/Unity/2ndAction/Assets"
OUT = os.path.dirname(os.path.abspath(__file__))
ALPHA = 32

# guid -> png path
guid2path = {}
for meta in glob.glob(ROOT + "/Art/**/*.png.meta", recursive=True) + glob.glob(ROOT + "/Resources/**/*.png.meta", recursive=True):
    with open(meta, encoding="utf-8") as f:
        head = f.read(300)
    m = re.search(r"guid: ([0-9a-f]{32})", head)
    if m: guid2path[m.group(1)] = meta[:-5].replace("\\", "/")

def meta_info(png):
    with open(png + ".meta", encoding="utf-8") as f: t = f.read()
    g = lambda k, d=None: (re.search(r"\n\s*" + k + r": ([^\n]*)", t) or [None, d])[1]
    piv = re.search(r"\n  spritePivot: \{x: ([-\d.e]+), y: ([-\d.e]+)\}", t)
    return dict(ppu=float(g("spritePixelsToUnits", "100")), spriteMode=int(g("spriteMode", "1")),
                meshType=int(g("spriteMeshType", "1")), alignment=int(g("alignment", "0")),
                pivot=(float(piv.group(1)), float(piv.group(2))) if piv else (0.5, 0.5),
                npot=int(g("nPOTScale", "0")), textureType=int(g("textureType", "0")),
                maxSize=int(g("maxTextureSize", "2048")))

ALIGN = {0: (0.5, 0.5), 1: (0, 1), 2: (0.5, 1), 3: (1, 1), 4: (0, 0.5), 5: (1, 0.5), 6: (0, 0), 7: (0.5, 0), 8: (1, 0)}

cache = {}
def png_info(path):
    if path in cache: return cache[path]
    im = Image.open(path).convert("RGBA")
    a = np.asarray(im)[:, :, 3]
    H, W = a.shape
    mask = a > ALPHA
    ys, xs = np.nonzero(mask)
    mi = meta_info(path)
    pv = mi["pivot"] if mi["alignment"] == 9 else ALIGN.get(mi["alignment"], (0.5, 0.5))
    if len(xs) == 0:
        bb = (0, 0, W, H)
    else:
        bb = (int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1)  # x0,y0(top),x1,y1
    # opaque area for sqrt(area) measure
    area = int(mask.sum())
    d = dict(path=path, W=W, H=H, bbox=bb, area=area, **mi, pivotEff=pv)
    cache[path] = d
    return d

def parse_asset(p):
    with open(p, encoding="utf-8") as f: t = f.read()
    def ref(line):
        m = re.search(r"guid: ([0-9a-f]{32})", line)
        return m.group(1) if m else None
    d = {}
    for k in ["enemyId", "movementType", "category", "behaviorKind", "visualScaleMultiplier", "bonusKind", "idleHoverAmplitude"]:
        m = re.search(r"\n  " + k + r": ([^\n]*)", t)
        d[k] = m.group(1).strip() if m else None
    m = re.search(r"\n  bodyColliderScale: \{x: ([-\d.e]+), y: ([-\d.e]+)\}", t)
    d["colScale"] = (float(m.group(1)), float(m.group(2))) if m else (1.0, 1.0)
    m = re.search(r"\n  bodyColliderOffset: \{x: ([-\d.e]+), y: ([-\d.e]+)\}", t)
    d["colOffset"] = (float(m.group(1)), float(m.group(2))) if m else (0.0, 0.0)
    m = re.search(r"\n  stageIds:(.*?)\n  [a-zA-Z]", t, re.S)
    d["stageIds"] = re.findall(r"- (\S+)", m.group(1)) if m else []
    sets = {}
    m = re.search(r"\n  sprite: ([^\n]*)", t); sets["sprite"] = [ref(m.group(1))] if m else []
    m = re.search(r"\n  runFrames:\n((?:  - [^\n]*\n)+)", t)
    sets["run"] = [ref(l) for l in m.group(1).splitlines()] if m else []
    m = re.search(r"\n  attackSprite: ([^\n]*)", t); sets["attackSprite"] = [ref(m.group(1))] if m else []
    m = re.search(r"\n  poses:\n((?:    [^\n]*\n)+)", t)
    if m:
        for l in m.group(1).splitlines():
            mm = re.match(r"\s+(\w+): (.*)", l)
            if mm and ref(mm.group(2)): sets["pose_" + mm.group(1)] = [ref(mm.group(2))]
    # motion link frames
    for i, mm in enumerate(re.finditer(r"  - from: (\S+)\n    to: (\S+)\n(?:    \w+: [^\n]*\n)*?    frames:\n((?:    - [^\n]*\n)+)", t)):
        sets[f"link_{mm.group(1)}>{mm.group(2)}"] = [ref(l) for l in mm.group(3).splitlines()]
    d["sets"] = {k: [g for g in v if g] for k, v in sets.items() if any(v)}
    return d

enemies = []
for p in sorted(glob.glob(ROOT + "/Resources/Enemies/*.asset")):
    d = parse_asset(p)
    d["asset"] = p.replace("\\", "/")
    out = {}
    for k, gs in d["sets"].items():
        lst = []
        for g in gs:
            path = guid2path.get(g)
            if path is None: lst.append(dict(guid=g, missing=True)); continue
            lst.append(png_info(path))
        out[k] = lst
    d["sets"] = out
    enemies.append(d)

with open(OUT + "/gather.json", "w", encoding="utf-8") as f:
    json.dump(enemies, f, indent=1, default=str)
for e in enemies:
    print(e["enemyId"], e["visualScaleMultiplier"], {k: len(v) for k, v in e["sets"].items()})
    for k, v in e["sets"].items():
        for i in v[:1]:
            if i.get("missing"): print("   MISSING", k, i); continue
            print(f"   {k:16s} {os.path.relpath(i['path'], ROOT):60s} {i['W']}x{i['H']} ppu={i['ppu']:.1f} mode={i['spriteMode']} mesh={i['meshType']} al={i['alignment']} piv={i['pivotEff']} npot={i['npot']} tt={i['textureType']} bb={i['bbox']}")
