# カードバランス調査(2026-10-03、調査のみ。カード/敵/ボスの数値は変えていない)

対象は最新プロジェクト。値はすべて現在のコードとアセットから取っている。

- **カード**: ゲーム内でゲーム自身の適用処理 `GameManager.ApplyCardEffectsStacked` を通して実測した(`QaSweep -qaCardSurvey`)。
- **キャラの技**: コード調査。
- **ボス**: 実戦(`QaSweep -qaBossKill`)とコード調査。

前提:
- **数値はすでに10倍スケール(CombatScale、2026-10-02実装済み)**。今回の依頼の「10倍化は保留」とは異なり、今のコードは攻撃20/ハート1つ=10HPの体系。その現状のまま測った(戻していない)。
- Lv9上限(`MaxRunCardLevel = 9`)は実装済み。ボスHPの再設計案(`BossHpPlan`)は開発版の切り替えだけで、既定は現行のHP。

## 0. 結論(先に)

1. **一番大きい原因は「1Lvあたりの上昇量」と「掛け方」**
   - 攻撃系は基礎20に対して +10〜+60/Lv の足し算。Lv9で1枚あたり基礎の×5.5〜×28。
   - 速度/ジャンプ/攻撃時間は**複利**(1.12^9 = ×2.77、1.25^9 = ×7.45)。
   - 13枚がすべて加算で重なるので、完成ビルドの1発は基礎の数百倍になる。
2. **MOMENTUM / OVERDRIVE は距離だけで ×4.556 が掛かる(3.6km以降ずっと)**
   - Lv1の2枚だけで、5km以降は1発 20 → 339。10kmの大蛇(550)が2発で倒れる。
   - SPEED UP等で出した実際の速さは関係ない。
3. **Lv9の上限はカードIDの文字列ごと**
   - 合成カード(v2キー)は別のIDになるので、同じ能力を何枚も重ねられる。
   - ゲーム内で確認: ATTACK UP 36回分(攻撃20→380)。理論上は13枠×9 = 117回。
