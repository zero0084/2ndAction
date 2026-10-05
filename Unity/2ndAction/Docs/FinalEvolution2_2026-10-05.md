# FINAL EVOLUTION 第2段階(2026-10-05): 再使用 + 全99枚

土台: 第1段階 `bb79cf9`(APK 260)。仕組みの本体は `Cards/FinalEvolution.cs`、カードごとのデータは `Cards/FinalEvolutionTuning.cs`(アセットを作ればインスペクターで調整。無ければコードの既定値。アセットに無いカードは既定値で補う)。

## 1. 流れ(第2段階)
```
Lv9(実際の能力Lv。キャラカードで開始時から9も可)= 資格
  └ 初回だけ readyMeters(5,000m)走る → READY(= FINAL EVOLUTION の抽選に参加できる)
      └ 通常の LEVEL UP の3択に最大1枠 → 選ぶ → ACTIVE(時間 or 距離)
          └ 終わる → すぐ READY(再チャージ/Gauge/距離の待ち なし)→ また LEVEL UP の候補に…(何度でも)
```
- **USED(1ラン1回)は廃止**。`usesPerRun` は残したが既定 0(制限なし。将来の特殊カード用)。`uses` は「発動の回数」として保存/表示するだけ。
- **ACTIVE の間はその FE は候補に出ない**(同じ FE は重ならない。`Activate` も ACTIVE 中は拒否)。**別の FE 同士は同時に ACTIVE** になれる。
- 候補の規則は第1段階のまま: 3択のうち最大1枠 / 断っても READY のまま / デッキが全部 Lv9 でも出せる / ボス報酬・BONUS ZONE・ULTIMATE には混ぜない / 複数 READY は「候補に出た回数が少ないもの」から(同数はランダム。永久に外れるものは無い)。

## 2. 効果の作り(データ+共通モジュール)
各カードの FE = `Entry`(データ)= 次の組み合わせ。巨大な switch で個別に書いていない。
| 部品 | 中身 |
|---|---|
| `module` | 分類(26種: DamageBurst / AttackSpeed / RangeProjectile / Ground / Air / Combo / FirstStrike / Finisher / Shield / Guard / Heal / Lifesteal / LowHp / HighHp / Movement / Exp / Mile / EnemyChallenge / BossChallenge / Fire / Ice / Lightning / Wind / Blood / Revive / Special)。オーラの色と整理に使う |
| `amplify`(増幅) | **そのカード自身の効果(Lv9 の値)を ACTIVE の間だけ ×amplify**。マイナスの効果(遅くなる・封印・敵が強くなる…)も同じ倍率 = 長所も短所も完成する |
| `bonuses`(追加) | 既存の効果の種類(`EffectType`)を ACTIVE の間だけ足す。個性づけ(例: SKY RUNNER の空中ジャンプ+2、COMBO PLUS の COMBO MASTER+3、FROST EDGE の凍結に必要な冷え-1) |
| 専用の処理 | 代表9枚の第1段階の効果(最終ダメージ×1.5 / 超高速 / 斬撃波 / Blood Shield / 緊急復活 / 延焼 / 連鎖 / EXP の枠 / MILE とリスク) |
| `kind` + 持続 | 時間型(秒)/ 距離型(m)。戦闘/防御/属性 8〜12秒、移動 6〜10秒、成長/MILE/挑戦 1,500〜3,000m |

- 増幅/追加は `GameManager.RecomputeCardStats` の中で、カードの合計に乗る(①最大HPの成長 ×amplify + 追加、②各効果 ×amplify → `FinalEvolution.AddBonuses`)。その先のカードバランス v3 の枠/曲線/上限/EXP の減衰はそのまま通る。
- 発動/終了/CONTINUE の読み込みで `RecomputeCardStats` を呼び直し、能力値を**毎回最初から作り直す**ので、終われば何も残らない(足し引きの積み重ねをしない)。
- 最大HPが増える FE は回復しない(最大だけ上がる)。終わって最大が下がると今のHPは最大で切り詰め。
- 全カードの一覧(持続/増幅/追加/内容)は下の **6.**。

