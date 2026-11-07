# 常設チャレンジとバッヂの一覧(エステルへの相談用、2026-11-07)

本番の絵はまだ無く、ゲームの中ではカテゴリ色の枠+手続きで描いた仮のモチーフで出しています(`pf_badges_sheet.png` 上段 = 獲得 / 下段 = 未獲得)。
本番の絵は `Unity/2ndAction/Assets/Resources/Badges/badge_<チャレンジID>.png` に置くと、コードを変えずに差し替わります。

## 絵の決まり(案)

- 正方形・透明背景(512×512 で描いて、ゲームでは 256×256 に縮める想定)。表示は小さい所で 48〜72px、大きい所で 100px 前後。
- 小さくてもカテゴリが分かること: 枠の色をカテゴリで固定し、形(モチーフ)でも分かるように。
- 未獲得はゲームの側で暗く塗って鍵を重ねる(シルエットになる)ので、輪郭がはっきりした形が向いています。
- 段(同じカテゴリの1〜3段目)は、モチーフの数や派手さで差をつける(仮の絵は下の小さな点の数)。

## カテゴリ

| カテゴリ | 枠の色(仮) | モチーフの方向性 |
|---|---|---|
| DISTANCE | 琥珀 | 道標/一本道 |
| NO DAMAGE | 鋼の青 | 盾 |
| COMBO | 紅 | 連続斬撃 |
| SPEED | 水色 | 翼/速度線 |
| AIR | 若葉 | 羽 |
| BREAK | 橙 | 砕けた鎖 |
| BOSS | 金 | 王冠/ボス固有モチーフ |
| REAPER | 紫 | 鎌 |
| SPECIAL | 桃 | 星/紋章 |

## 一覧(26個)

| ID | カテゴリ | 段 | チャレンジ名 | 達成条件 | バッヂ名 | モチーフ(仮の絵) | MILE | 既存の記録から復元 |
|---|---|---|---|---|---|---|---|---|
| dist_10k | DISTANCE | 1 | REACH 10 KM | 1回のランで10,000mに到達する | TRAILBLAZER | 道標(1本の杭)と一本道 | 30 | する |
| dist_50k | DISTANCE | 2 | REACH 50 KM | 1回のランで50,000mに到達する | LONG ROAD | 遠くへ続く一本道と道標2本 | 60 | する |
| dist_100k | DISTANCE | 3 | REACH 100 KM | 1回のランで100,000mに到達する | HORIZON WALKER | 地平線へ消える一本道と星 | 100 | する |
| dist_total | DISTANCE | 3 | 1,000,000 M | 累計距離を1,000,000mにする(脱出して確定した距離) | MILLION MILER | 道標に刻まれた目盛り | 100 | する |
| nodmg_10k | NO DAMAGE | 1 | UNTOUCHED 10 KM | ランの開始から一度も被弾せずに10,000m走る | IRON GUARD | 盾 | 50 | — |
| nodmg_30k | NO DAMAGE | 2 | UNTOUCHED 30 KM | ランの開始から一度も被弾せずに30,000m走る | AEGIS | 縁取りのある大盾 | 100 | — |
| nodmg_boss | NO DAMAGE | 3 | FLAWLESS BOSS | ボス戦の間に一度も被弾せずにボスを倒す | FLAWLESS | 盾と小さな王冠 | 60 | — |
| combo_100 | COMBO | 1 | 100 HIT | 100 HITのコンボをつなぐ | TWIN SLASH | 2本の斬撃 | 30 | — |
| combo_500 | COMBO | 2 | 500 HIT | 500 HITのコンボをつなぐ | TRIPLE SLASH | 3本の斬撃 | 60 | — |
| combo_1000 | COMBO | 3 | 1000 HIT | 1000 HITのコンボをつなぐ | ENDLESS BLADE | 交差する多数の斬撃 | 100 | — |
| speed_130 | SPEED | 1 | 130 KM/H | 走る速さが130km/hに達する | SWIFT WING | 片翼 | 30 | — |
| speed_150 | SPEED | 2 | 150 KM/H | 走る速さが150km/hに達する | GALE WING | 翼と速度線 | 60 | — |
| speed_170 | SPEED | 3 | 170 KM/H | 走る速さが170km/hに達する | SONIC WING | 両翼と多くの速度線 | 100 | — |
| air_kill | AIR | 1 | AERIAL KILL | 空中にいる間に敵を倒す | FEATHER | 羽1枚 | 20 | — |
| air_kill_100 | AIR | 2 | 100 AERIAL KILLS | 空中にいる間に敵を累計100体倒す | SKY HUNTER | 羽2枚 | 60 | — |
| air_stay | AIR | 3 | 6 SEC AIRBORNE | 着地せずに6秒間空中にいる | CLOUD DANCER | 輪になった羽3枚 | 40 | — |
| break_run5 | BREAK | 1 | 5 BREAKS | 1回のランでボスを5回BREAKさせる | CHAIN BREAKER | 砕けた鎖 | 50 | — |
| break_total50 | BREAK | 2 | 50 BREAKS TOTAL | ボスを累計50回BREAKさせる | SHACKLE CRUSHER | 砕けた鎖2本 | 80 | — |
| boss_garmud | BOSS | 1 | WASTELAND KING | 荒野王ガルムードを倒す | BEAST CROWN | 王冠と牙 | 100 | — |
| boss_noctaris | BOSS | 2 | ABYSS SOVEREIGN | 深淵の主ノクタリスを倒す | ABYSS CROWN | 王冠と結晶 | 100 | — |
| boss_seraphiel | BOSS | 3 | SKY EMPEROR | 天帝セラフィエルを倒す | HALO CROWN | 王冠と光輪 | 100 | — |
| boss_rush | BOSS | 3 | FINAL TRIAD | 95〜97kmの3体同時ボスラッシュを抜けて、ラスボスの前にたどり着く | TRIAD CROWN | 3つ並んだ王冠 | 80 | — |
| reaper_meet | REAPER | 1 | REAPER SIGHTED | 死神と出会う | REAPER'S MARK | 鎌 | 30 | する |
| reaper_escape | REAPER | 2 | REAPER ESCAPE | 死神が現れた後に脱出に成功する | SCYTHE EVADER | 鎌を振り切る軌跡 | 100 | — |
| sp_starter | SPECIAL | 1 | STARTER'S ROAD | 初期デッキのカードだけで、1回のランで50,000m走る | ORIGIN STAR | 星 | 80 | — |
| sp_noauto | SPECIAL | 2 | ON MY OWN FEET | オートを一度もONにせずに、1回のランで30,000m走る | CREST OF RESOLVE | 星と紋章 | 80 | — |

MILE の合計: 1800(全部達成した時。ガチャ1回 = 500 MILE)

## 名前について

- チャレンジ名・バッヂ名・カテゴリ名は英字で、全言語そのまま(カード名と同じ扱い)。達成条件の文だけ訳します。
- 名前は仮です。エステルの案があれば差し替えます(ID は保存に使うので変えません)。