4. **最大HPを下げるRiskカードは、欠点が下限1で止まる**
   - 下限に達した後は攻撃だけが増える(DEATH'S CONTRACT は Lv5で最大HP 1、Lv9で攻撃+540)。
5. **速度**
   - 自然加速の上限100km/hから、SPEED UP Lv4 / GREED Lv3 / NO TURNING BACK Lv2 で150km/hを超える。
   - 速度カード5枚Lv9で ×593(安全上限900km/hで止まる)。
   - GREED / NO TURNING BACK の欠点(敵の出現頻度)は実装上効いていない。
6. **カードなし**: ほとんどのボスは時間を掛ければ倒せる。ただし近接キャラには不可能に近いボスがある。
   - **天空20kmのタイタン**: お嬢様騎士/格闘/忍者/竜人は300秒で0ダメージ。ボスが5m以上離れて立つため。
   - **天空5kmの魔人**: 近接キャラだと10〜33分かかる見込み。
   - 天空/洞窟にはボス戦中の「ラン再開」が無い(荒野だけ)。倒せない間、ランは止まったままになる。
7. **仕様不一致が多い**
   - 属性(雷/炎/氷/風)は表示だけ。
   - 敵出現率のカードは全ステージで無効。
   - 初撃/締めのカードは弓/魔法/忍者/巫女に一切乗らない。
   - 攻撃範囲は飛び道具に一切乗らない、など。

## 1. 現在の通常カード総数: **91枚**(Resources/Cards)

## 2. キャラカード3枠 / デッキ10枚 / 合成の扱い(最新コード)

**キャラカード枠**(`GameManager.ApplyCharacterCardEffects`)
- キャラごとに3枠。ラン開始時に1回適用する。
- 素のカードは「その枠のLv」回、合成カード(v2キー)は1回適用する。
- 合成カードの effects には、能力ごとに「強化量」回ぶんの効果が並んでいる。

**合成**(`CardFusionLogic` / `CardVariant`)
- 合成Lv = 2枚のLvの合計。上限は9。
- 能力ごとの強化量も合算する。能力は最大9種類(主1+副8)。
- **合成Lv9のカード1枚 = 9回分の効果**。中身は同じ能力×9でも、別々の能力×1を9種類でもよい。

**デッキ**
- 素のカードは取得1回で+1Lv。合成カードは取得1回で合成Lvぶんが入る。
- 同じカードは `GetCurrentRunStack(cardId)` の合計が9まで。

**上限の数え方**
- `GetCurrentRunStack` はカードIDの文字列ごとに数える。
- `attack_up` と `v2|attack_up|9|1|attack_up*9` と `v2|attack_up|9|2|attack_up*9`(レア度だけ違う)は**別々に**Lv9まで入る。
- → **想定上の最大**(この報告の基準): 13枚、すべて別のカードでLv9 = 117回分。
- → **実際に成立する最大**: 13枠すべてに同じ能力を入れられ、1つの能力を最大117回分まで重ねられる。必要な枚数を合成すれば作れる。

## 3. SPEED UP を「取得時の短時間Buff+小さな恒常強化」にする案について(分析)

今の値を見る限り、この案へ変えた方がよい理由は強い。

- **複利で上がり続ける**
  - 恒常の速度が `runSpeed *= 1+v` の複利で、自然加速の上限(100km/h)の上に掛け算で乗る。
  - そのため、取るほど人間の限界(150km/h前後)を必ず超える。Lv4で150、Lv9で277km/h。
  - 単純に1回あたりの値を下げても、複利である限りLv9や組み合わせで再び大きくなる。
- **合成で117回まで重ねられる**: 安全上限900km/hまで簡単に届く。
- **速さを上げる価値が戦闘とつながっていない**
  - 攻撃側の速度依存(MOMENTUM)は「距離」で決まり、カードの速さとは無関係。
  - 一方で、走る速さが上がると一部の技の溜めが短くなる(`KitWindupScale = 7.5/走行速度`)。
- **短時間Buffにした場合**
  - 「取った瞬間に加速して気持ちいい」は残せる。
  - 恒常の最高速は自然加速の100km/h+小さな上乗せに収められる。
  - 速度に依存するもの(高速補助、カメラ、敵の間隔、障害物の先読み)は、すでに一時的な速度変化(スロー/加速)を扱える作りになっている。
- **代わりに必要なこと**
  - 恒常の上乗せは加算(例: +2%/Lv、Lv9で+18%)にして複利をやめる。
  - GREED / NO TURNING BACK の欠点(敵の出現頻度)を実際に効かせるか、別の欠点へ変える(今は欠点が無い)。


## 全カードの仮分類(次の調整用。最終判断ではない)

1枚に複数の問題がある時は、重い方を主分類にした(全部の分類は下の全カード表の「判定」列)。

| 分類 | 意味 | 枚数(主分類) | カード |
|---|---|---|---|
| **BROKEN** | 数式/複利/上限/仕様の食い違いで想定外の値 | 13(該当 13) | BERSERKER, HEAVY ARMOR, GREED, GLASS CANNON, MOMENTUM, DOUBLE ATTACK, RAPID EDGE, FORTRESS, OVERDRIVE, REVERSE GEAR, HEART BREAKER, DEATH'S CONTRACT, NO TURNING BACK |
| **NO EFFECT** | 実装上、実質効果がない | 3(該当 3) | MORE ENEMIES, HORDE, SECOND WIND |
| **MISMATCH** | 説明と実装が一致しない | 21(該当 21) | BRAKE ATTACK, FIRST STRIKE, COMBO PLUS, TOUGH ENEMIES, FAST ENEMIES, COMBO EDGE, MONSTER RUSH, ELITE ENEMIES, CHAIN EXPLOSION, THUNDER STRIKE, SONIC BLADE, COMBO MASTER, HIGH VOLTAGE, FLAME BLADE, FROST EDGE, WIND CUTTER, HELL MODE, WANTED, PANDEMONIUM, FLAME COUNTER, COMBO RUSH |
| **TOO STRONG** | Lv5〜Lv9付近で明確に過剰(攻撃はLv9で基礎20の×4以上) | 30(該当 50) | SPEED UP, ATTACK UP, JUMP POWER UP, ATTACK SPEED UP, AIR ATTACK UP, MOB KILLER, GROUND FIGHTER, PIERCING BLADE, AERIAL BLADE, COUNTER, LAST STAND, BLOOD RUSH, IRON WILL, BOSS KILLER, ADRENALINE, SKY RUNNER, GIANT SLAYER, LONG BLADE, SKY MASTER, PERFECT GUARD, BLOOD BLADE, BERSERK DRIVE, HEAVY IMPACT, AIR DOMINION, GROUND ZERO, SKYBOUND, ULTIMATE, GROUND BREAKER, AIR STRIKE, HUNTER |
| **WATCH** | 単体では大きくないが、組み合わせ/上限で危険 | 14(該当 26) | SPEED DOWN, JUMP COUNT UP, HEART UP, ATTACK RANGE UP, SHIELD, VAMPIRE, SHOCKWAVE, PREDATOR, BOSS CHALLENGE, EXP CONVERTER, BOSS RUSH, PHOENIX, LAST CHANCE, CLOSE CALL |
| **OK** | Lv9まで育てても大きく壊さない(主に成長/報酬) | 10(該当 10) | EXP UP, PATHFINDER, EXECUTIONER, TREASURE HUNTER, OVERHEAL, LONG HAUL, LEVEL BREAK, EXPERIENCE BURST, ONE MORE MILE, THE LONG ROAD |

## 全カード Lv1 / Lv5 / Lv9(ゲーム内で実測、黒剣士: 基礎 攻撃20 / 最大HP50 / runSpeed5 / ジャンプ力9 / ジャンプ2回)

攻撃倍率 = 基礎攻撃20に対する1発の倍率(そのカードの条件を満たした時: 空中/地上の大きい方、初撃/締めの大きい方、満HP/瀕死の大きい方、MOMENTUMは3.6km以降の×4.556、ボス特効込み)。

| # | ID | 名前 | R | 分類 | 実装効果(1回分) | Lv1 | Lv5 | Lv9 | 方式 | 攻撃倍率 Lv1/5/9 | 判定 | メモ |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | speed_up | SPEED UP | 1 | Movement | MoveSpeed +0.12 | 速度×1.12 | 速度×1.76 | 速度×2.77 | 複利 | - | **TOO STRONG** | 移動速度が複利。Lv4で150km/h超(自然上限100km/hから)、Lv9 ×2.77 |
| 2 | speed_down | SPEED DOWN | 1 | Movement | MoveSpeed -0.12 | 速度×0.88 | 速度×0.53 | 速度×0.32 | 複利 | - | **WATCH** | 速度 Lv9 ×0.32(Riskカードとしては極端) |
| 3 | attack_up | ATTACK UP | 1 | Attack | AttackPower +10 | 攻撃+10 | 攻撃+50 | 攻撃+90 | 加算 | ×1.50/×3.50/×5.50 | **TOO STRONG** | 攻撃: 1枚Lv9で基礎(攻撃20)の×5.5(Lv1 ×1.50 / Lv5 ×3.50) |
| 4 | jump_power_up | JUMP POWER UP | 1 | Movement | JumpPower +0.15 | ジャンプ力×1.15 | ジャンプ力×2.01 | ジャンプ力×3.52 | 複利 | - | **TOO STRONG** | ジャンプ力が複利(Lv9 ×3.52、高さ約×12.4)。魔法/忍者/吸血鬼には効かない |
| 5 | jump_count_up | JUMP COUNT UP | 2 | Movement | JumpCount +1 | ジャンプ回数3 | ジャンプ回数7 | ジャンプ回数11 | 加算 | - | **WATCH** | ジャンプ回数に上限なし(Lv9で+9 = 11回)。魔法には効かない |
| 6 | heart_up | HEART UP | 1 | Defense | MaxHp +10 | 最大HP60 | 最大HP100 | 最大HP100 | 加算(上限100) | - | **WATCH** | 最大HPは上限100(ハート10)。基礎50のキャラはLv5で上限(Lv6〜9は効果なし) |
| 7 | attack_range_up | ATTACK RANGE UP | 1 | Attack | AttackRange +0.25 | 範囲×1.25 | 範囲×2.25 | 範囲×3.25 | 加算 | - | **WATCH** | 攻撃範囲 Lv9 ×3.25(上限なし)。飛び道具には効かない |
| 8 | attack_speed_up | ATTACK SPEED UP | 1 | Attack | AttackSpeed +0.15 | 攻撃時間×0.85 | 攻撃時間×0.44 | 攻撃時間×0.25 | 複利 | - | **TOO STRONG / WATCH** | 攻撃時間 Lv9 ×0.25(テンポ約×4.0) |
| 9 | air_attack_up | AIR ATTACK UP | 2 | Attack | AirAttackPower +20 | 空中+20 | 空中+100 | 空中+180 | 加算 | ×2.00/×6.00/×10.00 | **TOO STRONG** | 魔法は高度1以上で常に「空中」扱い(空中攻撃が常時) / 攻撃: 1枚Lv9で基礎(攻撃20)の×10.0(Lv1 ×2.00 / Lv5 ×6.00) |
| 10 | shield | SHIELD | 2 | Defense | Shield +1 | Shield1 | Shield5 | Shield9 | 加算 | - | **WATCH** | Shield 1回/Lv(Lv9で9回、回復しない) |
| 11 | exp_up | EXP UP | 1 | Growth | ExpGain +0.2 | EXP×1.20 | EXP×2.00 | EXP×2.80 | 加算 | - | **OK** | 成長/報酬(戦闘力に直接は効かない) |
| 12 | vampire | VAMPIRE | 3 | Heal | LifestealChance +0.15, LifestealAmount +10 | 吸収率0.15, 吸収量10 | 吸収率0.75, 吸収量50 | 吸収率1, 吸収量90 | 加算/加算(上限1.0) | - | **WATCH** | 吸収確率がLv7で100%(以降は確率が増えない) |
| 13 | berserker | BERSERKER | 3 | Risk | AttackPower +30, MaxHp -10 | 攻撃+30, 最大HP40 | 攻撃+150, 最大HP1 | 攻撃+270, 最大HP1 | 加算/加算(上限100) | ×2.50/×8.50/×14.50 | **BROKEN / TOO STRONG** | 最大HPの欠点が下限1で止まり(Lv5で最大HP1)、その後は攻撃だけ増える。Lv9 攻撃+270 / 攻撃: 1枚Lv9で基礎(攻撃20)の×14.5(Lv1 ×2.50 / Lv5 ×8.50) |
| 14 | heavy_armor | HEAVY ARMOR | 3 | Risk | MaxHp +30, JumpPower -0.15 | ジャンプ力×0.85, 最大HP80 | ジャンプ力×0.44, 最大HP100 | ジャンプ力×0.23, 最大HP100 | 加算(上限100)/複利 | - | **BROKEN** | ジャンプ力が複利で減る(Lv9 ×0.23、跳べる高さは約×0.05)。最大HPは上限100で頭打ち |
| 15 | greed | GREED | 3 | Risk | MoveSpeed +0.2, EnemySpawnRate +0.3 | 速度×1.20, 出現率(無効)×1.30 | 速度×2.49, 出現率(無効)×2.50 | 速度×5.16, 出現率(無効)×3.70 | 加算/複利 | - | **BROKEN** | 移動速度が複利(Lv9 ×5.16)。欠点の「敵の出現頻度」は効いていない(デメリットなしの速度カード) |
| 16 | pathfinder | PATHFINDER | 2 | Growth | ExpGain +0.1 | EXP×1.10 | EXP×1.50 | EXP×1.90 | 加算 | - | **OK** | 成長/報酬(戦闘力に直接は効かない) |
| 17 | more_enemies | MORE ENEMIES | 1 | Risk | EnemySpawnRate +0.15 | 出現率(無効)×1.15 | 出現率(無効)×1.75 | 出現率(無効)×2.35 | 加算 | - | **NO EFFECT** | 敵出現率は全4ステージ(EncounterDirector)で使われない(EnemyWallManagerの壁だけ。壁もProfileで止まっている) |
| 18 | brake_attack | BRAKE ATTACK | 2 | Attack | AttackPower +10, MoveSpeed -0.05 | 速度×0.95, 攻撃+10 | 速度×0.77, 攻撃+50 | 速度×0.63, 攻撃+90 | 加算/複利 | ×1.50/×3.50/×5.50 | **MISMATCH / TOO STRONG** | 説明は「攻撃後わずかに減速」だが実装は常時の速度低下(Lv9 ×0.63) / 攻撃: 1枚Lv9で基礎(攻撃20)の×5.5(Lv1 ×1.50 / Lv5 ×3.50) |
| 19 | first_strike | FIRST STRIKE | 2 | Attack | FirstHitBonus +20 | 初撃+20 | 初撃+100 | 初撃+180 | 加算 | ×2.00/×6.00/×10.00 | **MISMATCH / TOO STRONG** | 初撃: 弓/魔法/忍者/巫女には乗らない。お嬢様騎士は毎回、竜騎士は石突き/下突き/急降下で毎回 / 攻撃: 1枚Lv9で基礎(攻撃20)の×10.0(Lv1 ×2.00 / Lv5 ×6.00) |
| 20 | executioner | EXECUTIONER | 2 | Risk | MileGainMultiplier +0.15 | MILE×1.15 | MILE×1.75 | MILE×2.35 | 加算 | - | **OK** | 成長/報酬(戦闘力に直接は効かない) |
| 21 | treasure_hunter | TREASURE HUNTER | 2 | Growth | MileGainMultiplier +0.2 | MILE×1.20 | MILE×2.00 | MILE×2.80 | 加算 | - | **OK** | 成長/報酬(戦闘力に直接は効かない) |
| 22 | combo_plus | COMBO PLUS | 2 | Attack | ComboFinalStageBonus +10 | 締め+10 | 締め+50 | 締め+90 | 加算 | ×1.50/×3.50/×5.50 | **MISMATCH / TOO STRONG** | 締めの威力: 弓/魔法/忍者/巫女には乗らない(連撃の段が設定されない)。お嬢様騎士は毎回乗る。竜騎士は2段目と3段目に乗る / 攻撃: 1枚Lv9で基礎(攻撃20)の×5.5(Lv1 ×1.50 / Lv5 ×3.50) |
| 23 | mob_killer | MOB KILLER | 2 | Attack | AttackPower +10, MileGainMultiplier +0.1 | 攻撃+10, MILE×1.10 | 攻撃+50, MILE×1.50 | 攻撃+90, MILE×1.90 | 加算 | ×1.50/×3.50/×5.50 | **TOO STRONG** | 攻撃: 1枚Lv9で基礎(攻撃20)の×5.5(Lv1 ×1.50 / Lv5 ×3.50) |
| 24 | ground_fighter | GROUND FIGHTER | 2 | Attack | GroundAttackPower +20 | 地上+20 | 地上+100 | 地上+180 | 加算 | ×2.00/×6.00/×10.00 | **TOO STRONG** | 攻撃: 1枚Lv9で基礎(攻撃20)の×10.0(Lv1 ×2.00 / Lv5 ×6.00) |
| 25 | tough_enemies | TOUGH ENEMIES | 2 | Risk | EnemyHpMultiplier +0.3, MileGainMultiplier +0.15 | 敵HP×1.30, MILE×1.15 | 敵HP×2.50, MILE×1.75 | 敵HP×3.70, MILE×2.35 | 加算 | - | **MISMATCH** | 説明は「経験値/MILE」だがEXPは増えない(MILEだけ) |
| 26 | fast_enemies | FAST ENEMIES | 2 | Risk | EnemySpawnRate +0.1, MileGainMultiplier +0.15 | 出現率(無効)×1.10, MILE×1.15 | 出現率(無効)×1.50, MILE×1.75 | 出現率(無効)×1.90, MILE×2.35 | 加算 | - | **MISMATCH** | 「敵の行動が活発になる」は実装なし(出現率=無効)。説明の経験値も増えない(MILEだけ) |
| 27 | combo_edge | COMBO EDGE | 3 | Attack | ComboFinalStageBonus +30 | 締め+30 | 締め+150 | 締め+270 | 加算 | ×2.50/×8.50/×14.50 | **MISMATCH / TOO STRONG** | 締めの威力: 弓/魔法/忍者/巫女には乗らない(連撃の段が設定されない)。お嬢様騎士は毎回乗る。竜騎士は2段目と3段目に乗る / 攻撃: 1枚Lv9で基礎(攻撃20)の×14.5(Lv1 ×2.50 / Lv5 ×8.50) |
| 28 | piercing_blade | PIERCING BLADE | 3 | Attack | AttackRange +0.3, AttackPower +10 | 攻撃+10, 範囲×1.30 | 攻撃+50, 範囲×2.50 | 攻撃+90, 範囲×3.70 | 加算 | ×1.50/×3.50/×5.50 | **TOO STRONG / WATCH** | 攻撃範囲 Lv9 ×3.70(上限なし)。飛び道具には効かない / 攻撃: 1枚Lv9で基礎(攻撃20)の×5.5(Lv1 ×1.50 / Lv5 ×3.50) |
| 29 | shockwave | SHOCKWAVE | 3 | Attack | AttackRange +0.4 | 範囲×1.40 | 範囲×3.00 | 範囲×4.60 | 加算 | - | **WATCH** | 攻撃範囲 Lv9 ×4.60(上限なし)。飛び道具には効かない |
| 30 | aerial_blade | AERIAL BLADE | 3 | Attack | AirAttackPower +30 | 空中+30 | 空中+150 | 空中+270 | 加算 | ×2.50/×8.50/×14.50 | **TOO STRONG** | 魔法は高度1以上で常に「空中」扱い(空中攻撃が常時) / 攻撃: 1枚Lv9で基礎(攻撃20)の×14.5(Lv1 ×2.50 / Lv5 ×8.50) |
| 31 | counter | COUNTER | 3 | Defense | Shield +1, AttackPower +10 | 攻撃+10, Shield1 | 攻撃+50, Shield5 | 攻撃+90, Shield9 | 加算 | ×1.50/×3.50/×5.50 | **TOO STRONG / WATCH** | 攻撃: 1枚Lv9で基礎(攻撃20)の×5.5(Lv1 ×1.50 / Lv5 ×3.50) / Shield 1回/Lv(Lv9で9回、回復しない) |
| 32 | last_stand | LAST STAND | 3 | Risk | LowHpAttackBonus +40 | 瀕死+40 | 瀕死+200 | 瀕死+360 | 加算 | ×3.00/×11.00/×19.00 | **TOO STRONG / WATCH** | 瀕死時の攻撃 = 値×減っている割合(HP1/最大で最大) / 攻撃: 1枚Lv9で基礎(攻撃20)の×19.0(Lv1 ×3.00 / Lv5 ×11.00) |
| 33 | overheal | OVERHEAL | 3 | Heal | LifestealAmount +10, LifestealChance +0.1 | 吸収率0.1, 吸収量10 | 吸収率0.5, 吸収量50 | 吸収率0.9, 吸収量90 | 加算/加算(上限1.0) | - | **OK** |  |
| 34 | blood_rush | BLOOD RUSH | 3 | Heal | LowHpAttackBonus +20, LifestealChance +0.1 | 瀕死+20, 吸収率0.1 | 瀕死+100, 吸収率0.5 | 瀕死+180, 吸収率0.9 | 加算/加算(上限1.0) | ×2.00/×6.00/×10.00 | **TOO STRONG / WATCH** | 瀕死時の攻撃 = 値×減っている割合(HP1/最大で最大) / 攻撃: 1枚Lv9で基礎(攻撃20)の×10.0(Lv1 ×2.00 / Lv5 ×6.00) |
| 35 | predator | PREDATOR | 3 | Heal | LifestealChance +0.2 | 吸収率0.2 | 吸収率1 | 吸収率1 | 加算(上限1.0) | - | **WATCH** | 吸収確率がLv5で100%(以降は確率が増えない) |
| 36 | glass_cannon | GLASS CANNON | 3 | Risk | AttackPower +40, MaxHp -20 | 攻撃+40, 最大HP30 | 攻撃+200, 最大HP1 | 攻撃+360, 最大HP1 | 加算/加算(上限100) | ×3.00/×11.00/×19.00 | **BROKEN / TOO STRONG** | 最大HPの欠点が下限1で止まり(Lv5で最大HP1)、その後は攻撃だけ増える。Lv9 攻撃+360 / 攻撃: 1枚Lv9で基礎(攻撃20)の×19.0(Lv1 ×3.00 / Lv5 ×11.00) |
| 37 | iron_will | IRON WILL | 3 | Defense | MaxHp +20, FullHpAttackBonus +20 | 満HP+20, 最大HP70 | 満HP+100, 最大HP100 | 満HP+180, 最大HP100 | 加算/加算(上限100) | ×2.00/×6.00/×10.00 | **TOO STRONG / WATCH** | 満HP時の攻撃: 最大HPを下げるカード(下限1)と組むと常に満HPになりやすい / 攻撃: 1枚Lv9で基礎(攻撃20)の×10.0(Lv1 ×2.00 / Lv5 ×6.00) |
| 38 | long_haul | LONG HAUL | 3 | Growth | ExpGain +0.15, MaxHp +10 | 最大HP60, EXP×1.15 | 最大HP100, EXP×1.75 | 最大HP100, EXP×2.35 | 加算/加算(上限100) | - | **OK** | 成長/報酬(戦闘力に直接は効かない) |
| 39 | boss_killer | BOSS KILLER | 3 | Attack | BossDamageBonus +30 | ボス+30 | ボス+150 | ボス+270 | 加算 | ×2.50/×8.50/×14.50 | **TOO STRONG** | 攻撃: 1枚Lv9で基礎(攻撃20)の×14.5(Lv1 ×2.50 / Lv5 ×8.50) |
| 40 | adrenaline | ADRENALINE | 3 | Risk | LowHpAttackBonus +30, AttackSpeed +0.1 | 瀕死+30, 攻撃時間×0.90 | 瀕死+150, 攻撃時間×0.59 | 瀕死+270, 攻撃時間×0.39 | 加算/複利 | ×2.50/×8.50/×14.50 | **TOO STRONG / WATCH** | 瀕死時の攻撃 = 値×減っている割合(HP1/最大で最大) / 攻撃: 1枚Lv9で基礎(攻撃20)の×14.5(Lv1 ×2.50 / Lv5 ×8.50) |
| 41 | sky_runner | SKY RUNNER | 3 | Movement | JumpPower +0.15, AirAttackPower +20 | ジャンプ力×1.15, 空中+20 | ジャンプ力×2.01, 空中+100 | ジャンプ力×3.52, 空中+180 | 加算/複利 | ×2.00/×6.00/×10.00 | **TOO STRONG** | 魔法は高度1以上で常に「空中」扱い(空中攻撃が常時) / 攻撃: 1枚Lv9で基礎(攻撃20)の×10.0(Lv1 ×2.00 / Lv5 ×6.00) |
| 42 | monster_rush | MONSTER RUSH | 3 | Risk | EnemySpawnRate +0.25, ExpGain +0.15 | EXP×1.15, 出現率(無効)×1.25 | EXP×1.75, 出現率(無効)×2.25 | EXP×2.35, 出現率(無効)×3.25 | 加算 | - | **MISMATCH** | 敵出現率の部分が無効(EXPだけ効く)。説明の「敵の出現頻度が大きく上がる」は起きない |
| 43 | elite_enemies | ELITE ENEMIES | 3 | Risk | EnemyHpMultiplier +0.5, MileGainMultiplier +0.3 | 敵HP×1.50, MILE×1.30 | 敵HP×3.50, MILE×2.50 | 敵HP×5.50, MILE×3.70 | 加算 | - | **MISMATCH** | 説明は「経験値/MILE」だがEXPは増えない(MILEだけ) |
| 44 | horde | HORDE | 3 | Risk | EnemySpawnRate +0.3 | 出現率(無効)×1.30 | 出現率(無効)×2.50 | 出現率(無効)×3.70 | 加算 | - | **NO EFFECT** | 敵出現率は全4ステージ(EncounterDirector)で使われない(EnemyWallManagerの壁だけ。壁もProfileで止まっている) |
| 45 | boss_challenge | BOSS CHALLENGE | 3 | Risk | BossHpMultiplier +0.3, BossMileGainMultiplier +0.4 | ボスHP×1.30, ボスMILE×1.40 | ボスHP×2.50, ボスMILE×3.00 | ボスHP×3.70, ボスMILE×4.60 | 加算 | - | **WATCH** | ボスHP Lv9 ×3.7(報酬はボスMILE) |
| 46 | momentum | MOMENTUM | 4 | Movement | MomentumBonus +30 | 加速+30 | 加速+150 | 加速+270 | 加算 | ×7.83/×35.17/×62.50 | **BROKEN / TOO STRONG** | 攻撃 +値×(速度倍率-1)。速度倍率は距離だけで3.6km以降ずっと最大(×4.56) → Lv9で攻撃+1230(ATTACK UP Lv9の14倍) / 攻撃: 1枚Lv9で基礎(攻撃20)の×62.5(Lv1 ×7.83 / Lv5 ×35.17) |
| 47 | second_wind | SECOND WIND | 4 | Heal | LifestealAmount +20 | 吸収量20 | 吸収量100 | 吸収量180 | 加算 | - | **NO EFFECT** | 吸収量だけ。吸収確率が0だと発動しない(単体では効果なし) |
| 48 | chain_explosion | CHAIN EXPLOSION | 4 | Attack | AttackRange +0.3, ComboFinalStageBonus +20 | 締め+20, 範囲×1.30 | 締め+100, 範囲×2.50 | 締め+180, 範囲×3.70 | 加算 | ×2.00/×6.00/×10.00 | **MISMATCH / TOO STRONG / WATCH** | 攻撃範囲 Lv9 ×3.70(上限なし)。飛び道具には効かない / 締めの威力: 弓/魔法/忍者/巫女には乗らない(連撃の段が設定されない)。お嬢様騎士は毎回乗る。竜騎士は2段目と3段目に乗る / 攻撃: 1枚Lv9で基礎(攻撃20)の×10.0(Lv1 ×2.00 / Lv5 ×6.00) |
| 49 | thunder_strike | THUNDER STRIKE | 4 | Attack | AttackPower +20 | 攻撃+20 | 攻撃+100 | 攻撃+180 | 加算 | ×2.00/×6.00/×10.00 | **MISMATCH / TOO STRONG** | 属性(Thunder)の効果は未実装(表示だけ)。数値部分は効く / 攻撃: 1枚Lv9で基礎(攻撃20)の×10.0(Lv1 ×2.00 / Lv5 ×6.00) |
| 50 | level_break | LEVEL BREAK | 4 | Growth | ExpGain +0.3 | EXP×1.30 | EXP×2.50 | EXP×3.70 | 加算 | - | **OK** | 成長/報酬(戦闘力に直接は効かない) |
| 51 | double_attack | DOUBLE ATTACK | 4 | Attack | AttackSpeed +0.25 | 攻撃時間×0.75 | 攻撃時間×0.25 | 攻撃時間×0.25 | 複利 | - | **BROKEN** | 攻撃時間が下限0.25に Lv5 で到達(Lv6〜9は効果なし)。攻撃テンポ×4 |
| 52 | sonic_blade | SONIC BLADE | 4 | Attack | FirstHitBonus +20, AttackSpeed +0.1 | 初撃+20, 攻撃時間×0.90 | 初撃+100, 攻撃時間×0.59 | 初撃+180, 攻撃時間×0.39 | 加算/複利 | ×2.00/×6.00/×10.00 | **MISMATCH / TOO STRONG** | 初撃: 弓/魔法/忍者/巫女には乗らない。お嬢様騎士は毎回、竜騎士は石突き/下突き/急降下で毎回 / 攻撃: 1枚Lv9で基礎(攻撃20)の×10.0(Lv1 ×2.00 / Lv5 ×6.00) |
| 53 | giant_slayer | GIANT SLAYER | 4 | Risk | AttackPower +50, AttackSpeed -0.15 | 攻撃+50, 攻撃時間×1.15 | 攻撃+250, 攻撃時間×2.01 | 攻撃+450, 攻撃時間×3.52 | 加算/複利 | ×3.50/×13.50/×23.50 | **TOO STRONG** | 攻撃+450 / 攻撃時間 Lv9 ×3.52(遅くなる) / 攻撃: 1枚Lv9で基礎(攻撃20)の×23.5(Lv1 ×3.50 / Lv5 ×13.50) |
| 54 | rapid_edge | RAPID EDGE | 4 | Attack | AttackSpeed +0.2 | 攻撃時間×0.80 | 攻撃時間×0.33 | 攻撃時間×0.25 | 複利 | - | **BROKEN** | 攻撃時間が下限0.25に Lv7 で到達(0.8^7=0.21、Lv8〜9は効果なし)。テンポ×4 |
| 55 | long_blade | LONG BLADE | 4 | Attack | AttackRange +0.5 | 範囲×1.50 | 範囲×3.50 | 範囲×5.50 | 加算 | - | **TOO STRONG** | 攻撃範囲 Lv9 ×5.5(上限なし)。飛び道具のキャラには効かない |
| 56 | sky_master | SKY MASTER | 4 | Attack | AirAttackPower +40, JumpPower +0.1 | ジャンプ力×1.10, 空中+40 | ジャンプ力×1.61, 空中+200 | ジャンプ力×2.36, 空中+360 | 加算/複利 | ×3.00/×11.00/×19.00 | **TOO STRONG** | 魔法は高度1以上で常に「空中」扱い(空中攻撃が常時) / 攻撃: 1枚Lv9で基礎(攻撃20)の×19.0(Lv1 ×3.00 / Lv5 ×11.00) / AIR STRIKE(R3 空中+40)とSKY MASTER(R4 空中+40/ジャンプ+10%)はほぼ同じ |
| 57 | fortress | FORTRESS | 4 | Risk | MaxHp +40, MoveSpeed -0.15 | 速度×0.85, 最大HP90 | 速度×0.44, 最大HP100 | 速度×0.23, 最大HP100 | 加算(上限100)/複利 | - | **BROKEN** | 移動速度が複利で減る(Lv9 ×0.23 = 自然上限でも23km/h) |
| 58 | perfect_guard | PERFECT GUARD | 4 | Defense | Shield +2 | Shield2 | Shield10 | Shield18 | 加算 | - | **TOO STRONG** | Shield +2/Lv → Lv9で18回(1回=被弾1回を丸ごと無効) |
| 59 | experience_burst | EXPERIENCE BURST | 4 | Growth | ExpGain +0.35 | EXP×1.35 | EXP×2.75 | EXP×4.15 | 加算 | - | **OK** | 成長/報酬(戦闘力に直接は効かない) |
| 60 | blood_blade | BLOOD BLADE | 4 | Heal | FullHpAttackBonus +30 | 満HP+30 | 満HP+150 | 満HP+270 | 加算 | ×2.50/×8.50/×14.50 | **TOO STRONG / WATCH** | 満HP時の攻撃: 最大HPを下げるカード(下限1)と組むと常に満HPになりやすい / 攻撃: 1枚Lv9で基礎(攻撃20)の×14.5(Lv1 ×2.50 / Lv5 ×8.50) |
| 61 | berserk_drive | BERSERK DRIVE | 4 | Risk | LowHpAttackBonus +50 | 瀕死+50 | 瀕死+250 | 瀕死+450 | 加算 | ×3.50/×13.50/×23.50 | **TOO STRONG / WATCH** | 瀕死時の攻撃 = 値×減っている割合(HP1/最大で最大) / 攻撃: 1枚Lv9で基礎(攻撃20)の×23.5(Lv1 ×3.50 / Lv5 ×13.50) |
| 62 | heavy_impact | HEAVY IMPACT | 4 | Attack | GroundAttackPower +40 | 地上+40 | 地上+200 | 地上+360 | 加算 | ×3.00/×11.00/×19.00 | **TOO STRONG** | 攻撃: 1枚Lv9で基礎(攻撃20)の×19.0(Lv1 ×3.00 / Lv5 ×11.00) / GROUND BREAKER(R3)とHEAVY IMPACT(R4)は同じ「地上+40」(重複) |
| 63 | air_dominion | AIR DOMINION | 4 | Attack | AirAttackPower +30, GroundAttackPower +10 | 空中+30, 地上+10 | 空中+150, 地上+50 | 空中+270, 地上+90 | 加算 | ×2.50/×8.50/×14.50 | **TOO STRONG** | 魔法は高度1以上で常に「空中」扱い(空中攻撃が常時) / 攻撃: 1枚Lv9で基礎(攻撃20)の×14.5(Lv1 ×2.50 / Lv5 ×8.50) |
| 64 | combo_master | COMBO MASTER | 4 | Attack | ComboFinalStageBonus +50 | 締め+50 | 締め+250 | 締め+450 | 加算 | ×3.50/×13.50/×23.50 | **MISMATCH / TOO STRONG** | 締めの威力: 弓/魔法/忍者/巫女には乗らない(連撃の段が設定されない)。お嬢様騎士は毎回乗る。竜騎士は2段目と3段目に乗る / 攻撃: 1枚Lv9で基礎(攻撃20)の×23.5(Lv1 ×3.50 / Lv5 ×13.50) |
| 65 | high_voltage | HIGH VOLTAGE | 4 | Attack | AttackPower +30 | 攻撃+30 | 攻撃+150 | 攻撃+270 | 加算 | ×2.50/×8.50/×14.50 | **MISMATCH / TOO STRONG** | 属性(Thunder)の効果は未実装(表示だけ)。数値部分は効く / 攻撃: 1枚Lv9で基礎(攻撃20)の×14.5(Lv1 ×2.50 / Lv5 ×8.50) |
| 66 | overdrive | OVERDRIVE | 4 | Movement | MomentumBonus +40 | 加速+40 | 加速+200 | 加速+360 | 加算 | ×10.11/×46.56/×83.00 | **BROKEN / TOO STRONG** | 攻撃 +値×(速度倍率-1)。速度倍率は距離だけで3.6km以降ずっと最大(×4.56) → Lv9で攻撃+1640(ATTACK UP Lv9の18倍) / 攻撃: 1枚Lv9で基礎(攻撃20)の×83.0(Lv1 ×10.11 / Lv5 ×46.56) |
| 67 | reverse_gear | REVERSE GEAR | 4 | Risk | MoveSpeed -0.15, AttackPower +30 | 速度×0.85, 攻撃+30 | 速度×0.44, 攻撃+150 | 速度×0.23, 攻撃+270 | 加算/複利 | ×2.50/×8.50/×14.50 | **BROKEN / TOO STRONG** | 移動速度が複利で減る(Lv9 ×0.23 = 自然上限でも23km/h) / 攻撃: 1枚Lv9で基礎(攻撃20)の×14.5(Lv1 ×2.50 / Lv5 ×8.50) |
| 68 | heart_breaker | HEART BREAKER | 4 | Risk | MaxHp -20, AttackPower +40 | 攻撃+40, 最大HP30 | 攻撃+200, 最大HP1 | 攻撃+360, 最大HP1 | 加算/加算(上限100) | ×3.00/×11.00/×19.00 | **BROKEN / TOO STRONG** | 最大HPの欠点が下限1で止まり(Lv5で最大HP1)、その後は攻撃だけ増える。Lv9 攻撃+360 / 攻撃: 1枚Lv9で基礎(攻撃20)の×19.0(Lv1 ×3.00 / Lv5 ×11.00) |
| 69 | exp_converter | EXP CONVERTER | 4 | Risk | ExpGain -0.1, MileGainMultiplier +0.3 | EXP×0.90, MILE×1.30 | EXP×0.50, MILE×2.50 | EXP×0.10, MILE×3.70 | 加算 | - | **WATCH** | EXP倍率 Lv9 ×0.1。合成で同じ能力を重ねると0以下になり経験値が減る(下限なし) |
| 70 | ground_zero | GROUND ZERO | 4 | Risk | AirAttackPower -20, GroundAttackPower +50 | 空中-20, 地上+50 | 空中-100, 地上+250 | 空中-180, 地上+450 | 加算 | ×3.50/×13.50/×23.50 | **TOO STRONG** | 攻撃: 1枚Lv9で基礎(攻撃20)の×23.5(Lv1 ×3.50 / Lv5 ×13.50) |
| 71 | skybound | SKYBOUND | 4 | Risk | GroundAttackPower -20, AirAttackPower +50 | 空中+50, 地上-20 | 空中+250, 地上-100 | 空中+450, 地上-180 | 加算 | ×3.50/×13.50/×23.50 | **TOO STRONG / WATCH** | 魔法は高度1以上で常に「空中」扱い(空中攻撃が常時) / 攻撃: 1枚Lv9で基礎(攻撃20)の×23.5(Lv1 ×3.50 / Lv5 ×13.50) |
| 72 | flame_blade | FLAME BLADE | 4 | Attack | AttackPower +20 | 攻撃+20 | 攻撃+100 | 攻撃+180 | 加算 | ×2.00/×6.00/×10.00 | **MISMATCH / TOO STRONG** | 属性(Fire)の効果は未実装(表示だけ)。数値部分は効く / 攻撃: 1枚Lv9で基礎(攻撃20)の×10.0(Lv1 ×2.00 / Lv5 ×6.00) |
| 73 | frost_edge | FROST EDGE | 4 | Attack | AttackSpeed +0.1, AttackPower +10 | 攻撃+10, 攻撃時間×0.90 | 攻撃+50, 攻撃時間×0.59 | 攻撃+90, 攻撃時間×0.39 | 加算/複利 | ×1.50/×3.50/×5.50 | **MISMATCH / TOO STRONG** | Slow/Freezeは未実装(表示だけ)。攻撃速度/攻撃力は効く / 攻撃: 1枚Lv9で基礎(攻撃20)の×5.5(Lv1 ×1.50 / Lv5 ×3.50) |
| 74 | wind_cutter | WIND CUTTER | 4 | Attack | AttackRange +0.35 | 範囲×1.35 | 範囲×2.75 | 範囲×4.15 | 加算 | - | **MISMATCH** | 属性(Wind)の効果は未実装(表示だけ)。数値部分は効く |
| 75 | hell_mode | HELL MODE | 4 | Risk | EnemyHpMultiplier +0.6, EnemySpawnRate +0.4, MileGainMultiplier +0.5, BossMileGainMultiplier +0.3 | 出現率(無効)×1.40, 敵HP×1.60, MILE×1.50, ボスMILE×1.30 | 出現率(無効)×3.00, 敵HP×4.00, MILE×3.50, ボスMILE×2.50 | 出現率(無効)×4.60, 敵HP×6.40, MILE×5.50, ボスMILE×3.70 | 加算 | - | **MISMATCH** | 出現率は無効、EXPは増えない(敵HP/MILEだけ) |
| 76 | boss_rush | BOSS RUSH | 4 | Risk | BossHpMultiplier +0.4, BossMileGainMultiplier +0.5 | ボスHP×1.40, ボスMILE×1.50 | ボスHP×3.00, ボスMILE×3.50 | ボスHP×4.60, ボスMILE×5.50 | 加算 | - | **WATCH** | ボスHP Lv9 ×4.6(報酬はボスMILE) |
| 77 | wanted | WANTED | 4 | Risk | MileGainMultiplier +0.35 | MILE×1.35 | MILE×2.75 | MILE×4.15 | 加算 | - | **MISMATCH** | 「追跡者・急襲者が出やすくなる」は実装なし(MILEだけ) |
| 78 | phoenix | PHOENIX | 5 | Heal | LifestealChance +0.3, LifestealAmount +20 | 吸収率0.3, 吸収量20 | 吸収率1, 吸収量100 | 吸収率1, 吸収量180 | 加算/加算(上限1.0) | - | **WATCH** | 吸収確率がLv4で100%(以降は確率が増えない) |
| 79 | ultimate | ULTIMATE | 5 | Special | AttackPower +30, MaxHp +20, MoveSpeed +0.1 | 速度×1.10, 攻撃+30, 最大HP70 | 速度×1.61, 攻撃+150, 最大HP100 | 速度×2.36, 攻撃+270, 最大HP100 | 加算/加算(上限100)/複利 | ×2.50/×8.50/×14.50 | **TOO STRONG** | 攻撃+270/最大HP+180(上限100で頭打ち)/速度×2.36を1枚で / 攻撃: 1枚Lv9で基礎(攻撃20)の×14.5(Lv1 ×2.50 / Lv5 ×8.50) |
| 80 | one_more_mile | ONE MORE MILE | 5 | Special | MileGainMultiplier +0.5, BossMileGainMultiplier +0.5, ExpGain +0.2 | EXP×1.20, MILE×1.50, ボスMILE×1.50 | EXP×2.00, MILE×3.50, ボスMILE×3.50 | EXP×2.80, MILE×5.50, ボスMILE×5.50 | 加算 | - | **OK** | 成長/報酬(戦闘力に直接は効かない) |
| 81 | deaths_contract | DEATH'S CONTRACT | 5 | Risk | AttackPower +60, MaxHp -30 | 攻撃+60, 最大HP20 | 攻撃+300, 最大HP1 | 攻撃+540, 最大HP1 | 加算/加算(上限100) | ×4.00/×16.00/×28.00 | **BROKEN / TOO STRONG** | 最大HPの欠点が下限1で止まり(Lv5で最大HP1)、その後は攻撃だけ増える。Lv9 攻撃+540 / 攻撃: 1枚Lv9で基礎(攻撃20)の×28.0(Lv1 ×4.00 / Lv5 ×16.00) |
| 82 | no_turning_back | NO TURNING BACK | 5 | Risk | MoveSpeed +0.25, EnemySpawnRate +0.3 | 速度×1.25, 出現率(無効)×1.30 | 速度×3.05, 出現率(無効)×2.50 | 速度×7.45, 出現率(無効)×3.70 | 加算/複利 | - | **BROKEN** | 移動速度が複利(Lv9 ×7.45)。欠点の「敵の出現頻度」は効いていない(デメリットなしの速度カード) |
| 83 | the_long_road | THE LONG ROAD | 5 | Growth | ExpGain +0.4, MaxHp +20 | 最大HP70, EXP×1.40 | 最大HP100, EXP×3.00 | 最大HP100, EXP×4.60 | 加算/加算(上限100) | - | **OK** | 成長/報酬(戦闘力に直接は効かない) |
| 84 | pandemonium | PANDEMONIUM | 5 | Risk | EnemyHpMultiplier +0.8, EnemySpawnRate +0.6, MileGainMultiplier +0.6, BossMileGainMultiplier +0.6, BossHpMultiplier +0.5 | 出現率(無効)×1.60, 敵HP×1.80, ボスHP×1.50, MILE×1.60, ボスMILE×1.60 | 出現率(無効)×4.00, 敵HP×5.00, ボスHP×3.50, MILE×4.00, ボスMILE×4.00 | 出現率(無効)×6.40, 敵HP×8.20, ボスHP×5.50, MILE×6.40, ボスMILE×6.40 | 加算 | - | **MISMATCH** | 出現率は無効(敵HP/ボスHP/MILEは効く)。Lv9でボスHP ×5.5 |
| 85 | ground_breaker | GROUND BREAKER | 3 | Attack | GroundAttackPower +40 | 地上+40 | 地上+200 | 地上+360 | 加算 | ×3.00/×11.00/×19.00 | **TOO STRONG** | 攻撃: 1枚Lv9で基礎(攻撃20)の×19.0(Lv1 ×3.00 / Lv5 ×11.00) / GROUND BREAKER(R3)とHEAVY IMPACT(R4)は同じ「地上+40」(重複) |
| 86 | flame_counter | FLAME COUNTER | 3 | Defense | Shield +1, AttackPower +20 | 攻撃+20, Shield1 | 攻撃+100, Shield5 | 攻撃+180, Shield9 | 加算 | ×2.00/×6.00/×10.00 | **MISMATCH / TOO STRONG / WATCH** | 「反撃の威力」ではなく常時の攻撃力+20/Lv(Shieldも) / 攻撃: 1枚Lv9で基礎(攻撃20)の×10.0(Lv1 ×2.00 / Lv5 ×6.00) / Shield 1回/Lv(Lv9で9回、回復しない) |
| 87 | combo_rush | COMBO RUSH | 3 | Attack | AttackSpeed +0.15, ComboFinalStageBonus +20 | 締め+20, 攻撃時間×0.85 | 締め+100, 攻撃時間×0.44 | 締め+180, 攻撃時間×0.25 | 加算/複利 | ×2.00/×6.00/×10.00 | **MISMATCH / TOO STRONG** | 締めの威力: 弓/魔法/忍者/巫女には乗らない(連撃の段が設定されない)。お嬢様騎士は毎回乗る。竜騎士は2段目と3段目に乗る / 攻撃: 1枚Lv9で基礎(攻撃20)の×10.0(Lv1 ×2.00 / Lv5 ×6.00) |
| 88 | air_strike | AIR STRIKE | 3 | Attack | AirAttackPower +40 | 空中+40 | 空中+200 | 空中+360 | 加算 | ×3.00/×11.00/×19.00 | **TOO STRONG** | 魔法は高度1以上で常に「空中」扱い(空中攻撃が常時) / 攻撃: 1枚Lv9で基礎(攻撃20)の×19.0(Lv1 ×3.00 / Lv5 ×11.00) / AIR STRIKE(R3 空中+40)とSKY MASTER(R4 空中+40/ジャンプ+10%)はほぼ同じ |
| 89 | last_chance | LAST CHANCE | 3 | Defense | Shield +1, LifestealAmount +10 | Shield1, 吸収量10 | Shield5, 吸収量50 | Shield9, 吸収量90 | 加算 | - | **WATCH** | Shield 1回/Lv(Lv9で9回、回復しない) |
| 90 | close_call | CLOSE CALL | 3 | Movement | MoveSpeed +0.1, Shield +1 | 速度×1.10, Shield1 | 速度×1.61, Shield5 | 速度×2.36, Shield9 | 加算/複利 | - | **WATCH** | 速度複利 Lv9 ×2.36 + Shield 9 / Shield 1回/Lv(Lv9で9回、回復しない) |
| 91 | hunter | HUNTER | 3 | Attack | BossDamageBonus +40 | ボス+40 | ボス+200 | ボス+360 | 加算 | ×3.00/×11.00/×19.00 | **TOO STRONG** | 攻撃: 1枚Lv9で基礎(攻撃20)の×19.0(Lv1 ×3.00 / Lv5 ×11.00) |


## 速度

### カード1枚あたりの速度倍率(実測、黒剣士)

| カード | 1回 | Lv1 | Lv5 | Lv9 |
|---|---|---|---|---|
| SPEED UP | +12% | ×1.120 | ×1.762 | ×2.773 |
| SPEED DOWN | -12% | ×0.880 | ×0.528 | ×0.316 |
| GREED | +20% | ×1.200 | ×2.488 | ×5.160 |
| BRAKE ATTACK | -5% | ×0.950 | ×0.774 | ×0.630 |
| FORTRESS | -15% | ×0.850 | ×0.444 | ×0.232 |
| REVERSE GEAR | -15% | ×0.850 | ×0.444 | ×0.232 |
| ULTIMATE | +10% | ×1.100 | ×1.611 | ×2.358 |
| NO TURNING BACK | +25% | ×1.250 | ×3.052 | ×7.451 |
| CLOSE CALL | +10% | ×1.100 | ×1.611 | ×2.358 |

### 基礎速度ごとの最終速度(km/h)

| カード / Lv | 基礎50 | 基礎75 | 基礎100 | 基礎125 |
|---|---|---|---|---|
| SPEED UP Lv1 | 56 | 84 | 112 | 140 |
| SPEED UP Lv5 | 88 | 132 | 176 | 220 |
| SPEED UP Lv9 | 139 | 208 | 277 | 347 |
| GREED Lv1 | 60 | 90 | 120 | 150 |
| GREED Lv5 | 124 | 187 | 249 | 311 |
| GREED Lv9 | 258 | 387 | 516 | 645 |
| ULTIMATE Lv1 | 55 | 82 | 110 | 138 |
| ULTIMATE Lv5 | 81 | 121 | 161 | 201 |
| ULTIMATE Lv9 | 118 | 177 | 236 | 295 |
| NO TURNING BACK Lv1 | 62 | 94 | 125 | 156 |
| NO TURNING BACK Lv5 | 153 | 229 | 305 | 381 |
| NO TURNING BACK Lv9 | 373 | 559 | 745 | 931 |
| CLOSE CALL Lv1 | 55 | 82 | 110 | 138 |
| CLOSE CALL Lv5 | 81 | 121 | 161 | 201 |
| CLOSE CALL Lv9 | 118 | 177 | 236 | 295 |

### 150km/hを超える最初のLv(そのカード1枚だけ)

| カード | 基礎50 | 基礎75 | 基礎100 | 基礎125 |
|---|---|---|---|---|
| SPEED UP | 超えない | Lv7 | Lv4 | Lv2 |
| GREED | Lv7 | Lv4 | Lv3 | Lv2 |
| ULTIMATE | 超えない | Lv8 | Lv5 | Lv2 |
| NO TURNING BACK | Lv5 | Lv4 | Lv2 | Lv1 |
| CLOSE CALL | 超えない | Lv8 | Lv5 | Lv2 |

### 組み合わせ(自然加速の上限 100km/h から、キャラの速さ×1.00)

| 構成 | 倍率 | 100km/hから | 50km/hから | 双剣士(×1.15)100から |
|---|---|---|---|---|
| SPEED UP Lv9 | ×2.77 | 277 | 139 | 319 |
| SPEED UP Lv9 + GREED Lv9 | ×14.31 | 900 (安全上限900) | 715 | 900 |
| + NO TURNING BACK Lv9 | ×106.61 | 900 (安全上限900) | 900 | 900 |
| + CLOSE CALL Lv9 + ULTIMATE Lv9(速度5枚すべて) | ×592.72 | 900 (安全上限900) | 900 | 900 |
| SPEED UP Lv3 + GREED Lv2 | ×2.02 | 202 | 101 | 233 |
| SPEED UP Lv2 + GREED Lv1 + NO TURNING BACK Lv1 | ×1.88 | 188 | 94 | 216 |

### 速度特化(13枠)

- 速度が上がるカードは 5 種類(SPEED UP, GREED, ULTIMATE, NO TURNING BACK, CLOSE CALL)。全部Lv9で ×593 → 100km/hから 59,272km/h → 安全上限 900km/h で止まる。
- 合成カード(同じ能力を別のカードIDで重ねる)なら SPEED UP を13枠×9 = 117回重ねられる: ×5.73e+05 → 安全上限 900km/h。
- 走り始め(18km/h、自然加速なし)でも 速度5枚Lv9 なら 900km/h。


## 攻撃の最大構成(キャラ固有 + キャラカード3 + デッキ10 = 13枚、すべて別のカードでLv9)

13枚の効果はすべて足し算(攻撃時間だけ掛け算)なので、どの3枚をキャラカード枠(合成Lv9のカードでラン開始時に適用)、どの10枚をデッキ(素のカードを9回取得)にしても値は同じ。下の構成の先頭3枚をキャラカード枠と読んでよい。

条件: A=特殊条件なし(攻撃力+地上/空中だけ。初撃/締め/HP/速度/ボス特効なし) / B=ボス実戦(満HP・3.6km以降の速度・ボス特効・連撃の初撃/締め、BREAKなし、連撃1周の平均) / C=同時に成立する条件だけの最大の一撃(BREAK ×1.35込み)。
DPS = 連撃1周の合計 ÷ 1周の時間(全部当たる理論値。攻撃時間の下限0.25)。カードの値はゲーム内で実測(Lv9)。

| キャラ | 基礎攻撃 | カードなし 1発/DPS | A 恒常 1発 | A のDPS(攻撃速度込みで最大化) | B ボス実戦 1発(平均) | B DPS | C 単発最大 | Cの技 |
|---|---|---|---|---|---|---|---|---|
| 黒剣士 | 20 | 20 / 50 | 4,340 | 38,900(1周 0.30s) | 6,550 | 65,500 | 9,612(満HP) | 連撃3段目 |
| 双剣士 | 10 | 10 / 33 | 4,330 | 51,733(1周 0.38s) | 6,516 | 86,880 | 9,598(満HP) | 連撃5段目 |
| お嬢様騎士 | 10 | 10 / 19 | 4,330 | 28,741(1周 0.14s) | 6,840 | 50,667 | 9,598(満HP) | 突き(初撃+締め) |
| ガンスリンガー | 20 | 20 / 50 | 4,340 | 38,900(1周 0.30s) | 6,550 | 65,500 | 9,612(満HP) | 3発目 |
| 竜騎士 | 40 | 53 / 86 | 5,741 | 33,467(1周 0.46s) | 8,722 | 56,696 | 16,180(満HP) | 3段突き(×1.7) |
| 弓 | 20 | 40 / 60 | 8,680 | 13,414(1周 0.58s) | 12,980 | 22,379 | 29,981(満HP) | 最大溜め(×3.2) |
| 魔法 | 20 | 20 / 111 | 4,520 | 90,444(1周 0.09s) | 6,490 | 144,222 | 12,180(満HP) | 下の爆発(×1.3、空中) |
| 格闘 | 20 | 24 / 87 | 5,316 | 67,712(1周 0.28s) | 8,049 | 114,377 | 28,836(満HP) | 反撃の爆発(×3、締めの段の後) |
| 忍者 | 20 | 14 / 93 | 3,038 | 23,217(1周 0.24s) | 4,606 | 38,383 | 15,379(満HP) | 跳び蹴り/急降下(×1.6、前の連撃の締めが残る) |
| 巫女 | 20 | 23 / 81 | 4,919 | 62,981(1周 0.21s) | 7,355 | 105,076 | 20,612(満HP) | 札の起爆(×2.2) |
| 吸血鬼 | 20 | 27 / 114 | 5,902 | 87,977(1周 0.18s) | 8,941 | 148,692 | 19,994(満HP) | 3段目(×1.6×ラッシュ1.3) |
| 竜人 | 30 | 49 / 120 | 7,105 | 62,451(1周 0.31s) | 10,755 | 105,438 | 22,488(瀕死) | 爪3段目(×2.3) |

### 黒剣士
- A 恒常(1発): DEATH'S CONTRACT, GIANT SLAYER, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, BERSERKER, HIGH VOLTAGE, REVERSE GEAR, ULTIMATE, GROUND FIGHTER, THUNDER STRIKE
- A DPS: DEATH'S CONTRACT, ATTACK SPEED UP, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, BERSERKER, HIGH VOLTAGE, REVERSE GEAR, ULTIMATE, GROUND FIGHTER, THUNDER STRIKE
- B ボス実戦: OVERDRIVE, COMBO RUSH, MOMENTUM, DEATH'S CONTRACT, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, HUNTER, BERSERKER, BOSS KILLER, BLOOD BLADE  → 連撃の各段 [6490, 6490, 6670]、1周 0.30s
- C 単発最大(満HP): OVERDRIVE, MOMENTUM, DEATH'S CONTRACT, GIANT SLAYER, COMBO MASTER, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, HUNTER, BERSERKER, COMBO EDGE

### 双剣士
- A 恒常(1発): DEATH'S CONTRACT, GIANT SLAYER, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, BERSERKER, HIGH VOLTAGE, REVERSE GEAR, ULTIMATE, GROUND FIGHTER, THUNDER STRIKE
- A DPS: DEATH'S CONTRACT, ATTACK SPEED UP, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, BERSERKER, HIGH VOLTAGE, REVERSE GEAR, ULTIMATE, GROUND FIGHTER, THUNDER STRIKE
- B ボス実戦: OVERDRIVE, COMBO RUSH, MOMENTUM, DEATH'S CONTRACT, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, HUNTER, BERSERKER, BOSS KILLER, BLOOD BLADE  → 連撃の各段 [6480, 6480, 6480, 6480, 6660]、1周 0.38s
- C 単発最大(満HP): OVERDRIVE, MOMENTUM, DEATH'S CONTRACT, GIANT SLAYER, COMBO MASTER, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, HUNTER, BERSERKER, COMBO EDGE

### お嬢様騎士
- A 恒常(1発): DEATH'S CONTRACT, GIANT SLAYER, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, BERSERKER, HIGH VOLTAGE, REVERSE GEAR, ULTIMATE, GROUND FIGHTER, THUNDER STRIKE
- A DPS: DEATH'S CONTRACT, ATTACK SPEED UP, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, BERSERKER, HIGH VOLTAGE, REVERSE GEAR, ULTIMATE, GROUND FIGHTER, THUNDER STRIKE
- B ボス実戦: OVERDRIVE, COMBO RUSH, MOMENTUM, DEATH'S CONTRACT, COMBO MASTER, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, HUNTER, BERSERKER, COMBO EDGE  → 連撃の各段 [6840]、1周 0.14s
- C 単発最大(満HP): OVERDRIVE, MOMENTUM, DEATH'S CONTRACT, GIANT SLAYER, COMBO MASTER, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, HUNTER, BERSERKER, COMBO EDGE

### ガンスリンガー
- A 恒常(1発): DEATH'S CONTRACT, GIANT SLAYER, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, BERSERKER, HIGH VOLTAGE, REVERSE GEAR, ULTIMATE, GROUND FIGHTER, THUNDER STRIKE
- A DPS: DEATH'S CONTRACT, ATTACK SPEED UP, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, BERSERKER, HIGH VOLTAGE, REVERSE GEAR, ULTIMATE, GROUND FIGHTER, THUNDER STRIKE
- B ボス実戦: OVERDRIVE, COMBO RUSH, MOMENTUM, DEATH'S CONTRACT, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, HUNTER, BERSERKER, BOSS KILLER, BLOOD BLADE  → 連撃の各段 [6490, 6490, 6670]、1周 0.30s
- C 単発最大(満HP): OVERDRIVE, MOMENTUM, DEATH'S CONTRACT, GIANT SLAYER, COMBO MASTER, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, HUNTER, BERSERKER, COMBO EDGE

### 竜騎士
- A 恒常(1発): DEATH'S CONTRACT, GIANT SLAYER, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, BERSERKER, HIGH VOLTAGE, REVERSE GEAR, ULTIMATE, GROUND FIGHTER, THUNDER STRIKE
- A DPS: DEATH'S CONTRACT, ATTACK SPEED UP, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, BERSERKER, HIGH VOLTAGE, REVERSE GEAR, ULTIMATE, GROUND FIGHTER, THUNDER STRIKE
- B ボス実戦: OVERDRIVE, COMBO RUSH, MOMENTUM, DEATH'S CONTRACT, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, COMBO MASTER, HUNTER, BERSERKER, BLOOD BLADE  → 連撃の各段 [6240.0, 8498.0, 11427.0]、1周 0.46s
- C 単発最大(満HP): OVERDRIVE, MOMENTUM, DEATH'S CONTRACT, GIANT SLAYER, COMBO MASTER, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, BERSERKER, COMBO EDGE, BLOOD BLADE

### 弓
- A 恒常(1発): DEATH'S CONTRACT, GIANT SLAYER, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, BERSERKER, HIGH VOLTAGE, REVERSE GEAR, ULTIMATE, GROUND FIGHTER, THUNDER STRIKE
- A DPS: DEATH'S CONTRACT, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, ATTACK SPEED UP, BERSERKER, HIGH VOLTAGE, REVERSE GEAR, ULTIMATE, GROUND FIGHTER, THUNDER STRIKE
- B ボス実戦: OVERDRIVE, MOMENTUM, DEATH'S CONTRACT, ATTACK SPEED UP, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, HUNTER, BERSERKER, BOSS KILLER, BLOOD BLADE  → 連撃の各段 [12980]、1周 0.58s
- C 単発最大(満HP): OVERDRIVE, MOMENTUM, DEATH'S CONTRACT, GIANT SLAYER, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, HUNTER, BERSERKER, BOSS KILLER, BLOOD BLADE

### 魔法
- A 恒常(1発): DEATH'S CONTRACT, GIANT SLAYER, SKYBOUND, GLASS CANNON, SKY MASTER, HEART BREAKER, AIR STRIKE, BERSERKER, AERIAL BLADE, AIR DOMINION, HIGH VOLTAGE, REVERSE GEAR, ULTIMATE
- A DPS: DEATH'S CONTRACT, ATTACK SPEED UP, SKYBOUND, GLASS CANNON, SKY MASTER, HEART BREAKER, AIR STRIKE, BERSERKER, AERIAL BLADE, AIR DOMINION, HIGH VOLTAGE, REVERSE GEAR, ULTIMATE
- B ボス実戦: OVERDRIVE, ATTACK SPEED UP, MOMENTUM, DEATH'S CONTRACT, SKYBOUND, GLASS CANNON, SKY MASTER, HEART BREAKER, AIR STRIKE, HUNTER, BERSERKER, AERIAL BLADE, BOSS KILLER  → 連撃の各段 [6490, 6490]、1周 0.09s
- C 単発最大(満HP): OVERDRIVE, MOMENTUM, DEATH'S CONTRACT, GIANT SLAYER, SKYBOUND, GLASS CANNON, SKY MASTER, HEART BREAKER, AIR STRIKE, HUNTER, BERSERKER, AERIAL BLADE, BOSS KILLER

### 格闘
- A 恒常(1発): DEATH'S CONTRACT, GIANT SLAYER, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, BERSERKER, HIGH VOLTAGE, REVERSE GEAR, ULTIMATE, GROUND FIGHTER, THUNDER STRIKE
- A DPS: DEATH'S CONTRACT, ATTACK SPEED UP, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, BERSERKER, HIGH VOLTAGE, REVERSE GEAR, ULTIMATE, GROUND FIGHTER, THUNDER STRIKE
- B ボス実戦: OVERDRIVE, COMBO RUSH, MOMENTUM, DEATH'S CONTRACT, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, HUNTER, BERSERKER, BOSS KILLER, BLOOD BLADE  → 連撃の各段 [5192, 5192, 7139, 14674]、1周 0.28s
- C 単発最大(満HP): OVERDRIVE, MOMENTUM, DEATH'S CONTRACT, GIANT SLAYER, COMBO MASTER, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, HUNTER, BERSERKER, COMBO EDGE

### 忍者
- A 恒常(1発): DEATH'S CONTRACT, GIANT SLAYER, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, BERSERKER, HIGH VOLTAGE, REVERSE GEAR, ULTIMATE, GROUND FIGHTER, THUNDER STRIKE
- A DPS: DEATH'S CONTRACT, GROUND ZERO, FROST EDGE, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, BERSERKER, HIGH VOLTAGE, REVERSE GEAR, ULTIMATE, GROUND FIGHTER, THUNDER STRIKE
- B ボス実戦: OVERDRIVE, MOMENTUM, FROST EDGE, DEATH'S CONTRACT, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, HUNTER, BERSERKER, BOSS KILLER, BLOOD BLADE  → 連撃の各段 [4606, 4606]、1周 0.24s
- C 単発最大(満HP): OVERDRIVE, MOMENTUM, DEATH'S CONTRACT, GIANT SLAYER, COMBO MASTER, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, HUNTER, BERSERKER, COMBO EDGE

### 巫女
- A 恒常(1発): DEATH'S CONTRACT, GIANT SLAYER, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, BERSERKER, HIGH VOLTAGE, REVERSE GEAR, ULTIMATE, GROUND FIGHTER, THUNDER STRIKE
- A DPS: DEATH'S CONTRACT, ATTACK SPEED UP, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, BERSERKER, HIGH VOLTAGE, REVERSE GEAR, ULTIMATE, GROUND FIGHTER, THUNDER STRIKE
- B ボス実戦: OVERDRIVE, ATTACK SPEED UP, MOMENTUM, DEATH'S CONTRACT, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, HUNTER, BERSERKER, BOSS KILLER, BLOOD BLADE  → 連撃の各段 [3894, 3894, 14278]、1周 0.21s
- C 単発最大(満HP): OVERDRIVE, MOMENTUM, DEATH'S CONTRACT, GIANT SLAYER, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, HUNTER, BERSERKER, BOSS KILLER, BLOOD BLADE

### 吸血鬼
- A 恒常(1発): DEATH'S CONTRACT, GIANT SLAYER, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, BERSERKER, HIGH VOLTAGE, REVERSE GEAR, ULTIMATE, GROUND FIGHTER, THUNDER STRIKE
- A DPS: DEATH'S CONTRACT, ATTACK SPEED UP, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, BERSERKER, HIGH VOLTAGE, REVERSE GEAR, ULTIMATE, GROUND FIGHTER, THUNDER STRIKE
- B ボス実戦: OVERDRIVE, COMBO RUSH, MOMENTUM, DEATH'S CONTRACT, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, HUNTER, BERSERKER, BOSS KILLER, BLOOD BLADE  → 連撃の各段 [7009, 7009, 12806]、1周 0.18s
- C 単発最大(満HP): OVERDRIVE, MOMENTUM, DEATH'S CONTRACT, GIANT SLAYER, COMBO MASTER, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, HUNTER, BERSERKER, COMBO EDGE

### 竜人
- A 恒常(1発): DEATH'S CONTRACT, GIANT SLAYER, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, BERSERKER, HIGH VOLTAGE, REVERSE GEAR, ULTIMATE, GROUND FIGHTER, THUNDER STRIKE
- A DPS: DEATH'S CONTRACT, ATTACK SPEED UP, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, BERSERKER, HIGH VOLTAGE, REVERSE GEAR, ULTIMATE, GROUND FIGHTER, THUNDER STRIKE
- B ボス実戦: OVERDRIVE, COMBO RUSH, MOMENTUM, DEATH'S CONTRACT, GROUND ZERO, GLASS CANNON, HEAVY IMPACT, HEART BREAKER, GROUND BREAKER, HUNTER, BERSERKER, BOSS KILLER, BLOOD BLADE  → 連撃の各段 [8450, 8450, 15364]、1周 0.31s
- C 単発最大(瀕死): OVERDRIVE, MOMENTUM, DEATH'S CONTRACT, GIANT SLAYER, COMBO MASTER, GROUND ZERO, BERSERK DRIVE, HEAVY IMPACT, GROUND BREAKER, HUNTER, LAST STAND, ULTIMATE, GLASS CANNON


## 合成カードで同じ能力を重ねた場合(Lv9の上限はカードIDの文字列ごと → 13枠×9回 = 117回)

ゲーム内で確認: ATTACK UP×9の合成カード3種(レア度だけ違う)をキャラカードに + デッキの素のATTACK UPを9回 → 36回分(攻撃20→380)。
- 黒剣士: DEATH'S CONTRACT(+60)を117回 → 攻撃 7,040(1発 7,040、最大HPは下限1)
- 格闘: DEATH'S CONTRACT(+60)を117回 → 攻撃 7,040(1発 21,120、最大HPは下限1)
- 弓: DEATH'S CONTRACT(+60)を117回 → 攻撃 7,040(1発 22,528、最大HPは下限1)

## MOMENTUM / OVERDRIVE

式(PlayerController.cs:602): 攻撃 += round(MomentumBonus × (速度倍率 − 1))。速度倍率 = 自然加速(距離だけ: 100m+5%複利、上限 100km/h÷18km/h = ×5.556)。
**SPEED UP等のカードで実際の速さを上げても変わらない**(カードは runSpeed、MOMENTUM は距離の倍率)。3.6kmで上限に達し、以降ずっと ×4.556。

| 距離(自然の速さ) | 速度倍率 | MOMENTUM Lv1/5/9 | OVERDRIVE Lv1/5/9 | 両方Lv9 | ATTACK UP Lv9との比 |
|---|---|---|---|---|---|
| 0.1km(18km/h) | ×1.00 | +0 / +0 / +0 | +0 / +0 / +0 | +0 | ×0.0 |
| 1km(28km/h) | ×1.55 | +17 / +83 / +149 | +22 / +110 / +198 | +347 | ×3.9 |
| 2km(46km/h) | ×2.53 | +46 / +229 / +412 | +61 / +305 / +550 | +962 | ×10.7 |
| 2.5km(58km/h) | ×3.23 | +67 / +334 / +601 | +89 / +445 / +801 | +1402 | ×15.6 |
| 3km(75km/h) | ×4.12 | +93 / +467 / +841 | +125 / +623 / +1122 | +1963 | ×21.8 |
| 3.6km〜(100km/h) | ×5.56 | +137 / +683 / +1230 | +182 / +911 / +1640 | +2870 | ×31.9 |

- 125km/h / 150km/h / 150km/h超(カードで出した速さ)でも値は同じ(上の 3.6km〜 の行)。速度は効かず、**距離だけで最大**になる。
- 黒剣士(基礎20)が 3.6km 以降に MOMENTUM Lv9 + OVERDRIVE Lv9 だけを持つと 1発 20+2,870 = 2,890(×144)。
## 前半のボスを何発で倒せるか(黒剣士 基礎20、連撃1発=×1、地上、カードは取った枚数ぶん)

| カード | 1km オオカミ(HP247) | 5km ゴブリンライダー(HP460) | 10km 大蛇(HP550) | 20km サイクロプス(HP800) | 30km 大蜘蛛(HP1000) |
|---|---|---|---|---|---|
| カードなし | 20.0 → 13発 | 20.0 → 23発 | 20.0 → 28発 | 20.0 → 40発 | 20.0 → 50発 |
| ATTACK UP ×3 | 50.0 → 5発 | 50.0 → 10発 | 50.0 → 11発 | 50.0 → 16発 | 50.0 → 20発 |
| ATTACK UP ×2 + THUNDER STRIKE ×1 + BOSS KILLER ×1 | 90.0 → 3発 | 90.0 → 6発 | 90.0 → 7発 | 90.0 → 9発 | 90.0 → 12発 |
| DEATH'S CONTRACT ×1 | 80.0 → 4発 | 80.0 → 6発 | 80.0 → 7発 | 80.0 → 10発 | 80.0 → 13発 |
| DEATH'S CONTRACT ×2(最大HP 50→下限1) | 140.0 → 2発 | 140.0 → 4発 | 140.0 → 4発 | 140.0 → 6発 | 140.0 → 8発 |
| MOMENTUM ×1(3.6km以降) | 37.0 → 7発 | 157.0 → 3発 | 157.0 → 4発 | 157.0 → 6発 | 157.0 → 7発 |
| MOMENTUM ×1 + OVERDRIVE ×1(3.6km以降) | 59.0 → 5発 | 339.0 → 2発 | 339.0 → 2発 | 339.0 → 3発 | 339.0 → 3発 |
| OVERDRIVE ×2 + MOMENTUM ×1(3.6km以降) | 81.0 → 4発 | 521.0 → 1発 | 521.0 → 2発 | 521.0 → 2発 | 521.0 → 2発 |

- 1km/5kmはその距離での自然の速さ(1km ×1.55 / 5km以降 ×5.56)でMOMENTUMを計算。MOMENTUM/OVERDRIVEは解放(BEST 20km)後なら、どのランでも序盤から引ける。


## 実測DPS(ボス相手の実戦)

条件:
- 荒野40kmのゴーレム(HPを999,999に固定、段階/BREAK/移動はそのまま)を、ゲーム内30秒殴る。ボットは近いボスへ連続攻撃する。
- DPS = 与えた合計 ÷ 30秒(近づく時間/ボスの無敵/移動を含む)。「1回の命中」は同じフレームの複数ヒットを1回と数える(魔法弾+爆発など)。
- 代表 = ATTACK UP Lv2 + THUNDER STRIKE Lv1 + BOSS KILLER Lv1。最大 = B ボス実戦の13枚 Lv9(下の構成)。

| キャラ | カードなし DPS(1回平均) | 代表 DPS(1回平均) | 最大 DPS(1回平均 / 最大) | 最大÷なし |
|---|---|---|---|---|
| 黒剣士 | 48(21) | 278(98) | 10,127(5,962 / 6,939) | ×211 |
| 双剣士 | 36(11) | 247(87) | 12,436(5,657 / 8,748) | ×341 |
| お嬢様騎士 | 22(10) | 152(84) | 4,929(6,165 / 6,840) | ×224 |
| ガンスリンガー | 40(21) | 223(95) | 27,426(6,915 / 40,020) | ×677 |
| 竜騎士 | 80(54) | 284(139) | 24,757(7,216 / 15,427) | ×309 |
| 弓 | 53(25) | 236(117) | 25,575(7,104 / 23,644) | ×480 |
| 魔法 | 60(38) | 298(149) | 40,492(12,987 / 19,470) | ×673 |
| 格闘 | 21(27) | 99(124) | 18,457(8,267 / 19,810) | ×867 |
| 忍者 | 60(23) | 233(107) | 20,578(6,864 / 10,825) | ×342 |
| 巫女 | 61(23) | 265(110) | 38,116(10,638 / 35,047) | ×621 |
| 吸血鬼 | 83(27) | 331(127) | 33,305(8,928 / 18,224) | ×399 |
| 竜人 | 58(50) | 215(174) | 29,328(10,604 / 20,742) | ×501 |

- 魔法と巫女の最大構成は、HP999,999 のゴーレムを約25秒で倒した。
- 理論DPS(report の B DPS)より実測が低いのは、近づく時間/ボスの無敵/ボットが最適でない分。


## カードなしで各ボスを倒せるか(全12キャラ × 33関門、各ランを新しく、ゲーム内300秒まで、時間×4で実戦)

数字 = 倒すまでのゲーム内秒数。**未** = 300秒で倒せず(残りHP% / その速さで倒し切るまでの予想秒)。**0ダメ** = 300秒で1発も入らない。
ボット: 近いボスへ前(後ろ/上)攻撃を続けるだけ(反射/溜め/特殊技は狙わない)。プレイヤーは倒れないようにHPを戻している。

### 荒野

| 関門 | ボス | HP | 黒剣士 | 双剣士 | お嬢様 | 銃 | 竜騎士 | 弓 | 魔法 | 格闘 | 忍者 | 巫女 | 吸血鬼 | 竜人 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1km | Wolf | 247 | 18 | 16 | 15 | 17 | 12 | 24 | 10 | 15 | 12 | 11 | 12 | 20 |
| 5km | GoblinRider | 460 | 26 | 32 | 76 | 26 | 25 | 24 | 16 | 57 | 18 | 19 | 20 | 29 |
| 10km | Serpent | 550 | 27 | 32 | 64 | 39 | 26 | 29 | 64 | 81 | 29 | 22 | 29 | 28 |
| 20km | Cyclops | 800 | 30 | 60 | 87 | 30 | 30 | 35 | 18 | 111 | 25 | 24 | 25 | 34 |
| 30km | Spider | 1000 | 47 | 76 | 115 | 38 | 49 | 39 | 63 | 68 | 39 | 39 | 57 | 58 |
| 40km | Golem | 1300 | 51 | 85 | 136 | 47 | 64 | 42 | 38 | 182 | 36 | 38 | 35 | 55 |
| 50km | Griffin | 1500 | 56 | 101 | 168 | 72 | 69 | 59 | 79 | 119 | 61 | 52 | 43 | 71 |
| 60km | Hydra | 1800 | 64 | 123 | 174 | 56 | 58 | 58 | 45 | 202 | 50 | 41 | 50 | 70 |
| 70km | Demon | 2000 | 56 | 116 | 171 | 72 | 48 | 58 | 61 | 213 | 59 | 60 | 59 | 67 |
| 80km | Dragon | 2500 | 131 | 258 | **未** 31% / +136s | 76 | 108 | 57 | 68 | 185 | 72 | 132 | 83 | 78 |
| 90km | BlackKnight | 2800 | 103 | 163 | **未** 0% / +0s | 140 | 100 | 136 | 234 | 255 | 111 | 93 | 106 | 114 |

### 洞窟

| 関門 | ボス | HP | 黒剣士 | 双剣士 | お嬢様 | 銃 | 竜騎士 | 弓 | 魔法 | 格闘 | 忍者 | 巫女 | 吸血鬼 | 竜人 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1km | Centipede | 268 | 15 | 18 | 16 | 13 | 12 | 13 | 20 | 13 | 13 | 12 | 14 | 14 |
| 5km | Scorpion | 483 | 20 | 30 | 78 | 18 | 18 | 19 | 25 | 52 | 18 | 17 | 21 | 40 |
| 10km | Mole | 550 | 41 | 67 | 210 | 47 | 66 | 38 | 32 | 277 | 37 | 60 | 44 | 58 |
| 20km | Troll | 850 | 28 | 50 | 85 | 30 | 30 | 26 | 19 | 70 | 24 | 24 | 23 | 50 |
| 30km | Worm | 1100 | 146 | **未** 34% / +152s | **未** 73% / +800s | 147 | 277 | 166 | 101 | **未** 80% / +1228s | 107 | 174 | 191 | **未** 73% / +811s |
| 40km | CrystalGolem | 1400 | 50 | 86 | 150 | 43 | 69 | 39 | 24 | 134 | 40 | 32 | 34 | 65 |
| 50km | Bat | 1600 | 76 | 146 | **未** 42% / +216s | 89 | 97 | 65 | 44 | **未** 54% / +349s | 68 | 65 | 67 | 171 |
| 60km | ScorpionKing | 1900 | 47 | 89 | 190 | 60 | 70 | 60 | 39 | 184 | 48 | 48 | 56 | 92 |
| 70km | Basilisk | 2100 | 81 | 118 | **未** 19% / +71s | 75 | 89 | 72 | 42 | **未** 6% / +20s | 72 | 60 | 99 | 129 |
| 80km | Drake | 2600 | 80 | 183 | **未** 13% / +45s | 101 | 105 | 83 | 77 | 275 | 101 | 74 | 100 | 141 |
| 90km | AncientDemon | 2900 | 81 | 171 | **未** 0% / +0s | 93 | 136 | 86 | 48 | 251 | 73 | 82 | 75 | 139 |

### 天空

| 関門 | ボス | HP | 黒剣士 | 双剣士 | お嬢様 | 銃 | 竜騎士 | 弓 | 魔法 | 格闘 | 忍者 | 巫女 | 吸血鬼 | 竜人 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1km | Dragon(天空の竜) | 206 | 10 | 27 | 180 | 13 | 62 | 9 | 10 | 77 | 10 | 14 | 19 | 10 |
| 5km | Majin(天空の魔人) | 1380 | **未** 7% / +23s | **未** 9% / +29s | **未** 77% / +994s | 51 | **未** 70% / +686s | 93 | 22 | **未** 87% / +2000s | 31 | 236 | 64 | **未** 67% / +600s |
| 10km | Behemoth | 700 | 26 | 51 | 86 | 32 | 24 | 22 | 21 | 58 | 27 | 26 | 27 | 33 |
| 20km | Titan | 1100 | **未** 54% / +360s | **未** 54% / +347s | **0ダメ** | 64 | **未** 20% / +73s | 60 | 26 | **0ダメ** | **0ダメ** | 88 | **未** 97% / +10013s | **0ダメ** |
| 30km | Jellyfish | 1300 | 71 | 125 | **未** 50% / +300s | 51 | 128 | 44 | 30 | **未** 15% / +51s | 41 | 40 | 42 | 94 |
| 40km | Leviathan | 1600 | **未** 11% / +38s | **未** 52% / +323s | **未** 68% / +641s | 150 | **未** 46% / +253s | 154 | 272 | **未** 79% / +1146s | 243 | 235 | 280 | **未** 8% / +26s |
| 50km | Fenrir | 1800 | 54 | 83 | 169 | 52 | 50 | 54 | 98 | 75 | 61 | 47 | 59 | 48 |
| 60km | SkyGolem | 2100 | 100 | 140 | **未** 44% / +238s | 64 | 122 | 77 | 36 | **未** 56% / +383s | 58 | 51 | 54 | 186 |
| 70km | Phoenix | 3080 | 216 | **未** 14% / +48s | **未** 85% / +1700s | 140 | 259 | 88 | 79 | **未** 48% / +283s | 129 | 116 | 175 | **未** 8% / +27s |
| 80km | SkySerpent | 2500 | 204 | **未** 18% / +68s | **未** 78% / +1039s | 166 | **未** 8% / +25s | 129 | 79 | **未** 69% / +679s | 114 | 157 | 140 | **未** 20% / +74s |
| 90km | Guardian | 3200 | 79 | 187 | **未** 26% / +103s | 95 | 99 | 84 | 124 | 276 | 100 | 90 | 100 | 134 |

合計 396 戦: 倒した 350 / 300秒で倒せず(削れている) 42 / 1発も入らない 4 / ボスが出なかった(試験の問題) 0


## カードなしでのボス撃破(コード調査)

コード上、**永久に倒せないボスは無い**。

- **1発の最低ダメージ**: 1(`EffectiveAttackPower` ≥ 1、技の倍率の後も ≥ 1)。
- **ボス側の防御/軽減**: 無い。
- **時間で消えるボス**: いない。
- **回復**:
  - フェニックスの1回だけの復活(40%)。
  - 三姉妹のソロは HP0 で退く(倒した扱い)。
- 例外: 100,000mの追跡の死神は、仕様で倒せない(捕まるとラン終了)。

**ラン再開(時間切れ)は荒野だけ**(`BossBattleTuning.resumeStages`)。洞窟/天空/ラスダンは、ボスが生きている間ずっとランが止まる。
→ 実戦で倒すのに何十分もかかる組み合わせは、そのままプレイが止まる。

**実戦で問題が出た組み合わせ(原因の分類)**

**天空20km タイタン**
- 近接キャラが届かない。お嬢様騎士/格闘/忍者/竜人は0ダメージ、吸血鬼は1発だけ。黒剣士/双剣士/竜騎士は削れるがとても遅い。
- 原因の分類: **ボスの位置**(`minGap = 5`、SkyBosses.cs:337)と、**キャラの間合い**(攻撃範囲のカードなし)。
- 飛び道具のキャラ(銃/弓/魔法/巫女)は60〜90秒で倒せる。

**天空5km 魔人**
- 12〜24m前を漂う。近接キャラは反射/たまの接近でしか当たらない。
- 予想: 格闘は残り33分、お嬢様は17分、竜騎士は11分、竜人は10分。
- 原因の分類: **ボスの位置**と**飛び道具を持たないキャラ**。
- 再戦(5km関門)では最大 約10,300HP になる(ゲーム内最大の単体)。

**天空40km レヴィアタン / 洞窟30km ワーム**
- 当てられる時間が短い。潜っている間は無敵。近接キャラは数分〜20分。
- 原因の分類: **無敵の時間(潜る)** と**近接の間合い**。
- ワームの CrossSweep は自分から離れてしまう(コード上の不具合の可能性)。

**お嬢様騎士(全般)**
- 1発10、攻撃時間×1.35でDPSが最も低い(22)。80km以降の大型ボスは5分を超える。
- 原因の分類: **キャラ固有の性能**。ボスの数値は通常どおり。

**天空70km フェニックス**: 1回復活(実効3,080HP)。浮いていて近接は遅い。

**ボスが出なかった3件**
- 試験のボットが関門の手前の障害物で止まっていた(お嬢様騎士はジャンプ1回)。
- ボットを直して再試験し、3件とも倒せた。ボスの問題ではない。

**開発版だけの注意**
- PlayerPrefs `Dev.BossHpPlanHits`(ボス試験タブの15/20/25発)が残っていると、ボスHPが50〜75倍になる。
- 「ボスが硬すぎる」と感じた端末では、この値を確認すること。

## 既知の問題の再確認(最新コード)

| 問題 | 今のコード | 状態 |
|---|---|---|
| 出現率カードがEncounterDirectorステージで無効 | 全4ステージが EncounterDirector(`replacesMilestoneWalls=1`)。出現率は EnemyWallManager の壁だけで使われ、その壁も止まっている | **無効のまま**(MORE ENEMIES/HORDE は効果なし。GREED/NO TURNING BACK/MONSTER RUSH/FAST ENEMIES/HELL MODE/PANDEMONIUM の出現率部分も) |
| THUNDER STRIKE等の属性 | `CardDefinition.element` は表示だけ。どこからも使われない | **未実装のまま**(数値部分は効く) |
| FROST EDGE の Slow/Freeze | 実装なし | **未実装のまま** |
| WIND CUTTER の属性 | 実装なし | **未実装のまま** |
| 初撃/締めが一部キャラで機能しない | 弓/魔法/忍者/巫女は連撃の段を設定しないので、一切乗らない。お嬢様騎士は毎回両方乗る。竜騎士は締めが2段目と3段目に乗り、石突き/下突き/急降下は毎回初撃。連撃以外の技に前の段が残る | **残っている(詳しくなった)** |
| 飛び道具に攻撃範囲が乗らない | 弾/矢/魔法/札/手裏剣/式神/蝙蝠/ブレスすべて | **残っている**。標準の着地衝撃/竜騎士の急降下/忍者の斬り抜け等の近接技にも効かないものがある |
| MOMENTUM/OVERDRIVE が過大 | 攻撃 += 値×(距離の速度倍率−1)、3.6km以降 ×4.556 | **変わらず**。カードの実際の速さとは無関係 |
| 最大HPのCapで表示と実効値が違う | 上限 `maxLivesCap = 100`(10倍スケールでハート10)。基礎50のキャラはHEART UP Lv5で上限。負の値は下限1 | **残っている**(上限は100へ) |
| 回復量だけでは発動しない | `TryLifesteal` は確率>0 かつ 量>0 の時だけ | **残っている**(SECOND WIND 単体は無効) |
| 吸収確率がLv9前に100% | PHOENIX Lv4 / PREDATOR Lv5 / VAMPIRE Lv7 | **残っている** |
| EXP CONVERTER が0以下 | 1枚Lv9なら ×0.1(0にはならない)。合成で同じ能力を重ねると0以下になり、経験値が減る(下限なし) | **合成の時だけ起きる** |
| ジャンプ回数に上限がない | 上限なし(Lv9で11回) | **残っている** |
| 魔法でジャンプ系が無効 | 魔法はジャンプ力/回数とも無効。忍者/吸血鬼もジャンプ力が無効(上昇の速さが固定) | **残っている(範囲が広がった)** |
| GROUND BREAKER と HEAVY IMPACT が同じ | どちらも「地上+40」(R3とR4) | **残っている** |
| AIR STRIKE と SKY MASTER が近い | 空中+40 / 空中+40・ジャンプ+10% | **残っている**(SKY MASTERが上位互換) |

**新たに見つかったこと**
- 合成カードでLv9上限を超えられる(上限がカードIDの文字列ごと)。
- 最大HPを下げるRiskカードの欠点が下限1で止まる。
- 速度/ジャンプ力の**下げる**側も複利。
  - HEAVY ARMOR Lv9 でジャンプ力×0.23(高さ×0.05 = ほぼ跳べない)。
  - FORTRESS / REVERSE GEAR Lv9 で速度×0.23(自然上限でも23km/h)。
- DOUBLE ATTACK は Lv5、RAPID EDGE は Lv7 で攻撃時間の下限0.25に達し、以降は効果なし。
- 魔法の前の魔法弾は2ヒット(平らなボーナスが2倍効く)。
- ガンスリンガーの弾は荒野/洞窟/天空ボスを貫通する。
- CARD TEST の `BossHitComboAverage` は、弓/魔法/忍者/巫女の初撃/締めを乗る前提で計算している(過大)。

## 修正優先候補(順位ではなく列挙)

- **MOMENTUM / OVERDRIVE**: 距離だけで ×4.556。Lv1の2枚で前半ボスが2発。
- **Lv9上限の数え方**: カードIDごと → 合成で同じ能力を117回まで重ねられる。
- **速度の複利**: SPEED UP / GREED / NO TURNING BACK / CLOSE CALL / ULTIMATE。150km/hを Lv2〜5 で超える。GREED/NTB は欠点が効いていない。
- **欠点が下限で止まるRisk**: DEATH'S CONTRACT / GLASS CANNON / HEART BREAKER / BERSERKER。
- **攻撃の足し算の量**: +10〜+60/Lv で、基礎20に対して1枚Lv9で×5.5〜×28。13枚で数百倍。全攻撃カード。
- **攻撃時間の下限0.25**: DOUBLE ATTACK / RAPID EDGE が途中で頭打ち。テンポ×4。
- **ジャンプ力の複利**: JUMP POWER UP 高さ×12 / HEAVY ARMOR ほぼ跳べない。
- **攻撃範囲の上限なし**: LONG BLADE Lv9 ×5.5(近接だけ)。
- **Shield**: PERFECT GUARD Lv9 で18回。
- **キャラ間の不揃い**: 初撃/締め、範囲、ジャンプが効かないキャラがある。魔法の2ヒット。
- **(ボス側だが「カードなしで倒せる」に関わる)**: タイタン(近接が届かない)、天空の魔人、レヴィアタン/ワーム。天空/洞窟はラン再開が無い。


## キャラ別の攻撃(コード調査)

P = 命中時の攻撃値(EffectiveAttackPower / ボスは EffectiveBossAttackPower)。ダメージ = round(P × damageScale)、最低1(PlayerAttackInfo.cs:92-100)。

## 共通
- ASM(攻撃時間倍率) = max(0.25, ASM×(1-f))。乗算・下限0.25・上限なし(GIANT SLAYER の -0.15 で遅くなる)。基礎値は def.attackSpeedMultiplier。
- ARM(攻撃範囲倍率) = max(0.1, ARM+δ)。加算。ARMを掛けている技だけに効く。**飛び道具(弾/矢/魔法/札/手裏剣/式神/蝙蝠/ブレス)にはすべて効かない**。
- 標準の剣(黒剣士/双剣士/お嬢様騎士/ガンスリンガー): 1振り = attackActiveTime 0.4×ASM、cooldown 0.3は振りの中に収まり実質効かない → 1発/0.4×ASM 秒。
- 速度倍率(GetSpeedMultiplier)は距離だけ: 100mごとに+5%複利、上限 100km/h÷18km/h = 5.56。MOMENTUM は最大 ×4.56。
- KitWindupScale = clamp(7.5/走行速度, 0.2, 1): 速いほど一部の溜めが短い(SPEED UP でも短くなる)。
- 雑魚/障害物は EffectiveAttackPower、ボス(荒野/洞窟/天空/ドラゴン/魔人)は全部 EffectiveBossAttackPower → BOSS KILLER/HUNTER は全ボスに効く。
- 飛び道具/爆発/ゾーンは damageScale だけを作った時に固定し、**P は当たった時に読む**(空中/地上、連撃の段、HP、速度、竜騎士の倍率も当たった時の状態)。
- ボスには SwingId の重複防止がない: 飛び道具1つ/爆発1つ/ゾーンの1回ごとに1ヒット。

## キャラの基礎値(Resources/Characters/*.asset)
| id | 攻撃 | HP/最大 | 連撃(maxComboChain) | ASM | ARM | ジャンプ | 走る速さ× | 種類 |
|---|---|---|---|---|---|---|---|---|
| swordsman 黒剣士 | 20 | 30/50 | 3 | 1 | 1 | 2 | 1 | 標準 |
| dual_blade 双剣士 | 10 | 50/50 | 5 | 0.75 | 0.85 | 2 | 1.15 | 標準 |
| noble_lady お嬢様騎士 | 10 | 30/30 | **1** | 1.35 | 0.75 | 1 | 0.95 | 標準(上/空中/下なし) |
| gunslinger | 20 | 30/50 | 3 | 1 | 1 | 2 | 1 | 銃 |
| dragon_lancer 竜騎士 | **40** | 40/50 | **2**(3段) | 1.35 | 1 | 2 | 1 | 槍 |
| archer 弓 | 20 | 30/50 | 1 | 1 | 1 | 2 | 0.93 | 弓 |
| mage 魔法 | 20 | 20/40 | 1 | 1 | 1 | 2 | 1 | 魔法 |
| fighter 格闘 | 20 | 40/50 | 4 | 1 | 1 | 2 | 1 | 格闘 |
| ninja 忍者 | 20 | 30/40 | 1 | 1 | 1 | 2 | 1.08 | 忍者 |
| miko 巫女 | 20 | 30/50 | 1 | 1 | 1 | 2 | 1 | 巫女 |
| vampire 吸血鬼 | 20 | 30/50 | 3 | 1 | 1 | 2 | 1 | 吸血鬼 |
| dragonkin 竜人 | 30 | 50/60 | 3 | 1 | 1 | 2 | 0.95 | 竜人 |

## 技(単体の相手・全部命中・基礎ASM・低速。P/秒)
- 黒剣士 前連撃 ×1 ×3段、0.4s/発 → 2.5P/s(約50/s)。双剣士 0.3s → 3.33P/s(約33/s)。お嬢様騎士 0.54s → 1.85P/s(約18.5/s)。
  上攻撃0.28s固定(ASM無効)、急降下×1、着地の衝撃×1(範囲:見た目だけ)。急降下中の上で上攻撃へ(ジャンプを使わず空中で繰り返せる)。
- ガンスリンガー 前/後ろ ×1、0.4×ASM → 2.5P/s。上撃ち(ジャンプごと)、下撃ち(空中3発)はASM無効。範囲は全部無効。
- 竜騎士 3段突き ×1.0/1.25/1.7(lanceDamageScaleでPそのものに掛かる)、1周1.846s → 2.14P/s(約86/s)。石突き×0.5。急降下の着地 ×1.5。
  速度係数は吹き飛ばし/踏み込みだけ(ダメージには効かない)。
- 弓 溜め ×1/2/3.2(0.55s/1.2s、ASM無効)。中溜めが最効率 2.99P/s。上の矢×1.8(ジャンプごと)、空中下×2.0(2本)。範囲は全部無効。
- 魔法 前の魔法弾 ×1 + 着弾の爆発 ×1 = **1回で2ヒット**、0.36×ASM → **5.56P/s(約111/s)**。下 ×1.2+×1.3 → 6.94P/s。範囲は全部無効。
  **ジャンプ力/回数は効かない**(飛行の移動へ先に分岐)。高度1以上は常に「空中」扱い = 空中攻撃力が常に乗る。
- 格闘 4段 ×0.8/0.8/1.1/2.2、1周1.126s → 4.35P/s(約87/s)。反撃(爆発)×3、アッパー×1.4、跳び蹴り×1.6。
- 忍者 斬り抜け×1.1(範囲無効)、手裏剣 2×0.7(最短0.24s固定) → 4.67P/s。上昇の速さは固定(ジャンプ力無効)。
- 巫女 札×0.6 → 封印 1.3s後に×1.6、封印に2枚目で×2.2。交互で4.05P/s。**封印は雑魚と荒野/洞窟/天空ボスだけ**(ドラゴン/魔人は×0.6のみ 約1.43P/s)。
  結界: 0.45sごと×0.35を6回。
- 吸血鬼 連撃 ×0.9/0.9/1.6、1周0.82s → 4.15P/s(ゲージ50以上5.65、ラッシュ中7.70)。BloodPower ×1.2/×1.3 はdamageScaleへ。上昇固定(ジャンプ力無効)。
- 竜人 爪 ×1.3/1.3/2.3、1周1.224s → 4.0P/s(約120/s)。ブレス 5×0.6(貫通無制限)、焼けた地面 0.3sごと×0.35。

## 見つかった問題(重要な順)
1. **弓/魔法/忍者/巫女は初撃(FIRST STRIKE/SONIC BLADE)と締め(COMBO PLUS/EDGE/MASTER/RUSH/CHAIN EXPLOSION)が一切乗らない**。
   maxComboChain=1 だが comboCount を設定する処理が無く0のまま。BossHitComboAverage(PC:636)は乗る前提で計算している(CARD TESTの見積りが過大)。
2. **竜騎士: 連撃の段数3なのに maxComboChain=2** → 締めのボーナスが2段目と3段目に乗る。石突き/下突き/急降下は comboCount=1 → 初撃が毎回乗る。
   段の倍率は平らなボーナスに掛かるが BossDamageBonus には掛からない(他のキャラは damageScale×飛び道具の数ぶん掛かる → 不揃い)。
3. **お嬢様騎士は毎振り「初撃」と「締め」の両方が乗る**(連撃1)。
4. comboCount が連撃以外の技で古いまま残る(上/急降下/着地/アッパー/反撃等が、直前の連撃の締め・初撃のボーナスを受ける)。被弾でだけ0に戻る。
5. 飛び道具/爆発/ゾーンは当たった時の状態でPを読む(封印の爆発/結界の何秒も後の1回も、その時の空中/地上・HP・段で計算)。
6. 効かない能力: 攻撃範囲(飛び道具全部、標準の着地衝撃、竜騎士の急降下落下、忍者の斬り抜け/スライド/下、吸血鬼の霧/急降下、格闘の跳び蹴り、竜人のブレス/炎)。
   ジャンプ力(魔法/忍者/吸血鬼)、ジャンプ回数(魔法)。攻撃速度(上/下の技、弓の溜め、忍者/手裏剣/ブレスの最短時間、ガンスリンガーの空中撃ち)。
7. 魔法の前の魔法弾は2ヒット → 平らなボーナスが1回で2倍効く。
8. 魔法 + SKYBOUND(空中+50) は高度1以上で常時。欠点なし。
9. MOMENTUM は距離だけで最大×4.56。竜騎士の速度係数はダメージに効かない。
10. ガンスリンガーの弾は荒野/洞窟/天空ボスを貫通する(PlayerBullet.cs:49 が子のヒットボックスを見ていない)。ダメージは1回。
11. (未確認)KitProjectile がボスの攻撃判定(子)もボス扱いで当たる可能性(KitAttacks.cs:80)。
12. 下限: P≥1, ダメージ≥1, ASM≥0.25, ARM≥0.1。maxJumps/jumpForce/runSpeed には下限なし(現在のカードで負にはならない)。


## CARD BALANCE TEST「ビルド」タブ(今回追加、開発版のみ)

開き方: DEBUG ON → ラン中の左のデバッグ列「CARD TEST」→ タブ「ビルド」。

**掛ける**
- Lv1 / Lv5 / Lv9 を選び、次のどれかを押す。
  - 「カードなし」
  - 「代表的な数枚」
  - 「攻撃 恒常最大」
  - 「ボス実戦最大」
  - 「速度特化」
- 「1枚だけ」(◀▶でカードを選んで「このカードをLvN」)もある。
- 押すと RESET してから、ゲームのカード処理(`ApplyCardEffectsStacked`)そのままで掛ける。

**表示**: 今の能力値。
- 攻撃(地上/空中/初撃/締め/満HP/瀕死/加速/ボス)
- 1発(ボス・地上・満HP・今の速度、+初撃か締め、BREAK)
- 速さ km/h、攻撃時間/範囲、ジャンプ力(高さ)/回数、HP/上限、Shield、ボスHP倍率

**保存されないこと**
- ラン中の値だけを変える。所持/デッキ/保存は変えない。
- 新しいラン(シーンの読み直し)で消える。
- キャラの切り替えは「カードLv / キャラ」タブ。

## 自動テスト(開発版 Windows)

**`QaSweep -qaCardSurvey <dir>`**
- 91枚 × Lv1/5/9 をゲームの処理で掛けて、能力値を表(card_survey.tsv)に出す。同じフレームで元へ戻す。
- キャラの基礎値(characters.tsv)、合成の重ね(36回分を確認)、速度のkm/hも出す。
- 結果: 全件 / 元へ戻った / 例外0。

**`QaSweep -qaBossKill <dir>`**
- 各キャラ × 各ステージ × 各関門。毎回新しいランで、カードなし・経験値なし・報酬カードを選ばない。
- 実戦はゲーム内300秒まで、時間×4。
- 引数: `-qaBkChars` `-qaBkStages` `-qaBkGates` `-qaBkTimeout` `-qaBkTs` `-qaBkCards id:lv,...` `-qaBkHp`(ボスHP固定で実測DPS)。
- 結果: 396戦(3プロセス並列)、例外0。倒した350 / 300秒で倒せず42(削れている) / 0ダメ4 / ボスが出ない0(ボット修正後)。

**実測DPS**: `-qaBossKill -qaBkGates 40000 -qaBkHp 999999 -qaBkTimeout 30 -qaBkCards ...` を36戦。例外0。

テスト機のセーブ: 各テストが開始時に控えて終了時に戻している。さらにテスト前のレジストリを書き出して比べ、ゲームの保存値が変わっていないことを確認した(違いは Unity の画面サイズ/セッションの値だけ)。

**Android 実機では未確認。**

## 計算の元(scratchpad)

- **カード**
  - `cards/parse_cards.py`(アセット→cards.json)
  - `analyze.py`(全カード表/速度)
- **攻撃 / ボス**
  - `analyze_attack.py`(A/B/C/DPS/前半ボス)
  - `analyze_boss.py`(撃破表)
- **調査メモ**
  - `kit_audit.md`(キャラの技の調査)
