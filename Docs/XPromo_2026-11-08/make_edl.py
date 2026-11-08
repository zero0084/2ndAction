# X 宣伝動画(ゲーム紹介 PV)の編集表を作る。切り替えは BGM(bgm_boss_strong、162BPM)の拍の整数倍
import json, sys
B = 60 / 162
RAW = sys.argv[1]  # 撮影素材の置き場(play/ と boss2/ の上)
def cut(shot, inn, beats, cx=.5, cy=.45, z45=1.25, z169=1.45, **kw):
    c = {"shot": shot, "in": inn, "dur": round(beats * B, 4), "cx": cx, "cy": cy, "x45": {"zoom": z45}, "x169": {"zoom": z169}}
    c.update(kw); return c
cuts = [
    # A 0〜3秒: 強烈な導入(高速で走りながら斬り倒す)
    cut("boss3/raw/aerial", 0.55, 4, cx=.48, cy=.40, z45=1.6, z169=1.75, scene="黒剣士: 雷の一撃 → 斬り抜けて敵をまとめて吹き飛ばす(荒野街道)"),
    cut("play/raw/pv_luka", 7.50, 4, cx=.42, cy=.40, scene="ルカ: 108km/h で敵の群れを突き抜ける(荒野街道)"),
    # B 3〜9秒: 爽快なアクション
    cut("play/raw/aerial_dual", 0.70, 3, cx=.40, cy=.50, z45=1.4, z169=1.55, scene="双剣士: 斬り上げ → 雷"),
    cut("boss3/raw/aerial", 1.70, 2, cx=.45, cy=.42, z45=1.6, z169=1.75, scene="黒剣士: 走りながらの斬撃"),
    cut("play/raw/gunslinger", 1.45, 4, cx=.36, cy=.42, scene="二丁拳銃士: 撃ち抜いて吹き飛ばす(夕暮れの荒野)"),
    cut("play/raw/pv_luka", 9.65, 4, cx=.36, cy=.42, scene="ルカ: 空中で捕まえて連撃(雷)"),
    cut("play/raw/gunslinger", 9.40, 3, cx=.40, cy=.45, scene="二丁拳銃士: 空の敵の光線の下を駆け抜ける"),
    # C 9〜15秒: 広がる世界
    cut("boss2/raw/pv_road", 1.00, 3, cx=.45, cy=.45, z45=1.0, z169=1.0, scene="荒野街道: 城の見える道"),
    cut("play/raw/pv_fighter", 4.55, 4, cx=.36, cy=.40, z45=1.2, z169=1.35, vf="eq=gamma=1.08", scene="格闘家: 自然洞窟で蜘蛛/蝙蝠をはじき飛ばす"),
    cut("play/raw/miko", 1.50, 3, cx=.40, cy=.48, z45=1.1, z169=1.15, scene="巫女: 天空回廊で矢を放つ"),
    cut("play/raw/pv_road_sky", 1.00, 3, cx=.45, cy=.48, z45=1.1, z169=1.15, scene="天空回廊: 浮島を駆ける"),
    cut("boss3/raw/pv_boss_griffin", 0.30, 3, cx=.62, cy=.40, z45=1.1, z169=1.2, scene="グリフォンが空から迫る(荒野街道)"),
    # D 15〜23秒: 巨大ボス(第2版: ネタバレを避けて通常ボスだけ。98km のラスボスは使わない)
    cut("boss3/raw/pv_boss_cyclops", 4.30, 4, cx=.55, cy=.45, z45=1.15, z169=1.3, scene="サイクロプス: 必殺技「天崩しの大棍棒」の溜め"),
    cut("boss2/raw/pv_boss_hydra", 9.60, 4, cx=.45, cy=.48, z45=1.15, z169=1.3, scene="ヒュドラ: 必殺技「三首の連撃」の炎を跳んでかわす"),
    cut("boss3/raw/pv_boss_cyclops", 7.00, 3, cx=.52, cy=.48, z45=1.15, z169=1.3, scene="サイクロプス: 大棍棒で地面を砕く"),
    cut("boss3/raw/pv_boss_demon", 7.50, 3, cx=.55, cy=.45, z45=1.15, z169=1.3, scene="デーモン: 必殺技「冥界の魔柱陣」"),
    cut("boss3/raw/pv_boss_cyclops", 11.00, 4, cx=.60, cy=.50, z45=1.15, z169=1.3, scene="サイクロプスに BREAK! → 最終段階"),
    cut("boss2/raw/pv_boss_knight", 18.10, 4, cx=.45, cy=.55, z45=1.15, z169=1.3, scene="黒騎士: 強烈な一撃 → 撃破"),
]
t = sum(c["dur"] for c in cuts)
end = {"shot": "boss2/raw/pv_road", "in": 2.5, "dur": round(18 * B, 4), "cx": .45, "cy": .60, "x45": {"zoom": 1.2}, "x169": {"zoom": 1.2}, "se": 0.5,
       "logo_at": 3.05, "lines": [{"text": "決めるのは、あなたです。", "at": 3.95, "gold": False, "size": 72}],
       "dim": 0.32, "dim_ramp": 2.6, "scene": "締め: 遠くまで続く道を走り続ける"}
caps = [
    {"text": "走り出したら、止まらない。", "at": 0.12, "to": 2.85, "style": "hero"},
    {"text": "走れ。戦え。突き進め。", "at": 3.05, "to": 8.75, "style": "hero"},
    {"text": "その先には、何が待つ？", "at": 9.00, "to": 14.75, "style": "main"},
    {"text": "どこまで走り、\nどこで引き返すのか。", "at": t + 0.15, "to": t + 2.95, "style": "main"},
]
edl = {
    "raw": RAW,
    "logo": "C:/GameProject/2ndAction/Unity/2ndAction/Assets/Art/UI/TitleLogoV2.png",
    "loudness": -14, "tp": -2.0, "se_gain": 1.0,
    "bgm": [{"file": "C:/GameProject/2ndAction/Unity/2ndAction/Assets/Audio/Placeholder/Bgm/bgm_boss_strong_n.wav",
             "loop": True, "at": 0, "from": 0, "dur": round(t + end["dur"], 3), "gain": 0.42, "fade_in": 0.02, "fade_out": 2.2}],
    "captions": caps, "cuts": cuts, "end": end,
}
json.dump(edl, open(sys.argv[2], "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print(f"cuts {t:.3f}s + end {end['dur']:.3f}s = {t + end['dur']:.3f}s")
