# X 投稿用「カードシステム」紹介動画の編集表を作る(2026-11-09)。
#   python make_edl.py <撮影素材の置き場> edl_cards.json
# 切り替えは BGM(bgm_sky_middle、132BPM)の拍の整数倍。寄せ方(view)は 4:5 と 16:9 で別(ゲームの画面の並びが縦/横で違うため)
#   view: zoom(1 = 帯の下を埋める大きさ)/ cx, cy(素材の中の中心、割合)/ to(場面の終わりへ向けて動かす先)/ fit(全体を入れる)
import json, sys
B = 60 / 132
ROOT = sys.argv[1].rstrip("/")
ARC = "C:/GameProject/SteamCaptureArchive/2026-11-08_XPromo"  # 前回の PV の素材(同じ版のゲーム、炎/雷/氷/風のカードのデッキで走った戦闘)


def cut(shot, inn, beats, x45, x169, scene, **kw):
    c = {"shot": shot, "in": inn, "dur": round(beats * B, 4), "x45": x45, "x169": x169, "scene": scene}
    c.update(kw)
    return c


def v(zoom=1.0, cx=0.5, cy=0.5, **kw):
    d = {"zoom": zoom, "cx": cx, "cy": cy}
    d.update(kw)
    return d


cuts = [
    # A 0〜5秒: カードの魅力(★4 に絞った所持カードの一覧を流す → 高レアのカードの大きな詳細)
    cut("cp_cards", 1.40, 5, v(1.45, .5, .80, to={"cy": .84}), v(1.3, .38, .72, to={"cy": .76}), "所持カードを ★4 に絞ってレア度順に流す(デッキ編集の一覧)"),
    cut("cp_cards", 8.15, 2, v(2.1, .5, .19), v(1.7, .30, .40), "PHOENIX ★5 の大きな詳細"),
    cut("cp_cards", 10.55, 2, v(2.1, .5, .19), v(1.7, .30, .40), "INFERNO ★4"),
    cut("cp_cards", 16.55, 2, v(2.1, .5, .19), v(1.7, .30, .40), "ABSOLUTE ZERO ★4"),
    # B 5〜17秒: 取得と使い方(ガチャ → 効果の確認 → ラン中の3択 → 取った能力で戦う)
    cut("cp_gacha", 5.0, 8, v(1.25, .62, .45, **{"in": 25.90}), v(1.15, .55, .45, **{"in": 4.90}), "ガチャ本体をタップ → 本物の抽選で NEW CARD", id="gacha"),
    cut("cp_deck", 3.10, 6, v(1.0, .5, .5, to={"zoom": 1.55, "cx": .5, "cy": .30}, move_from=.25, move_to=.85), v(1.0, .5, .5, to={"zoom": 1.5, "cx": .45, "cy": .40}, move_from=.25, move_to=.85),
        "一覧のカードを長押し → 大きな詳細で効果を確認(PHOENIX)", id="check"),
    cut("cp_choice", 1.55, 7, v(1.0, .5, .55, **{"in": 1.55}), v(1.1, .5, .45, **{"in": 2.20}),
        "ラン中のレベルアップ: デッキから出た3枚から1枚を選ぶ(4:5 は CHAIN LIGHTNING、16:9 は THUNDER STRIKE)", id="choice"),
    cut(ARC + "/boss3/raw/aerial", 0.40, 4, v(1.5, .48, .40), v(1.6, .48, .42), "雷の能力の発動: 落雷の一撃 → 斬り抜けて敵を吹き飛ばす(雷のカードを入れたデッキで走った別の撮影)", id="fire"),
    # C 17〜31秒: デッキ編集
    cut("cp_deck", 0.70, 4, v(1.0, .5, .5), v(1.0, .5, .5), "ホームのデッキ編集を開く", id="deck"),
    cut("cp_deck", 6.55, 6, v(1.25, .5, .62), v(1.15, .5, .55), "一覧の PHOENIX をタップ → デッキへ(11 → 12/12)"),
    cut("cp_deck", 9.00, 8, v(1.15, .5, .55), v(1.1, .5, .5), "COMBUSTION をデッキの枠へドラッグ → 確認 → 入れ替え"),
    cut("cp_deck", 12.40, 6, v(1.45, .5, .26), v(1.35, .5, .30), "プリセットの ▶ で「2 風と氷」へ切り替え(12枚がまとめて替わる)"),
    # D 29〜48秒: カード合成(同名 → 異名 → 異名の失敗の例 → MASTERY)。説明欄は指でスクロールして成功率/継承率の行を見せる
    cut("cp_fuse_same", 0.50, 2, v(1.0, .5, .5), v(1.0, .5, .5), "ホームのカード合成を開く", id="fuse"),
    cut("cp_fuse_same", 2.45, 8, v(1.3, .5, .62, to={"zoom": 1.6, "cx": .31, "cy": .86}, move_from=.5, move_to=.95), v(1.25, .62, .50, to={"zoom": 1.55, "cx": .80, "cy": .62}, move_from=.5, move_to=.95),
        "メイン(TEMPEST Lv3)→ 素材(TEMPEST Lv2)→ 説明欄をスクロール →【同名強化】成功率 100% / Lv.3 + Lv.2 → Lv.5"),
    cut("cp_fuse_same", 7.65, 8, v(1.1, .5, .45), v(1.0, .5, .5), "合成する → 演出 → 同名強化 完了! TEMPEST Lv.5"),
    cut("cp_fuse_cross", 3.95, 8, v(1.3, .5, .62, to={"zoom": 1.6, "cx": .31, "cy": .86}, move_from=.3, move_to=.75), v(1.25, .62, .50, to={"zoom": 1.55, "cx": .80, "cy": .62}, move_from=.3, move_to=.75),
        "異名: JUDGMENT BOLT + FIRE WHIRL → 説明欄をスクロール →【異名合成】結果は抽選 / メイン側 50% / 素材 25%", id="cross"),
    cut("cp_fuse_cross", 8.25, 8, v(1.1, .5, .45), v(1.0, .5, .5), "合成する → メイン側/素材側 継承成功 → 合成大成功!(撮影では結果を指定。README の注)", badge=["成功例", "ok"]),
    cut("cp_fuse_fail", 11.05, 4, v(1.1, .5, .40), v(1.0, .5, .45), "異名の失敗の例: 両方とも継承失敗 → 2枚は消費、MILE を還元", badge=["失敗例", "ng"]),
    cut("cp_mastery", 9.70, 6, v(1.2, .42, .40), v(1.15, .5, .45), "Lv.9 MAX の SHATTER STRIKE に同じカード → MASTERY +3(★★☆☆☆)", id="mastery"),
]
battle = [
    cut(ARC + "/play/raw/pv_luka", 7.50, 4, v(1.4, .42, .40), v(1.4, .42, .40), "ルカ: 敵の群れを突き抜ける(雷の連鎖)", id="battle"),
    cut(ARC + "/play/raw/pv_luka", 9.65, 3, v(1.4, .36, .42), v(1.4, .36, .42), "ルカ: 空中で捕まえて連撃(雷)"),
]
end = cut(ARC + "/play/raw/gunslinger", 7.40, 9, v(1.15, .40, .45), v(1.35, .40, .45), "締め: 二丁拳銃士の連続撃破を暗くしてロゴ", end=True)
cuts += battle + [end]

