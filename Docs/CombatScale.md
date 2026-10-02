# 戦闘数値10倍スケール(2026-10-02)

目的: 攻撃力 +1 が旧 +50%(攻撃力2) → 新 +5%(攻撃力20)になり、カードを細かく調整できるようにする。
**攻撃とHPを同じ比率で10倍にしたので、何発で倒せるか(TTK)は旧と同じ**(技の倍率の端数の丸めが細かくなった分だけ、ごくわずかに変わる)。
定数は `Assets/Scripts/CombatScale.cs`(K=10、ハート1つ=10HP、通常の被弾10、強い一撃20)。

## 10倍にしたもの

| 区分 | 項目 | 旧 → 新 | 場所 |
|---|---|---|---|
| キャラ | 基礎攻撃力 attackPower | 1〜4 → 10〜40 | Resources/Characters/*.asset, CharacterDatabaseBuilder, CharacterDefinition既定 |
| キャラ | 開始HP baseLives / 最大HP baseMaxLives | 2〜6 → 20〜60 | 同上 |
| Player | AttackPower の既定 | 2 → 20 | PlayerController |
| GameManager | startingLives / maxLives / maxLivesCap | 3/5/10 → 30/50/100 | Main.unity, GameManager既定 |
| カード効果 | AttackPower, AirAttackPower, GroundAttackPower, FirstHitBonus, ComboFinalStageBonus(締め), LowHpAttackBonus, FullHpAttackBonus, MomentumBonus, BossDamageBonus | 例 ATTACK UP +1 → +10 | Resources/Cards/*.asset(55枚), CardDatabaseBuilder |
| カード効果 | MaxHp(HEART UP等) | +1 → +10 | 同上 |
| カード効果 | LifestealAmount(ドレインの回復量) | 1 → 10 | 同上 |
| 雑魚 | 基本HP(DistanceTierManager.CurrentEnemyHp) | 5(0m)〜55(100km) → 50〜550 | DistanceTierManager(式に×10) |
| ボス | 全ボスの基本HP | 例 狼24→240、黒騎士280→2800、守護者320→3200 | BossManager(Wild/Cave/Sky表、荒野ドラゴン250→2500、dragonMaxHp 20→200、機械竜30→300) |
| ボス | 死神三姉妹(最終戦) | 280/200 → 2800/2000 | ReaperFinaleBattle |
| ボス | 跳ね返した火球のダメージ | 2/6(巨大)、ドラゴン2、魔人2 → 20/60、20、20 | WildBossBase, DragonController, MajinController |
| 障害物 | 耐久 durability | 1/2/4/7/10 → 10/20/40/70/100(旧ルール 2/1 → 20/10) | Resources/Obstacles/ObstacleBalance.asset, ObstacleController |
| 被弾 | 通常の被弾(敵/障害物/地形/落下/ボスの通常攻撃) | 1 → 10 | PlayerController.TakeDamage / TryDamagePlayer の既定、BossBattleTuning.normalDamage |
| 被弾 | 強い一撃(必殺技/大技/巨大火球) | 2 → 20 | BossBattleTuning.ultimateDamage、WildBosses、DragonController |
| 被弾 | 満タンから強い一撃では倒れない | ハート1つ残す | 同じ意味のまま(通常の一撃ぶん=10残す) |
| 被弾 | 死神の一撃(ソロ) | HPを1にしてから1 → 10にしてから10 | GameManager.ReapPlayer |
| 回復 | 吸血鬼の固有回復 | 1 → 10 | PlayerController.Vampire |
| マルチ | JOINの被弾(HOST権威) | 1 → 10 | NetMatch.ApplyHostDamage |
| マルチ | CO-OP復活(渡す側は2以上必要・1渡す) | 11以上必要・10渡す | NetMatch.Phase3 |
| マルチ | 受信ダメージの上限(異常値の防止) | 9999 / 999 → 999999 | NetCombat / NetObstacles |
| 保存 | 中断中のラン(CONTINUE)のHP | 移行で×10 | SaveSystem 形式1→2 |
| 開発 | CARD TEST の攻撃力/最大HPの候補値 | ×10(保存済みの候補も移行) | CardBalanceTest, SaveSystem |

## 10倍にしなかったもの(倍率・割合・回数・時間)
移動速度倍率 / ジャンプ力倍率 / 攻撃時間倍率(ATTACK SPEED)/ 攻撃範囲倍率 / EXP倍率 / MILE・ボスMILE倍率 /
ドレイン確率 / 出現率倍率 / 敵HP倍率・ボスHP倍率(カード) / ジャンプ回数 / Shield回数(1回=被弾1回を丸ごと防ぐ) /
技の倍率(damageScale、竜騎士の槍倍率、吸血鬼のBloodPower)/ BREAK倍率(×1.35)・疲労倍率 / 崩し(stagger)の値 /
ボスの段階の閾値(HPの割合)/ 速度(MOMENTUMの速度倍率)/ EXP・MILEの量 / 文字の壁・ONE MORE MILE? の看板(当てた回数で壊れる)。

## Lv9上限
- Run中のカードLv = キャラカード枠のLv + 取得したLv(通常カードは1回+1、合成カードは1回で合成Lvぶん)。上限 `GameManager.MaxRunCardLevel = 9`。
- Lv9のカードはレベルアップ/ボス報酬の候補に出ない。何かの経路で選ばれても効果は重ねない。デッキが全部Lv9なら選択は出ない(回復はする)。
- 候補の表示は「Lv.8 -> Lv.9 MAX」。
- 最終強化(Final Evolution)は今回は作っていない(Lv9の次の別の仕組みとして後で)。

## ボスHPの再設計案(開発版DEBUGのみ、既定は現行)
`Bosses/BossHpPlan.cs`。CARD BALANCE TEST の「ボス試験」タブで 現行 / 15発 / 20発 / 25発 を切り替える。
距離ごとの想定1発(下の表)× 発数 = その距離の10km節目ボスのHP。他のボスにも同じ倍率を掛ける(狼と節目ボスの差、ステージ差は残る)。
想定1発 = 黒剣士(標準の3段連撃)の最大級ビルド(13枠すべてLv9、満HP・地上・速度最大・ボス特効、連撃平均)= 6,940 を上限に、
距離ごとのカード成長(QAの全走行でのレベルアップ回数 ≒ 2回/km + 12、デッキ10枚×Lv9 = 90回で完成)と、
デッキの効率(10kmで6割 → 90kmで10割)を掛けた値。**カードの数値を見直したら作り直す**(計算は scratchpad の maxdmg.py)。
