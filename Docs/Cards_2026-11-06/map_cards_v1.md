# マップ専用カードの追加 第1弾(2026-11-06、マスターの指示「洞窟・天空・ラスダンを優先、荒野は少なめ」)

目標の枚数(マスター): 荒野 32〜35 / 洞窟 30〜35 / 天空 30〜35 / ラスダン 15〜20。
第1弾: **荒野 +2(34)/ 洞窟 +7(32)/ 天空 +7(32)/ ラスダン +10(15)= 26 枚**。共通 43 と合わせて各マップ 75〜77 枚の候補。

決まり(既存と同じ):
- 分類 = そのマップで解禁する場所(一度解禁すれば全マップで使える)。抽選の重みはレア度のまま(隠れて下げない)
- 解禁の距離: 洞窟/天空は max(カードの解放距離, 1,000m)、ラスダンはラスダン解放後
- Lv から毎回計算(CardBalanceV3)。同じ効果の種類は既存の枠の足し方・上限(Lv9 上限は能力ごと)に従う
- ★5 は「ルールを変える」ものだけ。ラスダンは ★4 中心、★5 は1枚(ULTIMATE と並ぶ象徴)
- 初期デッキから外した生存系の代わりに、**洞窟の序盤(1,000m)に分かりやすい守りの★2 を置く**(MOSS BED / CRYSTAL SKIN)

数値は Lv1 → Lv9。既存の効果の種類を使う(新しい仕組みは ★ の印のあるものだけ)。

## 荒野街道(+2 → 34)
| ID | 名前 | ★ | 種類 | 解放 | 効果(Lv1 → Lv9) | 狙い |
|---|---|---|---|---|---|---|
| dust_runner | DUST RUNNER | ★2 | MOVEMENT | 0m | 移動速度 +2%/Lv(18%)・地上での攻撃力 +3%/Lv(27%) | 荒野の地面を走る基礎 |
| beast_skull | BEAST SKULL | ★4 | ATTACK | 20,000m | ボスへの攻撃 +5%/Lv(45%)・被弾ののけぞり -4%/Lv(36%) | 獣の頭骨のお守り。大物狩り |

## 自然洞窟(+7 → 32)
| ID | 名前 | ★ | 種類 | 解放 | 効果 | 狙い |
|---|---|---|---|---|---|---|
| moss_bed ★ | MOSS BED | ★2 | HEAL | 1,000m | ボスを倒すとハートを回復(Lv1〜4: 1つ / Lv5〜8: 2つ / Lv9: 3つ)。【新】 | 序盤の分かりやすい回復(初期デッキの生存系の代わり) |
| crystal_skin | CRYSTAL SKIN | ★2 | DEFENSE | 1,000m | 被弾後の無敵 +0.05秒/Lv(0.45)・のけぞり -3%/Lv(27%) | 序盤の守り |
| miners_luck | MINER'S LUCK | ★2 | GROWTH | 1,000m | BONUS(宝)の MILE +10%/Lv(90%)・撃破 EXP +5%/Lv(45%) | 洞窟の宝 |
| echo_strike | ECHO STRIKE | ★3 | ATTACK | 5,000m | 追加攻撃の確率 +2%/Lv(18%)・冷えの確率 +2%/Lv(18%) | 洞窟の反響 |
| stalagmite | STALAGMITE | ★3 | ATTACK | 5,000m | 下攻撃・叩きつけ +8%/Lv(72%)・地上での攻撃力 +2%/Lv(18%) | 鍾乳石を落とす |
| frost_ward | FROST WARD | ★3 | DEFENSE(氷) | 5,000m | Shield を1つ持てる・冷えの確率 +3%/Lv(27%)・冷えの減速 +2.5%/Lv | 氷の守り |
| crystal_heart | CRYSTAL HEART | ★4 | DEFENSE | 20,000m | 最大HP(ハート)+1(Lv1〜2)以降2Lvごとに+1(Lv9 +5)・被弾後の無敵 +0.05秒/Lv | 結晶の心臓 |

