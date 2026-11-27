# Collider vs visual check. Collider = def.sprite.bounds (UNSCALED by visualScaleMultiplier, GroundFactory.CreateEnemy)
# then * bodyColliderScale + bodyColliderOffset*full (GroundFactory.ApplySkyTraits). Visual = displayed move frame opaque bbox * scale.
import json, os, sys
import numpy as np
from PIL import Image
OUT = os.path.dirname(os.path.abspath(__file__))
E = {e["enemyId"]: e for e in json.load(open(OUT + "/gather.json", encoding="utf-8"))}
M = {m["id"]: m for m in json.load(open(OUT + "/measure.json"))}

def opaque_rect(i, thr=32):
    a = np.asarray(Image.open(i["path"]).convert("RGBA"))[:, :, 3] > thr
    ys, xs = np.nonzero(a); H, W = a.shape; px, py = i["pivotEff"]; p = i["ppu"]
    return ((xs.min() - px * W) / p, (xs.max() + 1 - px * W) / p, (H - ys.max() - 1 - py * H) / p, (H - ys.min() - py * H) / p)

def bounds(i):  # Unity sprite.bounds relative to pivot
    if i["meshType"] == 0:
        W, H = i["W"], i["H"]; px, py = i["pivotEff"]; p = i["ppu"]
        return (-px * W / p, (1 - px) * W / p, -py * H / p, (1 - py) * H / p)
    return opaque_rect(i, 0)  # tight mesh ~ opaque bounds

def collider(b, cs, co):
    fw, fh = b[1] - b[0], b[3] - b[2]; cx, cy = (b[0] + b[1]) / 2, (b[2] + b[3]) / 2
    cx += fw * co[0]; cy += fh * co[1]; w, h = fw * cs[0], fh * cs[1]
    return (cx - w / 2, cx + w / 2, cy - h / 2, cy + h / 2)

def new_col_params(b, cs, co, k):
    fw, fh = b[1] - b[0], b[3] - b[2]; cx, cy = (b[0] + b[1]) / 2, (b[2] + b[3]) / 2
    return (k * cs[0], k * cs[1]), ((k - 1) * cx / fw + k * co[0], (k - 1) * cy / fh + k * co[1])

def sc(r, s): return tuple(v * s for v in r)
res = {}
for eid, m in M.items():
    e = E[eid]; s = m["s"]; k = m["k"]
    spr = e["sets"]["sprite"][0]; disp = (e["sets"].get("run") or e["sets"]["sprite"])[0]
    b = bounds(spr); col = collider(b, e["colScale"], e["colOffset"]); vis = sc(opaque_rect(disp), s)
    ncs, nco = new_col_params(b, e["colScale"], e["colOffset"], k)
    ncol = collider(b, ncs, nco); nvis = sc(opaque_rect(disp), s * k)
    res[eid] = dict(bounds=b, col=col, vis=vis, newColScale=ncs, newColOffset=nco, ncol=ncol, nvis=nvis)
    f = lambda r: "x[%.2f,%.2f] y[%.2f,%.2f]" % r
    print(f"{eid:22s} k={k:.2f} col {f(col)}  vis {f(vis)}  | new col {f(ncol)} vis {f(nvis)} colScale {ncs[0]:.4f},{ncs[1]:.4f} off {nco[0]:.4f},{nco[1]:.4f}")
json.dump(res, open(OUT + "/collider.json", "w"), indent=1)
