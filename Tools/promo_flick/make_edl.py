# -*- coding: utf-8 -*-
# 撮影の記録(raw/<shot>_flicks.tsv)から、宣伝動画「スマホでの遊び方」の区切り(edl.json)を作る。区切りはフリックの時刻が基準
#  (技が当たる直前で切らない)。出来た edl.json は手で直してよい(edit.py はそれを読むだけ)。
#  第2版(2026-11-28 マスターの確認後): コンボ紹介に拡大カット(冒頭は上フリックの直前から)/横画面は全体 + 戦闘部分の拡大/
#   縦の3つの見え方に ①②③ と上下2段の補足/操作と技の言葉(edit.py が記録から付ける)
#   python make_edl.py <撮影のフォルダ(side/fold/oblique/land がある)> <edl.json>
import io, json, os, subprocess, sys

FPS = 60
cap = sys.argv[1]; out = sys.argv[2]
# 拡大の範囲 [幅の割合, 左, 上](縦横比はそのまま)。縦(横から見る)はキャラが横 0.33〜0.4・道が上から 0.78・空中は 0.62 前後まで上がる
ZOOM_SIDE = [0.5, 0.12, 0.42]  # 2倍(第2版の確認: 0.6 では空が多くキャラが小さかった)
# 横画面の戦闘部分(キャラは横 0.3 前後・道は上から 0.5・戦闘は 0.3〜0.5)
INSET_LAND = [0.42, 0.12, 0.18]


def flicks(view, shot):
    p = os.path.join(cap, view, "raw", shot + "_flicks.tsv")
    rows = []
    for ln in io.open(p, encoding="utf-8").read().splitlines()[1:]:
        f = ln.split("\t")
        rows.append((int(f[0]) / FPS, f[1], f[2]))
    return rows


def length(view, shot):
    r = subprocess.run([os.environ.get("FFPROBE", "ffprobe"), "-v", "error", "-show_entries", "format=duration", "-of", "default=nw=1:nk=1",
                        os.path.join(cap, view, "raw", shot + ".mkv")], capture_output=True, text=True)
    return float(r.stdout.strip())


def seg(view, shot, a, b, **kw):
    d = {"shot": shot, "view": view, "layout": kw.pop("layout", "portrait"), "start": round(a, 3), "end": round(b, 3)}
    d.update(kw); return d


combo = flicks("side", "pf_combo")
ups = [t for t, d, r in combo if d == "Up"]
backs = [t for t, d, r in combo if d == "Backward"]
downs = [t for t, d, r in combo if d == "Down"]
L = length("side", "pf_combo")
C1 = "フリックで、コンボをつなげ。"; C2 = "横でも、縦でも。"; C3 = "縦画面は、3つの見え方から。"; C4 = "走りは自動。攻撃はフリック。"
segs = []
# 冒頭(拡大): 上フリックの 0.5 秒前から = 打ち上げを早めに → 空中で前フリック → 急降下 → 着地(後ろフリックの手前まで)
segs.append(seg("side", "pf_combo", ups[0] - 0.5, backs[0] - 0.25, caption=C1, zoom=ZOOM_SIDE))
# 横画面: 全体(下)+ 同じ場面の戦闘部分の拡大(上)
land = flicks("land", "pf_land")
b0 = max(0.0, land[0][0] - 0.9)
segs.append(seg("land", "pf_land", b0, b0 + 5.5, layout="land2", inset=INSET_LAND, caption=C2))
# 縦の3つの見え方(画面全体のまま。同じ手順の打ち上げ)を 4.2 秒ずつ。表示名は設定画面の表記に ①②③
for v, name in (("side", "① 横から見る"), ("fold", "② 上下2段で見る"), ("oblique", "③ 斜め上から見る")):
    fl = flicks(v, "pf_launch")
    u = [t for t, d, r in fl if d == "Up"][0]
    s0 = max(0.0, u - 0.6)
    sg = seg(v, "pf_launch", s0, s0 + 4.2, caption=C3, label=name)
    # 字幕の位置: 横から = 画面下の地面 / 上下2段 = 上の段の地面の帯 / 斜め上 = 左上の空(戦っている所を隠さない)
    if v == "fold":
        # 上下2段: 下 = 今の走り、上 = その先(右端で折り返し)。設定画面の説明(SettingsPanel)と同じ意味の短い補足
        sg.update(caption_y=600, caption_size=64, note="下：今の走り ／ 上：その先（右端で折り返し）", note_y=1700)  # 補足は下の段の地面(段の境目には下フリックの指が来る)
    if v == "oblique": sg.update(caption_y=600, caption_size=60)
    segs.append(sg)
# コンボ紹介: 後ろフリック(全体)→ 2回目の打ち上げから急降下(拡大)→ 地上の敵へ前フリック(全体)
d0 = segs[0]["end"]
z0 = ups[1] - 0.4; z1 = downs[1] + 0.6; d1 = ups[-1] - 0.45
segs.append(seg("side", "pf_combo", d0, z0, caption=C4))
segs.append(seg("side", "pf_combo", z0, z1, caption=C4, zoom=ZOOM_SIDE))
segs.append(seg("side", "pf_combo", z1, d1, caption=C4))
# 締め: 最後の打ち上げから急降下(拡大)→ 全体 → ロゴ
e0 = downs[-1] + 0.5
segs.append(seg("side", "pf_combo", d1, e0, caption="", zoom=ZOOM_SIDE))
segs.append(seg("side", "pf_combo", e0, min(L - 0.05, e0 + 2.6), caption="", actions=False))
edl = {"capture_dir": cap, "segments": segs, "end_card_seconds": 2.4, "bgm_volume": 0.28, "se_volume": 1.0,
       "note": "start/end = 撮影の映像の秒(等速)。shot = raw の名前、view = 撮影のフォルダ(side/fold/oblique/land)。zoom/inset = [幅の割合, 左, 上]"}
io.open(out, "w", encoding="utf-8").write(json.dumps(edl, ensure_ascii=False, indent=1))
print(f"[edl] total {sum(s['end'] - s['start'] for s in segs):.2f}s -> {out}")
t = 0.0
for s in segs:
    d = s['end'] - s['start']
    print(f"  {t:6.2f}-{t + d:6.2f} {s['view']:8s} {s['shot']:10s} {s['start']:6.2f}-{s['end']:6.2f} {'zoom' if s.get('zoom') else s['layout']:9s} {s.get('caption', '')} {s.get('label', '')}")
    t += d