t = 0.0
T = {}
for c in cuts:
    c["at"] = round(t, 3)
    if "id" in c:
        T[c["id"]] = t
    t += c["dur"]
T["end"] = cuts[-1]["at"]

caps = [
    {"text": "走るだけじゃない。", "at": 0.10, "to": 2.35, "style": "hero"},
    {"text": "カードで、戦い方が変わる。", "at": 2.45, "to": T["gacha"] - 0.12, "style": "hero"},
    {"text": "カードを集めて、能力を強化！", "note": "ガチャでカードを入手", "at": T["gacha"] + 0.10, "to": T["check"], "fo": 0},
    {"text": "カードを集めて、能力を強化！", "note": "長押しで効果をチェック", "at": T["check"], "to": T["choice"], "fi": 0, "fo": 0, "rise": False},
    {"text": "カードを集めて、能力を強化！", "note": "ラン中はレベルアップで1枚を選ぶ", "at": T["choice"], "to": T["fire"], "fi": 0, "fo": 0, "rise": False},
    {"text": "カードを集めて、能力を強化！", "note": "選んだ能力が戦闘で発動", "at": T["fire"], "to": T["deck"] - 0.12, "fi": 0, "rise": False},
    {"text": "12枚のカードで、\n自分だけのデッキを！", "at": T["deck"] + 0.10, "to": T["fuse"] - 0.12, "style": "main"},
    {"text": "同じカードで確実に強化！", "note": "同名合成は成功率100%", "at": T["fuse"] + 0.10, "to": T["cross"] - 0.10},
    {"text": "異なる能力の組み合わせにも挑戦！", "note": "※異名合成は確率で能力を継承。失敗することもあります", "at": T["cross"], "to": T["mastery"] - 0.10, "style": "main"},
    {"text": "Lv.9 MAX の先は、MASTERY で育成！", "at": T["mastery"], "to": T["battle"] - 0.12, "style": "main"},
    {"text": "組み合わせは、あなた次第。", "at": T["battle"] + 0.10, "to": t + 1.0, "style": "hero"},
]
edl = {
    "raw": {"x45": ROOT + "/x45/raw", "x169": ROOT + "/x169/raw"},
    "logo": "C:/GameProject/2ndAction/Unity/2ndAction/Assets/Art/UI/TitleLogoV2.png",
    "loudness": -14, "tp": -2.0, "se_gain": 1.0,
    # ホームの入口のタップ(撮影ではボタンの位置を記録できないので、画面の絵から決めた位置)
    # 「ラスダン」のタブ(隠し要素の存在が分かる)は、その場所だけ近くの背景の色で塗る(ゲーム本体の UI は変えない)。
    #   [x0, y0, x1, y1, 色を取る所 sx0, sy0, sx1, sy1](撮影の画面の割合)
    #   9番目 = 塗り始める素材の秒(cp_deck はホームから開くので、デッキ編集が出る 1.22 秒から)
    "masks": {"x45": {s: [[0.810, 0.598, 0.990, 0.666, 0.86, 0.590, 0.97, 0.597, t0]] for s, t0 in (("cp_cards", 0.0), ("cp_deck", 1.22))},
              "x169": {s: [[0.406, 0.501, 0.490, 0.558, 0.489, 0.512, 0.501, 0.550, t0]] for s, t0 in (("cp_cards", 0.0), ("cp_deck", 1.22))}},
    "tapfix": {"x45": {"home_deck": [0.39, 0.87], "home_fusion": [0.62, 0.87]},
               "x169": {"home_deck": [0.14, 0.585], "home_fusion": [0.92, 0.756]}},
    "bgm": [{"file": "C:/GameProject/2ndAction/Unity/2ndAction/Assets/Audio/Placeholder/Bgm/bgm_sky_middle_n.wav",
             "loop": True, "at": 0, "from": 0, "dur": round(t, 3), "gain": 0.50, "fade_in": 0.02, "fade_out": 2.4}],
    "captions": caps, "cuts": cuts,
    "end": {"logo_at": 0.9, "dim": 0.5, "dim_at": 0.4, "dim_ramp": 0.7},
}
json.dump(edl, open(sys.argv[2], "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print(f"{len(cuts)} cuts, total {t:.3f}s  " + "  ".join(f"{k} {x:.2f}" for k, x in T.items()))
