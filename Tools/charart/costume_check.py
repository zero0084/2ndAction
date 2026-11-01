# -*- coding: utf-8 -*-
# 女性7人の衣装(Steam=元の衣装 / Android=布を増やした衣装)のどちらが出ているかを確かめる(2026-11-01)。
#  2つの衣装で違う画素(差が大きい所)だけを比べ、近い方を判定にする(圧縮・拡大縮小の誤差は両方に同じように乗るので判定には効かない)。
#  使い方:
#   python costume_check.py dump  <書き出しのフォルダ> <Unity のプロジェクト> <steam|android>
#     … CharacterCostumeCheck.Dump が書き出した、取り込み後の絵 28枚
#   python costume_check.py shots <QA の log> <撮影のフォルダ> <Unity のプロジェクト> <steam|android>
#     … QaSweep -qaCharSelect の撮影と [drawn] の行(立ち絵/選んだカードが描かれた範囲)
#  終了コード: 期待と違う物が1つでもあれば 1
import io, os, re, sys
from PIL import Image, ImageChops

FEM = ["dual_blade", "noble_lady", "mage", "gunslinger", "ninja", "miko", "dragonkin"]
ART = "Assets/Art/UI/Characters"
AND = "Assets/Editor/AndroidCharacterArt"

def rgb_on(img, bg=(0, 0, 0)):
    img = img.convert("RGBA"); base = Image.new("RGBA", img.size, bg + (255,)); base.alpha_composite(img); return base.convert("RGB")

def sources(proj, cid, slot):
    s = Image.open(f"{proj}/{ART}/{cid}_{slot}.png"); s.load()
    a = None
    if cid in FEM:
        a = Image.open(f"{proj}/{AND}/{cid}_{slot}.png.bytes"); a.load()
    return s, a

def judge(got, steam, android, size):
    got = rgb_on(got).resize(size, Image.BILINEAR)
    st = rgb_on(steam).resize(size, Image.BILINEAR)
    if android is None:
        d = ImageChops.difference(got, st).convert("L")
        return "shared", sum(d.tobytes()) / (size[0] * size[1]), None
    an = rgb_on(android).resize(size, Image.BILINEAR)
    mask = [v > 40 for v in ImageChops.difference(st, an).convert("L").tobytes()]
    n = max(1, sum(mask))
    ds = [v for v, m in zip(ImageChops.difference(got, st).convert("L").tobytes(), mask) if m]
    da = [v for v, m in zip(ImageChops.difference(got, an).convert("L").tobytes(), mask) if m]
    es, ea = sum(ds) / n, sum(da) / n
    return ("steam" if es < ea else "android"), es, ea

def report(rows, expect, shared_max=12):
    bad = 0
    for name, verdict, es, ea in rows:
        want = "shared" if verdict == "shared" else expect
        ok = verdict == want and (verdict != "shared" or es < shared_max)
        bad += not ok
        extra = f"diff {es:.1f}" if ea is None else f"diff steam {es:.1f} / android {ea:.1f}"
        print(f"{'OK ' if ok else 'NG '} {name:28s} {verdict:8s} {extra}")
    print(f"== {len(rows) - bad}/{len(rows)} as expected ({expect})")
    return 1 if bad else 0

def main():
    mode = sys.argv[1]
    if mode == "dump":
        d, proj, expect = sys.argv[2], sys.argv[3], sys.argv[4]
        rows = []
        for f in sorted(os.listdir(d)):
            m = re.match(r"(.+)_(main|card)\.png$", f)
            if not m: continue
            s, a = sources(proj, m.group(1), m.group(2))
            got = Image.open(os.path.join(d, f)).transpose(Image.FLIP_TOP_BOTTOM) if "--flip" in sys.argv else Image.open(os.path.join(d, f))
            v, es, ea = judge(got, s, a, (s.width // 4, s.height // 4))
            rows.append((f[:-4], v, es, ea))
        sys.exit(report(rows, expect))
    if mode == "shots":
        log, shots, proj, expect = sys.argv[2], sys.argv[3], sys.argv[4], sys.argv[5]
        drawn = {}
        for line in io.open(log, encoding="utf-8", errors="replace"):
            m = re.search(r"\[drawn\] (\S+) (main|card) ([-\d.]+) ([-\d.]+) ([-\d.]+) ([-\d.]+)", line)
            if m: drawn[(m.group(1), m.group(2))] = tuple(float(x) for x in m.groups()[2:])
        rows = []
        for f in sorted(os.listdir(shots)):
            m = re.match(r"(?:\d+_)?charsel_(\d+)x(\d+)_(\d\d)_(.+)\.png$", f)  # Android は番号が前に付く
            if not m: continue
            cid = m.group(4); shot = Image.open(os.path.join(shots, f)); H = shot.height
            for slot in ("main", "card"):
                r = drawn.get((cid, slot))
                if r is None: continue
                x, y, w, h = r
                box = (int(round(x)), int(round(H - (y + h))), int(round(x + w)), int(round(H - y)))
                crop = shot.crop(box)
                s, a = sources(proj, cid, slot)
                if slot == "main":  # 2026-11-01: 立ち絵は外周を黒へなじませて表示する → 外周を除いた中央(縦横 70%)だけで比べる
                    def inner(im):
                        w, h = im.size; return im.crop((int(w * 0.15), int(h * 0.15), int(w * 0.85), int(h * 0.85)))
                    crop, s = inner(crop), inner(s); a = inner(a) if a is not None else None
                # 2つの衣装の差を見る大きさ(小さすぎると差が消える)
                size = (max(32, crop.width // 2), max(48, crop.height // 2))
                v, es, ea = judge(crop, s, a, size)
                rows.append((f"{m.group(1)}x{m.group(2)} {cid}_{slot}", v, es, ea))
        sys.exit(report(rows, expect, 25))  # 撮影は選択中の金の縁が重なる分だけ差が出る

if __name__ == "__main__":
    main()
