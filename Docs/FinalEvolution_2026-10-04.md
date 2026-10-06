# FINAL EVOLUTION 第1段階(2026-10-04)

「Lv10」ではなく、Lv9 MAX の能力が**一時的に限界突破**する仕組み。カードの保存Lv / Mastery / AWAKENED は変えない。
ULTIMATE(キャラの必殺技)とは別の仕組みで、Gauge も共有しない。

## 流れ

```
能力Lv9(資格) → 追加で5,000m(READY) → 次の LEVEL UP の3択に最大1枠 → 選ぶ → 一時的な限界突破 → 終われば Lv9 MAX(USED)
```

| 状態 | 条件 | HUD(RUN BUILD) |
|---|---|---|
| 資格 | そのランで能力Lvが実際に9(キャラカードで開始時から9でも資格あり) | 変化なし |
| READY | 資格の地点から `readyMeters`(既定5,000m) | 金の枠が脈動+★(AWAKENED は ✦・白金寄り) |
| ACTIVE | LEVEL UP で FINAL EVOLUTION を選んだ | 明るい枠+残り(秒 / m) |
| USED | 効果が終わった(同じ能力はこのランでは `usesPerRun` 回まで=1) | 小さな◆の紋章(暗くしない) |

- 資格は**能力(元カードの cardId)ごと**。キャラカード / 合成カード / ラン中の取得は能力ごとに合算済み(GameManager.CardCap)なので、同じ能力が二重に進化することはない。
- 候補は通常の LEVEL UP だけ(ボス報酬 / BONUS ZONE / ULTIMATE には混ぜない)。3択のうち最大1枠、残りは通常の候補。デッキが全部 Lv9 でも FINAL EVOLUTION だけは出る。
- 断っても READY のまま。複数 READY は「候補に出た回数が少ないもの」から(同数はランダム)。
- 選ぶと: 名前の表示 / 画面の縁の光 / 0.08秒のヒットストップ / SE(LevelUp。`FinalEvolution.Activated` でフック可)/ オーラ → すぐ再開。

## 9枚の実装値(`FinalEvolutionTuning`、Resources/FinalEvolution/FinalEvolutionTuning.asset を置けば上書き)

| カード | 型 | 効果 |
|---|---|---|
| ATTACK UP | 10秒 | 最終ダメージ×1.5(`EffectiveAttackPower` の最後。攻撃の枠 AttackPct は変えない) |
| SPEED UP | 8秒 | 実速度×1.15(150km/h まで。元から速ければ上げない)/ 接敵の敵へ自動の小攻撃(攻撃力×0.5、1体0.5秒に1回)/ 障害物・敵の体との接触を受けない / 残像を最大 / カメラが8%引く |
| ATTACK RANGE UP | 10秒 | 射程×1.6(前の倍率との比で掛けて戻す)/ 命中時に先端から斬撃波(0.3秒に1回、proc の枠) |
| VAMPIRE | 10秒 | 吸収の確率+35% / 満タンで溢れた回復は Blood Shield(上限2回分)/ 終われば消える(通常の Shield にしない) |
| PHOENIX | 10秒 | 間に1回だけ致死の被弾から緊急復活(HP50%+短い無敵)。通常の PHOENIX Charge は使わない・増やさない。使わなければ消える |
| FLAME BLADE | 10秒 | 炎上の確率+35%・強さ×1.5 / 炎上させた相手の周り(2.6m)へ2体まで延焼。延焼からは延焼しない・同じ相手は1秒に1回・proc の枠 |
| THUNDER STRIKE | 10秒 | 落雷の確率+25% / 連鎖+1。1体へ0.4秒に1回の制限と、落雷から落雷を出さない決まりはそのまま |
| EXP UP | 次の2,000m | EXP の枠へ+0.6(曲線の**前**に足す。減衰の曲線は迂回しない) |
| GREED | 次の2,000m | MILE×1.5(敵・BONUS・ボス)/ 受けるダメージ×1.3(リスク) |

AWAKENED: 持続+10%、オーラが金寄りで少し大きい、終わりに光。通常の性能は変えない。

## CONTINUE / ラン終了
- `RunCheckpoint.Data.finalEvolution`(資格・資格の地点・READY・ACTIVE・USED・残り・候補に出た回数)。古いデータは空=状態なし。
- Game Over / ホーム / 新しいラン で消える(`GameManager.ResetCardStatsForRun` → `FinalEvolution.ResetRun`)。

## マルチ
候補に出さない(`disableInMultiplayer`)。必要な同期は `Unity/2ndAction/Docs/Multiplayer8.md`。

## 開発用
- DEBUG →「FINAL EVOLUTION TEST…」: キャラ / ステージ / カード / AWAKENED / Lv9開始 / READY開始 / ボス を選んで DEBUG RUN。「LEVEL UP(3択)」「今すぐ READY」「今すぐ終了」。
- 自動テスト: `-qaFinalEvo <dir>`(A〜T)。
