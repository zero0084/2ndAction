# -*- coding: utf-8 -*-
# 撮影の記録(raw/<shot>_flicks.tsv)から、宣伝動画「スマホでの遊び方」の区切り(edl.json)を作る。区切りはフリックの時刻が基準
#  (技が当たる直前で切らない)。出来た edl.json は手で直してよい(edit.py はそれを読むだけ)。
#   python make_edl.py <撮影のフォルダ(side/fold/oblique/land がある)> <edl.json>
import io, json, os, sys

FPS = 60
cap = sys.argv[1]; out = sys.argv[2]


def flicks(view, shot):
    p = os.path.join(cap, view, "raw", shot + "_flicks.tsv")
    rows = []
    for ln in io.open(p, encoding="utf-8").read().splitlines()[1:]:
        f = ln.split("\t")
        rows.append((int(f[0]) / FPS, f[1], f[2]))
    return rows


def length(view, shot):
    import subprocess
    r = subprocess.run([os.environ.get("FFPROBE", "ffprobe"), "-v", "error", "-show_entries", "format=duration", "-of", "default=nw=1:nk=1",
                        os.path.join(cap, view, "raw", shot + ".mkv")], capture_output=True, text=True)
    return float(r.stdout.strip())


combo = flicks("side", "pf_combo")
ups = [t for t, d, r in combo if d == "Up"]
backs = [t for t, d, r in combo if d == "Backward"]
downs = [t for t, d, r in combo if d == "Down"]
L = length("side", "pf_combo")
segs = []
# 0〜5秒: 縦で 上フリック → 打ち上げ → 空中で追撃
#  最初の前フリックの少し前 〜 1回目の打ち上げ/急降下/着地(後ろフリックの手前で切る = 技の途中で切らない)
a0 = max(0.0, combo[0][0] - 0.7)
a1 = backs[0] - 0.25
segs.append({"shot": "pf_combo", "view": "side", "layout": "portrait", "start": round(a0, 3), "end": round(a1, 3), "caption": "フリックで、コンボをつなげ。"})
# 5〜10秒: 横で疾走しながら戦う
land = flicks("land", "pf_land")
b0 = max(0.0, land[0][0] - 0.9)
segs.append({"shot": "pf_land", "view": "land", "layout": "land", "start": round(b0, 3), "end": round(b0 + 5.0, 3), "caption": "横でも、縦でも。", "label": "横画面"})
# 10〜22秒: 縦の3つの見え方(同じ手順の打ち上げ)を 4 秒ずつ
for v, name in (("side", "横から見る"), ("fold", "上下2段で見る"), ("oblique", "斜め上から見る")):
    fl = flicks(v, "pf_launch")
    u = [t for t, d, r in fl if d == "Up"][0]
    s0 = max(0.0, u - 0.6)
    seg = {"shot": "pf_launch", "view": v, "layout": "portrait", "start": round(s0, 3), "end": round(s0 + 4.0, 3), "caption": "縦画面は、3つの見え方から。", "label": name}
    # 字幕の位置: 横から = 画面下の地面 / 上下2段 = 上の段の地面の帯 / 斜め上 = 左上の空(道の奥の端に掛からない大きさ)。戦っている所を隠さない
    if v == "fold": seg.update(caption_y=700, caption_size=64)
    if v == "oblique": seg.update(caption_y=600, caption_size=60)
    segs.append(seg)
# 22〜30秒: 縦で 後ろ → 2回目の打ち上げ → 空中 → 急降下 → 地上の敵へ前(入力から攻撃・追撃まで)。締めの打ち上げの手前まで
d0 = segs[0]["end"]
d1 = ups[-1] - 0.45
segs.append({"shot": "pf_combo", "view": "side", "layout": "portrait", "start": round(d0, 3), "end": round(d1, 3), "caption": "走りは自動。攻撃はフリック。"})
# 30〜35秒: 締めの打ち上げ → 急降下 → ロゴ(最後の 3 秒でロゴが重なる)
e1 = min(L - 0.05, downs[-1] + 2.8)
segs.append({"shot": "pf_combo", "view": "side", "layout": "portrait", "start": round(d1, 3), "end": round(e1, 3), "caption": "", "touch": True})
edl = {"capture_dir": cap, "segments": segs, "end_card_seconds": 3.0, "bgm_volume": 0.28, "se_volume": 1.0,
       "note": "start/end = 撮影の映像の秒(等速)。shot = raw の名前、view = 撮影のフォルダ(side/fold/oblique/land)"}
io.open(out, "w", encoding="utf-8").write(json.dumps(edl, ensure_ascii=False, indent=1))
print(f"[edl] total {sum(s['end'] - s['start'] for s in segs):.2f}s -> {out}")
for s in segs: print(f"  {s['view']:8s} {s['shot']:10s} {s['start']:7.2f}-{s['end']:7.2f} ({s['end'] - s['start']:.2f}s) {s.get('caption', '')} {s.get('label', '')}")