## 3. 個別の注意
- **PHOENIX**: 1回の ACTIVE につき緊急復活1回。使わずに終われば消える(溜まらない)。通常の PHOENIX の Charge は増やさない。使った復活は `SaveState.spent` に残し、CONTINUE で戻らない。
- **EXP / MILE**: EXP は第1段階どおり枠(減衰の曲線の前)へ。MILE は倍率。どちらも終われば作り直しで消え、何回使っても溜まらない(テスト J/K)。
- **SPEED DOWN**: 速くしない(遅さを ×1.5)。代わりに攻撃/攻撃速度/被弾後の無敵を追加。
- **リスク系**(BERSERKER / GLASS CANNON / GIANT SLAYER / FORTRESS / HEAVY ARMOR / GREED / HELL MODE / PANDEMONIUM …): 欠点も同じ倍率で強くなる。封印したハートは増幅しない(封印の数はそのまま)。
- **挑戦系**(MORE ENEMIES / TOUGH / FAST / MONSTER RUSH / ELITE / HORDE / HELL MODE / WANTED / BOSS CHALLENGE / BOSS RUSH / PANDEMONIUM): 今の Challenge の値(出現/HP/速さ/精鋭/賞金首/ボス)と報酬の両方を ×amplify。EncounterDirector/ChallengeSystem が読む既存の値に乗るだけ(古い出現処理は使っていない)。
- **属性系**: ElementSystem/CardProcs の既存の上限(同じ敵のクールダウン/再帰の禁止/proc と演出の枠)はそのまま。FE の増幅で確率/威力が上がっても、上限で頭打ち(負荷テストの procDrops/fxDrops)。
- **ALMIGHTY**(内部 id `ultimate`): ×1.6(専門のカードは ×1.8〜2.5)。ALMIGHTY 自身の Lv9 の値も専門より小さいので、どの分野でも専門の FE に負ける。
- **#100 ULTIMATE**(`character_ultimate`): FE の対象外(定義なし。`Supported` = false)。
- **AWAKENED**: 持続 +10%・金のオーラ・終わりの光だけ(通常の能力値は変えない)。
- **能力Lv型の効果**(SHOCKWAVE Lv など)は式で使われる値なので、×amplify で 9 を超えても配列外にはならない(UltimateLevel だけは 0〜9 で切るが、対象外)。HEAVY ARMOR の HeavyArmorLevel は「あるか無いか」なので増幅しても変わらない(最大HP/ジャンプの低下は増幅、のけぞり軽減と被弾後の無敵を追加)。
- **封印カード**(GLASS CANNON / DEATH'S CONTRACT): 最大HPが足りないキャラは、通常の規則どおり Lv9 まで取れない(= FE の資格が無い)。今回は規則を変えていない。

## 4. 保存 / CONTINUE
`RunCheckpoint.Data.finalEvolution`(`SaveState`: id / eligible / eligibleAt / ready / active / uses / remaining / offers / spent)。
- 読み込みは `ResetRun` の後に状態を置き直し、ACTIVE の専用の効果を1回だけ付け、能力値を作り直す → 二重にならない(2回読んでも同じ)。
- 第1段階の保存の「USED」(資格あり・使用済み・READY でない)は READY として読む。項目の無い古い保存は状態なし。
- Game Over / ホーム / 新しいランで消える。メタ側(カードLv / Mastery / AWAKENED)は変えない。

## 5. 開発用
- DEBUG → FINAL EVOLUTION TEST: 99枚(10枚ずつのページ)。「終了→LEVEL UP」で ACTIVE を即終了 → READY → すぐ3択(「候補に固定」ON なら同じ FE を選び直せる。固定は DEBUG RUN の中だけ効く)。NO SAVE / NO RECORD は第1段階のまま。封印カードは HEART UP を足して Lv9 にする。
- 自動テスト: `-qaFinalEvo`(第1段階、再使用に合わせて G/H/I を更新)/ `-qaFe2 <dir> [-qaFe2Only RAFSP]`
  - R: 再使用 A〜O(本物の LEVEL UP の3択)+ 公平さ
  - A: 全99枚(定義/Lv8は資格なし/Lv9で資格/初回+5,000m/READY/候補/発動/効果/ACTIVE 中は候補に出ない/終了/READY/2回目/同じ強さ/保存と読み込み/10回/AWAKENED/例外)→ `fe2_cards.tsv`
  - S: 組み合わせ10種の負荷 → `fe2_stress.tsv`(`-qaFe2StressSecs` 既定8)
  - P: 実時間の10回反復(敵を殴りながら)→ `fe2_repeat.tsv`(`-qaFe2RepeatIds`)

## 6. 全カードの FINAL EVOLUTION(99枚)
| # | id | カード | 分類 | モジュール | 型 | 持続 | 増幅 | 追加 | FE名 | 内容 |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | attack_up | ATTACK UP | Attack | DamageBurst | 時間 | 10s | ×1 | - | 攻撃限界突破 | 10秒間 最終ダメージ×1.5 |
| 2 | speed_up | SPEED UP | Movement | Movement | 時間 | 8s | ×1 | - | 超高速状態 | 8秒間 加速・接敵時に自動の小攻撃・障害物と接触から守る |
| 3 | attack_range_up | ATTACK RANGE UP | Attack | RangeProjectile | 時間 | 10s | ×1 | - | 画面を切り裂く射程 | 10秒間 射程が大きく伸び、命中時に斬撃波 |
| 4 | vampire | VAMPIRE | Heal | Lifesteal | 時間 | 10s | ×1 | - | 血の飢え | 10秒間 吸収の確率が大きく上がり、溢れた分は Blood Shield |
| 5 | phoenix | PHOENIX | Heal | Revive | 時間 | 10s | ×1 | - | 不死鳥状態 | 10秒間 致死の被弾から1回だけ緊急復活(通常の PHOENIX とは別。使わずに終わると消える) |
| 6 | flame_blade | FLAME BLADE | Attack | Fire | 時間 | 10s | ×1 | - | 炎獄 | 10秒間 強化された炎上と、周囲への延焼 |
| 7 | thunder_strike | THUNDER STRIKE | Attack | Lightning | 時間 | 10s | ×1 | - | 雷神状態 | 10秒間 落雷しやすく、連鎖が1つ増える |
| 8 | exp_up | EXP UP | Growth | Exp | 距離 | 2000m | ×1 | - | 経験値覚醒 | 次の2,000m EXP の強化を追加(減衰の曲線はそのまま) |
| 9 | greed | GREED | Risk | Mile | 距離 | 2000m | ×1 | - | 黄金暴走 | 次の2,000m MILE×1.5 / 受けるダメージ×1.3 |
| 10 | speed_down | SPEED DOWN | Movement | Movement | 時間 | 9s | ×1.5 | AttackPct+0.25 AttackSpeedPct+0.15 HurtInvincibleSeconds+0.3 | 精密走法 | 9秒間 さらに遅くなるが、攻撃/攻撃速度/被弾後の無敵が上がる |
| 11 | jump_power_up | JUMP POWER UP | Movement | Movement | 時間 | 8s | ×2.5 | AirPct+0.15 | 天翔 | 8秒間 ジャンプ力の効果×2.5、空中攻撃+15% |
| 12 | jump_count_up | JUMP COUNT UP | Movement | Air | 時間 | 8s | ×1.6 | AirPct+0.1 | 多段跳躍 | 8秒間 空中ジャンプの回数×1.6、空中攻撃+10% |
| 13 | sky_runner | SKY RUNNER | Movement | Air | 時間 | 9s | ×2 | JumpCount+2 | 空を駆ける者 | 9秒間 ジャンプ力/空中攻撃の効果×2、空中ジャンプ+2 |
| 14 | momentum | MOMENTUM | Movement | Movement | 時間 | 8s | ×2 | - | 加速の化身 | 8秒間 速さが攻撃になる効果×2 |
| 15 | overdrive | OVERDRIVE | Movement | Movement | 時間 | 8s | ×1.8 | - | 限界駆動 | 8秒間 OVERDRIVE の効果×1.8(入りやすく、強く) |
| 16 | close_call | CLOSE CALL | Movement | Movement | 時間 | 8s | ×1.8 | HurtInvincibleSeconds+0.4 | 紙一重 | 8秒間 速さ/被弾後の無敵×1.8、さらに無敵+0.4秒 |
| 17 | attack_speed_up | ATTACK SPEED UP | Attack | AttackSpeed | 時間 | 10s | ×2.2 | - | 超連撃 | 10秒間 攻撃速度の効果×2.2 |
| 18 | air_attack_up | AIR ATTACK UP | Attack | Air | 時間 | 10s | ×2 | JumpCount+1 | 空の覇者 | 10秒間 空中攻撃の効果×2、空中ジャンプ+1 |
| 19 | brake_attack | BRAKE ATTACK | Attack | DamageBurst | 時間 | 10s | ×1.8 | - | 制動の一撃 | 10秒間 攻撃力と、攻撃後の減速が×1.8 |
| 20 | first_strike | FIRST STRIKE | Attack | FirstStrike | 時間 | 10s | ×2.5 | - | 必殺の初撃 | 10秒間 初撃の効果×2.5 |
| 21 | combo_plus | COMBO PLUS | Attack | Finisher | 時間 | 10s | ×2.2 | ComboMasterLevel+3 | 締めの極意 | 10秒間 締めの効果×2.2、締めで衝撃(COMBO MASTER 相当+3) |
| 22 | mob_killer | MOB KILLER | Attack | DamageBurst | 時間 | 10s | ×2 | - | 殲滅者 | 10秒間 雑魚への攻撃/撃破のMILEの効果×2 |
| 23 | ground_fighter | GROUND FIGHTER | Attack | Ground | 時間 | 10s | ×2.2 | GroundBreakerLevel+3 | 大地の拳 | 10秒間 地上攻撃の効果×2.2、地上の締めで地を走る衝撃(+3) |
| 24 | combo_edge | COMBO EDGE | Attack | Combo | 時間 | 10s | ×2.2 | - | 連撃の刃 | 10秒間 連続ヒットの攻撃上昇×2.2 |
| 25 | piercing_blade | PIERCING BLADE | Attack | RangeProjectile | 時間 | 10s | ×1.8 | - | 全貫通 | 10秒間 貫通の数と威力×1.8 |
| 26 | shockwave | SHOCKWAVE | Attack | RangeProjectile | 時間 | 10s | ×1.8 | - | 大衝撃波 | 10秒間 衝撃波の威力と範囲×1.8 |
| 27 | aerial_blade | AERIAL BLADE | Attack | Air | 時間 | 10s | ×1.8 | - | 空裂 | 10秒間 空中の追い斬り×1.8 |
| 28 | boss_killer | BOSS KILLER | Attack | BossChallenge | 時間 | 12s | ×2 | - | 王殺し | 12秒間 ボスへの攻撃の効果×2 |
| 29 | chain_explosion | CHAIN EXPLOSION | Attack | Special | 時間 | 10s | ×1.7 | - | 誘爆連鎖 | 10秒間 撃破の爆発×1.7(連鎖の上限はそのまま) |
| 30 | double_attack | DOUBLE ATTACK | Attack | AttackSpeed | 時間 | 10s | ×2.2 | - | 二重の刃 | 10秒間 追加の1撃の確率×2.2(追加からは出ない) |
| 31 | sonic_blade | SONIC BLADE | Attack | RangeProjectile | 時間 | 10s | ×1.8 | - | 音速の斬撃 | 10秒間 飛ぶ斬撃の大きさ/強さ×1.8 |
| 32 | rapid_edge | RAPID EDGE | Attack | AttackSpeed | 時間 | 10s | ×2.2 | - | 神速の手数 | 10秒間 主攻撃の攻撃速度の効果×2.2 |
| 33 | long_blade | LONG BLADE | Attack | RangeProjectile | 時間 | 10s | ×1.8 | - | 長刃 | 10秒間 攻撃範囲の効果×1.8 |
| 34 | sky_master | SKY MASTER | Attack | Air | 時間 | 10s | ×1.8 | - | 天空の主 | 10秒間 空中攻撃と空中ジャンプの取り戻し×1.8 |
| 35 | heavy_impact | HEAVY IMPACT | Attack | Ground | 時間 | 10s | ×2 | - | 大地割り | 10秒間 下攻撃/叩きつけ/着地の衝撃×2 |
| 36 | air_dominion | AIR DOMINION | Attack | Air | 時間 | 10s | ×2 | - | 制空 | 10秒間 空中で当て続けるほど上がる効果×2 |
| 37 | combo_master | COMBO MASTER | Attack | Finisher | 時間 | 10s | ×1.8 | - | 奥義連舞 | 10秒間 締めの特殊な衝撃×1.8 |
| 38 | high_voltage | HIGH VOLTAGE | Attack | Lightning | 時間 | 10s | ×1.8 | - | 超高電圧 | 10秒間 雷の威力/足止め×1.8 |
| 39 | frost_edge | FROST EDGE | Attack | Ice | 時間 | 10s | ×1.8 | FreezeThresholdReduce+1 | 氷結の刃 | 10秒間 冷えの確率/減速×1.8、凍結に必要な冷え-1 |
| 40 | wind_cutter | WIND CUTTER | Attack | Wind | 時間 | 10s | ×1.8 | WindPierce+1 | 疾風刃 | 10秒間 風刃の確率/威力×1.8、貫通+1 |
| 41 | ground_breaker | GROUND BREAKER | Attack | Ground | 時間 | 10s | ×1.8 | - | 地脈崩し | 10秒間 地面を走る衝撃波×1.8 |
| 42 | combo_rush | COMBO RUSH | Attack | Combo | 時間 | 10s | ×2 | - | 連撃怒涛 | 10秒間 連撃の途中の攻撃/主攻撃の速さ×2 |
| 43 | air_strike | AIR STRIKE | Attack | Air | 時間 | 10s | ×2 | - | 対空の極み | 10秒間 空中の敵への攻撃×2 |
| 44 | hunter | HUNTER | Attack | BossChallenge | 時間 | 12s | ×2 | - | 狩人の本能 | 12秒間 ボス戦での踏み込み/接近の速さ×2 |
| 45 | burning_soul | BURNING SOUL | Attack | Fire | 時間 | 10s | ×1.8 | - | 燃え盛る魂 | 10秒間 炎上の強さ/長さ×1.8 |
| 46 | inferno | INFERNO | Attack | Fire | 時間 | 10s | ×1.8 | - | 業火 | 10秒間 燃えている敵の撃破で広がる炎×1.8 |
| 47 | ice_prison | ICE PRISON | Attack | Ice | 時間 | 10s | ×1.8 | - | 氷獄 | 10秒間 凍結しやすさ/冷えの時間×1.8 |
| 48 | absolute_zero | ABSOLUTE ZERO | Attack | Ice | 時間 | 10s | ×1.8 | - | 絶対零度 | 10秒間 凍結した雑魚の砕け/冷えた敵への攻撃×1.8 |
| 49 | chain_lightning | CHAIN LIGHTNING | Attack | Lightning | 時間 | 10s | ×1.8 | - | 雷鎖 | 10秒間 連鎖の数/距離×1.8 |
| 50 | thunder_lord | THUNDER LORD | Attack | Lightning | 時間 | 10s | ×1.8 | - | 雷帝 | 10秒間 雷の威力/連鎖/範囲×1.8 |
| 51 | gale | GALE | Attack | Wind | 時間 | 10s | ×1.8 | - | 暴風 | 10秒間 風刃の速さ/距離/貫通×1.8 |
| 52 | tornado | TORNADO | Attack | Wind | 時間 | 10s | ×1.8 | - | 大竜巻 | 10秒間 竜巻の威力と範囲×1.8 |
| 53 | heart_up | HEART UP | Defense | Shield | 時間 | 10s | ×1.6 | HealBonusHearts+1 | 鉄の心臓 | 10秒間 最大HPの増加×1.6(上限ハート20。終われば戻る)、回復+1 |
| 54 | shield | SHIELD | Defense | Shield | 時間 | 10s | ×2 | - | 盾の嵐 | 10秒間 Shield の数と戻る速さ×2 |
| 55 | counter | COUNTER | Defense | Guard | 時間 | 10s | ×2 | - | 倍返し | 10秒間 Shield で防いだ時の反撃×2 |
| 56 | iron_will | IRON WILL | Defense | HighHp | 時間 | 10s | ×2 | - | 鋼の意志 | 10秒間 満タンの間の攻撃/最大HP×2 |
| 57 | perfect_guard | PERFECT GUARD | Defense | Guard | 時間 | 10s | ×2 | - | 完全防御 | 10秒間 Shield を使った時の無敵×2 |
| 58 | flame_counter | FLAME COUNTER | Defense | Fire | 時間 | 10s | ×2 | - | 炎の報復 | 10秒間 Shield で防いだ時の炎の反撃×2 |
| 59 | last_chance | LAST CHANCE | Defense | Guard | 時間 | 10s | ×2 | - | 最後の砦 | 10秒間 ハート1つになった瞬間の無敵×2 |
| 60 | overheal | OVERHEAL | Heal | Heal | 時間 | 10s | ×2 | - | 溢れる生命 | 10秒間 回復量と、溢れた分の Shield×2 |
| 61 | blood_rush | BLOOD RUSH | Heal | Blood | 時間 | 10s | ×2 | - | 血の昂り | 10秒間 HP が低い時の攻撃/吸収×2 |
| 62 | predator | PREDATOR | Heal | Lifesteal | 時間 | 10s | ×2 | - | 捕食者 | 10秒間 吸収の確率×2、ボスからも吸収しやすい |
| 63 | second_wind | SECOND WIND | Heal | Heal | 時間 | 10s | ×2 | - | 不屈 | 10秒間 瀕死からの回復の量×2(クールダウンも短く) |
| 64 | blood_blade | BLOOD BLADE | Heal | Blood | 時間 | 10s | ×1.8 | - | 血刃 | 10秒間 出血の確率/威力×1.8 |
| 65 | pathfinder | PATHFINDER | Growth | Exp | 距離 | 2000m | ×2 | - | 道を拓く者 | 次の2,000m 距離の経験値の効果×2(曲線を通す) |
| 66 | long_haul | LONG HAUL | Growth | Exp | 距離 | 2000m | ×1.8 | - | 長旅の糧 | 次の2,000m 経験値/最大HPの効果×1.8 |
| 67 | level_break | LEVEL BREAK | Growth | Exp | 距離 | 1500m | ×1.8 | - | 限界突破の学び | 次の1,500m 経験値の効果×1.8(曲線を通す) |
| 68 | experience_burst | EXPERIENCE BURST | Growth | Exp | 距離 | 2000m | ×1.8 | - | 経験の奔流 | 次の2,000m 敵/ボスの経験値の効果×1.8 |
| 69 | the_long_road | THE LONG ROAD | Growth | Exp | 距離 | 2500m | ×1.6 | - | 果てなき道 | 次の2,500m 経験値/最大HPの効果×1.6 |
| 70 | treasure_hunter | TREASURE HUNTER | Growth | Mile | 距離 | 2000m | ×2 | - | 宝の嗅覚 | 次の2,000m 宝・報酬/ボス/距離の MILE の効果×2 |
| 71 | berserker | BERSERKER | Risk | LowHp | 時間 | 10s | ×1.8 | - | 狂戦士 | 10秒間 攻撃/攻撃速度×1.8(封印したハートはそのまま) |
| 72 | more_enemies | MORE ENEMIES | Risk | EnemyChallenge | 距離 | 1500m | ×2 | - | 群れを呼ぶ | 次の1,500m 敵の出現と撃破の MILE が×2 |
| 73 | executioner | EXECUTIONER | Risk | Mile | 距離 | 2000m | ×2 | - | 処刑人 | 次の2,000m 撃破の MILE の効果×2 |
| 74 | tough_enemies | TOUGH ENEMIES | Risk | EnemyChallenge | 距離 | 1500m | ×2 | - | 強敵の試練 | 次の1,500m 雑魚の HP と、MILE/撃破の経験値が×2 |
| 75 | fast_enemies | FAST ENEMIES | Risk | EnemyChallenge | 距離 | 1500m | ×1.8 | - | 疾風の敵 | 次の1,500m 雑魚の速さと、MILE/撃破の経験値が×1.8 |
| 76 | last_stand | LAST STAND | Risk | LowHp | 時間 | 10s | ×2 | - | 背水 | 10秒間 HP が低い時の攻撃×2 |
| 77 | glass_cannon | GLASS CANNON | Risk | DamageBurst | 時間 | 10s | ×1.8 | - | 砕ける砲台 | 10秒間 攻撃×1.8(封印したハートはそのまま) |
| 78 | adrenaline | ADRENALINE | Risk | LowHp | 時間 | 10s | ×2 | - | 限界の鼓動 | 10秒間 HP が低い時の攻撃速度×2 |
| 79 | monster_rush | MONSTER RUSH | Risk | EnemyChallenge | 距離 | 1500m | ×2 | - | 魔物の奔流 | 次の1,500m 敵の出現と経験値が×2 |
| 80 | elite_enemies | ELITE ENEMIES | Risk | EnemyChallenge | 距離 | 1500m | ×2 | - | 精鋭の行進 | 次の1,500m 精鋭の確率と MILE が×2 |
| 81 | horde | HORDE | Risk | EnemyChallenge | 距離 | 1500m | ×1.8 | - | 大群 | 次の1,500m 敵の出現/MILE/撃破の経験値が×1.8 |
| 82 | boss_challenge | BOSS CHALLENGE | Risk | BossChallenge | 距離 | 3000m | ×2 | - | 王への挑戦 | 次の3,000m(その間に始まるボス戦)ボスの HP とボス MILE が×2 |
| 83 | giant_slayer | GIANT SLAYER | Risk | DamageBurst | 時間 | 10s | ×1.8 | - | 巨人殺し | 10秒間 攻撃×1.8、攻撃速度の低下も×1.8(超重量の一撃) |
| 84 | berserk_drive | BERSERK DRIVE | Risk | LowHp | 時間 | 10s | ×1.8 | - | 狂乱 | 10秒間 HP が低い時の攻撃/攻撃速度×1.8 |
| 85 | reverse_gear | REVERSE GEAR | Risk | DamageBurst | 時間 | 10s | ×1.8 | - | 逆走の力 | 10秒間 攻撃と速さの低下が×1.8 |
| 86 | heart_breaker | HEART BREAKER | Risk | DamageBurst | 時間 | 10s | ×1.8 | - | 砕けた心 | 10秒間 攻撃と、封印したハートごとの攻撃×1.8 |
| 87 | exp_converter | EXP CONVERTER | Risk | Mile | 距離 | 2000m | ×2 | - | 錬金 | 次の2,000m 経験値の低下と MILE の増加が×2 |
| 88 | ground_zero | GROUND ZERO | Risk | Ground | 時間 | 10s | ×1.8 | - | 地上の鬼 | 10秒間 地上の攻撃と空中の低下が×1.8 |
| 89 | skybound | SKYBOUND | Risk | Air | 時間 | 10s | ×1.8 | - | 天上の鬼 | 10秒間 空中の攻撃と地上の低下が×1.8 |
| 90 | hell_mode | HELL MODE | Risk | EnemyChallenge | 距離 | 1500m | ×1.8 | - | 地獄の門 | 次の1,500m 雑魚の HP/動き/出現と MILE/ボス MILE が×1.8 |
| 91 | boss_rush | BOSS RUSH | Risk | BossChallenge | 距離 | 3000m | ×2 | - | 王の行列 | 次の3,000m ボスがもう1体加わる確率とボス MILE が×2 |
| 92 | wanted | WANTED | Risk | EnemyChallenge | 距離 | 2000m | ×1.8 | - | 賞金稼ぎ | 次の2,000m 賞金首の強さ/頻度と MILE が×1.8 |
| 93 | deaths_contract | DEATH'S CONTRACT | Risk | DamageBurst | 時間 | 10s | ×1.8 | - | 死神の契約 | 10秒間 攻撃×1.8(封印したハートはそのまま) |
| 94 | heavy_armor | HEAVY ARMOR | Risk | Guard | 時間 | 10s | ×1.8 | HurtKnockbackReduce+0.4 HurtInvincibleSeconds+0.3 | 重装の極み | 10秒間 最大HPの成長×1.8・のけぞり-40%・被弾後の無敵+0.3秒(ジャンプ力の低下も×1.8) |
| 95 | fortress | FORTRESS | Risk | HighHp | 時間 | 10s | ×1.8 | ShieldCapacity+1 | 不落の城塞 | 10秒間 最大HP/のけぞりの軽減×1.8・Shield+1(移動速度の低下も×1.8) |
| 96 | no_turning_back | NO TURNING BACK | Risk | Movement | 距離 | 1500m | ×1.6 | MileGainMultiplier+0.4 | 振り返らない | 次の1,500m 速さと敵の出現が×1.6、MILE+40% |
| 97 | pandemonium | PANDEMONIUM | Risk | EnemyChallenge | 距離 | 1500m | ×1.6 | - | 万魔殿 | 次の1,500m 敵/精鋭/ボスの強化と MILE/ボス MILE が×1.6 |
| 98 | ultimate | ALMIGHTY | Special | Special | 時間 | 10s | ×1.6 | - | 万能の完成 | 10秒間 攻撃/最大HP/速さ×1.6(どれも専門のカードには及ばない) |
| 99 | one_more_mile | ONE MORE MILE | Special | Mile | 距離 | 2000m | ×1.6 | - | もう1マイル | 次の2,000m MILE/ボス MILE/経験値の効果×1.6 |

## 7. 今後の正式素材(VFX / SE)
- 発動: カード別(モジュール別)の発動 SE、画面の縁の光の色違い(今は仮の単色の縁+共通のヒットストップ)。
- オーラ: モジュール別のオーラの形(今は丸い光の色だけ)。複数 ACTIVE の時は先頭の1つだけ表示 → 重ね方の素材。
- 終了: 終わりの光(今は AWAKENED のみ)と、READY へ戻った時の小さな知らせの SE。
- HUD: READY(★)/ACTIVE(残り)の枠の正式の絵、発動回数の小さな数字。
- 専用: 斬撃波(ATTACK RANGE UP)、Blood Shield、緊急復活、延焼、強化落雷の正式エフェクト。