## 天空回廊(+7 → 32)
| ID | 名前 | ★ | 種類 | 解放 | 効果 | 狙い |
|---|---|---|---|---|---|---|
| feather_step | FEATHER STEP | ★2 | MOVEMENT | 1,000m | ジャンプ力 +3%/Lv(27%)・空中での攻撃力 +3%/Lv(27%) | 羽のように跳ぶ |
| tailwind | TAILWIND | ★2 | MOVEMENT(風) | 1,000m | 移動速度 +2%/Lv(18%)・前攻撃/後攻撃の速度 +2%/Lv(18%) | 追い風 |
| halo_guard | HALO GUARD | ★3 | DEFENSE | 5,000m | Shield を1つ持てる・被弾後の無敵 +0.06秒/Lv(0.54) | 天空の守り(天空に守りが無かった) |
| storm_caller | STORM CALLER | ★3 | ATTACK(雷) | 5,000m | 落雷の確率 +2%/Lv(18%)・風刃の確率 +2%/Lv(18%) | 雷と風の入口を同時に |
| angels_tithe | ANGEL'S TITHE | ★3 | GROWTH | 5,000m | 敵撃破・BONUS の MILE +8%/Lv(72%)・空中での攻撃力 +2%/Lv(18%) | 天の施し |
| skyfall | SKYFALL | ★4 | ATTACK | 20,000m | 下攻撃・叩きつけ +10%/Lv(90%)・空中の敵への攻撃 +4%/Lv(36%) | 天から落ちる一撃 |
| seraph_wings | SERAPH WINGS | ★4 | MOVEMENT | 20,000m | ジャンプ回数 +1(Lv1・Lv5・Lv9 で +1ずつ)・空中での攻撃力 +4%/Lv(36%) | 翼 |

## ラストダンジョン(+10 → 15)
| ID | 名前 | ★ | 種類 | 効果 | 狙い |
|---|---|---|---|---|---|
| seal_breaker | SEAL BREAKER | ★4 | RISK | 攻撃力 +9%/Lv(81%)・ボスへの攻撃 +3%/Lv・封印するハート +2 | 封印を破る力 |
| abyss_gaze | ABYSS GAZE | ★4 | ATTACK | ボスへの攻撃 +6%/Lv(54%)・雑魚への攻撃 +4%/Lv(36%) | 深淵の眼 |
| ancient_script | ANCIENT SCRIPT | ★4 | GROWTH | 獲得 EXP +15%/Lv(135%)・敵撃破・BONUS の MILE +10%/Lv(90%) | 古代文字の知恵 |
| black_iron | BLACK IRON | ★4 | DEFENSE | 最大HP(Lv9 +5、2Lvごと)・ハート2つ以上の被弾をハート1つ軽く・のけぞり -5%/Lv | 黒鉄の鎧(代償なしの HEAVY ARMOR の上位) |
| void_step | VOID STEP | ★4 | MOVEMENT | 移動速度 +3%/Lv(27%)・被弾後の無敵 +0.05秒/Lv・ジャンプ回数 +1 | 虚空の歩み |
| violet_flame | VIOLET FLAME | ★4 | ATTACK(炎) | 炎上の確率 +3%/Lv・炎上の威力 +6%/Lv・冷えの確率 +2%/Lv | 紫の炎(炎と氷の両方) |
| last_light | LAST LIGHT | ★4 | HEAL | HP25%以下で撃破の回復確率 +5%/Lv・被弾でハート1つ以下になると短い無敵(LAST CHANCE と同じ、+0.1秒/Lv) | 最後の灯 |
| chain_of_seals | CHAIN OF SEALS | ★4 | ATTACK | 雑魚を倒すと周りの敵に封印の鎖(連鎖の爆発と同じ仕組み、威力 +2/Lv・範囲 +0.1m/Lv) | 鎖の連鎖 |
| cursed_crown | CURSED CROWN | ★4 | RISK | 攻撃力 +10%/Lv(90%)・敵の HP +8%/Lv・ボス撃破 MILE +40%/Lv | 呪いの王冠 |
| sealed_core ★ | SEALED CORE | ★5 | SPECIAL | 【ルールを変える】Shield を持てない代わりに、Shield の数ぶん攻撃力 +12%(持てる上限の計算はそのまま)・被弾のたびに1秒だけ攻撃力 +30%。【新】 | ラスダンの象徴(ULTIMATE と並ぶ) |

## 新しい仕組み(2つだけ)
- MOSS BED: ボス撃破の回復(BossManager.OnWildBossDefeated → Lv で 1/2/3 ハート。HealScaled を通す = 終盤は回復の効きが下がる)
- SEALED CORE: Shield の最大数を 0 にし、本来の最大数 × 12% を攻撃力へ。被弾から1秒の攻撃 +30%

## 確認(QA)
- 全 26 枚が分類の表にある・Lv1/Lv9 の効果の行が日英で出る・解禁の条件どおり
- MOSS BED: ボス撃破で回復する / SEALED CORE: Shield が 0 で攻撃が上がる
- ガチャ: マップのカードが出た時に台がその土地の見た目になる(既存の -qaGachaSkin)
