# カード長期育成(Mastery / AWAKENED)(2026-10-04)

Lv9 まで育てたカードをゴールにせず、その後の重複カードにも価値を持たせる。最重要方針は「今まで上げたカードLvを絶対に無駄にしない」。

```
Lv1〜9 → Lv9 MAX → Mastery ★1〜5 → AWAKENED
```

- Lv9 は今まで通り MAX。Lv10 以上は作らない。Lv1 へ戻すこともしない。
- Mastery / AWAKENED は所持Lvとは別のメタ育成値。通常の性能(攻撃/速度/EXP など)は一切変えない。

## 1. 現行の Lv / 合成の構造(確認結果)

| 何のLv | どこに | 意味 |
|---|---|---|
| コレクション(所持)のLv | `CardInventory`(PlayerPrefs `OwnedCardsV1`)| 束(キー, Lv, 枚数)。キーは素のID(Lv1)か `v2\|主ID\|Lv\|レア度\|能力*強化量/…`。Lv = 合成した枚数の合計(1〜9)|
| 合成で上がるLv | `CardFusionLogic` | 同名は2枚のLvの合計(100%)。異名は抽選。合計が Lv9 を超える組み合わせは合成不可だった(今回、同名だけ変更)|
| ラン中のLv | `GameManager.runAbilityStacks` | 能力ごとにそのランで適用した回数。上限9(能力ごと)|
| キャラカードのLv | キャラカード枠(キャラごと)| 付けたキーの強化量を、ランの開始時に能力ごとの Lv9 上限を守って適用 |
| デッキのカードのLv | デッキ(キーだけ)| レベルアップの候補。選ぶとそのキーの強化量を適用(Lv9 のカードなら1回で能力 Lv9)|
| 同一能力の Lv9 上限 | `ApplyRunCardCapped` | キャラカード/取得/合成の表記に関係なく、能力ごとに9まで |

その他の確認結果:
- ガチャは常に Lv1 を1枚、所持に足す。
- レベルアップで選んだカードは所持には入らない(ランの中だけ)。
- 余ったカードは既存の CONVERT(デッキ編集)で1枚 100 MILE に変換できる。

## 2. 保存構造

新しいキー `CardMasteryV1`(JSON、進行のカテゴリ。SaveKeys に登録)。カードの種類(主能力のカードID)ごとに次を持つ。

| 項目 | 意味 |
|---|---|
| level | ★の数 0〜5 |
| progress | 次の★までの進み |
| overflow | ★5 の後に余った分(保管)|
| awakened | ★5 に一度でも届いたら true のまま(閾値を後で変えても外れない)|
| maxReached | Lv9 MAX に一度でも届いた記録 |
| totalGained | 入った量の合計(確認用)|

その他:
- 所持カードの保存(`OwnedCardsV1`)は変えていない。
- 合成の確定では、所持カード / MILE / Mastery をメモリで変えてから両方書き、`PlayerPrefs.Save()` を1回だけ呼ぶ(途中までの保存を作らない)。

## 3. ★1〜5 の必要量

`MasteryTuning`(`Resources/Mastery/MasteryTuning.asset`。無ければコードの既定値)。

| | ★0→★1 | ★1→★2 | ★2→★3 | ★3→★4 | ★4→★5 | 合計 |
|---|---|---|---|---|---|---|
| 必要(枚分) | 1 | 2 | 3 | 4 | 5 | 15 |

レア度で必要量は変えない(全カード共通)。

## 4. Lv9 後の重複カード / 5. 余りの繰り越し

**合成で使う。勝手に消費しない。**

- **Lv9 MAX のメイン + 同じカードの素材**(新しい「MASTERY」合成):
  - 素材だけを消費して、素材のLv分の Mastery(Lv1 = +1、Lv3 = +3、Lv9 = +9)。メインはそのまま。
  - メインは消費しないので、デッキ/キャラカードで使用中でも選べる(素材の1枚だけ空いていればよい)。
- **同名で合計が Lv9 を超える合成**(以前は合成不可):
  - 完成品は Lv9 MAX。超えた分(合計 − 9)を Mastery へ。能力の強化量も9まで。
  - 例: Lv5 + Lv5 → Lv9 MAX + Mastery 1。Lv8 + Lv3 → Lv9 + Mastery 2。
  - 「Lv9 になった瞬間に余りが消える」ことは無い。
- **繰り越し**: 昇格で余った分は次の★へ。一度に大量に入っても、続けて★を進める。
  - 例: ★1 0/2 に +3 → ★2 1/3。★2 1/3 に +9 → ★4 3/5。
- 異名で合計が Lv9 を超える組み合わせは、今まで通り合成不可(何も消費しない)。
- 素材が合成カード(サブ能力つき)の場合、サブ能力は引き継がれない(合成前の説明に出す)。

## 6. ★5 後の余剰

- AWAKENED のカードをメインにした MASTERY の合成は止める。カードを消費しない(説明:「★5後の余ったカードの使い道は今後」)。
- ★5 を超える分が入る場合(Lv9 超えの合成、または大量の素材で一度に★5 を越えた時)は、`overflow` に保管する。消えない。
  - 合成の画面と詳細の画面に「保管」と出す。
- 余ったカードそのものは所持に残る。
- 安全な還元の候補: 既存の CONVERT(1枚 100 MILE、デッキ編集の詳細から、確認つき)。
  - `overflow` を将来の最終用途(Final Evolution の強化、称号、装飾など)へ回すこともできる。

## 7. AWAKENED の判定

- ★5 に届いた時点で `awakened = true`(永久)。
- デッキから外す / キャラ変更 / 再起動 / CONTINUE / アップデートでも外れない。
  - カードの所持とは別の保存で、キャラ/デッキ/ランの状態を見ないため。
- 全カード共通の報酬(見た目):
  - 金色の光(呼吸)
  - まわりを回る小さな粒
  - 上端の「AWAKENED」表記
  - ★★★★★
  - コレクションの総数
- カード固有の効果は `AwakenedEffects`(データ)へ後から足せる。
  - 予定を6件登録済み: ATTACK UP / SPEED UP / VAMPIRE / PHOENIX / DOUBLE ATTACK / ULTIMATE。
  - 実装済みの物はまだ無い(`implemented = false` の間は何も起きない)。
  - ゲーム側は `AwakenedEffects.Active(cardId, kind)` を見る。

## 8. コレクションの表示

- **一覧**: Lv9 のカードの下端に ★の帯(埋まった★は金、まだの★は暗い色)。
  - AWAKENED は光/粒/上端の「AWAKENED」。
  - Lv9 未満のカードには何も足さない(情報を増やしすぎない)。
- **詳細**: `Lv.9 MAX ★★★☆☆ Mastery 2 / 4`。AWAKENED なら `★★★★★ AWAKENED`(保管があれば数も)。

## 9. 合成の画面

- Lv9 MAX のカードを選ぶと「MASTERY」に切り替わる。今の★と進み、同じカードを素材にすると進むことを出す。
  - 以前は「上限のため合成できません」だった。
- 素材を選ぶと、合成前の説明が出る:
  - 今の★
  - 今回の合成 +n Mastery
  - 合成後の★/進み
  - 繰り越し / 保管 / サブ能力が消えること
  - 「通常の性能は上がらない」
- 演出は同名強化と同じ流れ(素材の光がメインへ集まる)。締めは「MASTERY ★★★☆☆」/「AWAKENED！」。
- 結果の画面に Mastery の変化(★の前後、繰り越し、保管、AWAKENED)を出す。
- ★5 の時は「AWAKENED」と表示して、合成のボタンは押せない(カードを消費しない)。
- 一覧の各カードの下に `★n` / `AWAKENED`。

## 10. MAX / AWAKENED の総数

- コレクションの見出し: `COLLECTION (n)  MAX 37/100  AWAKENED 12/100`。
- 総数はカードの一覧(`CardDatabase.AllCards`、素のカードだけ)から毎回数える(100 を固定値で持たない)。今は #100 ULTIMATE を含めて 100。
- 節目の解放(10 / 25 / 50 / 75 / 99 / 全カード)用の入口:
  - `CardMastery.AwakenedCount`
  - `CardMastery.MaxCount`
  - `CardMastery.TotalCards`
  - `CardMastery.AllAwakened`
  - `CardMastery.ReachedAwakenedMilestone(n)`

## 11. 既存セーブの移行 / 12. 既存の Lv9

- セーブ形式の版を 2 → 3(`SaveSystem.Migrate2To3`)。
  - 所持カードの Lv / 枚数 / キーは一切変えない。
  - 新しいキー `CardMasteryV1` を作る。今所持している Lv9 のカードは「Lv9 MAX 到達済み」として記録し、Mastery は ★0 から。
  - 過去に Lv9 の後で消費/変換したカードの枚数は保存されていない(合成は Lv9 超えを止めていた、変換は MILE にしただけ)。なので推測で Mastery を付けない。
- 起動のたびに、所持している Lv9 は記録へ足す(何度呼んでも同じ)。
- 壊れた `CardMasteryV1` は、既存の検査で直近の正常な控えへ戻す(無ければその項目だけ初期値)。所持カードには影響しない。

## 13. ガチャ

- Lv9 MAX のカードを引いてもハズレではない。カードは Lv1 として所持に入るだけで、勝手に Mastery へは消費しない。
- プレイヤーが合成の画面で、Lv9 MAX のカードの素材にする(+1)。
- ガチャの結果に `→ 合成で MASTERY +1`(AWAKENED 済みなら `(AWAKENED済み・保管)`)と出す。

## 14. キャラカード / デッキ

- AWAKENED のカードを付けても、通常の Lv9 の性能は変わらない(自動テストで、AWAKENED と ★0 で全部の数値が同じことを確認)。
- 同一能力の Lv9 上限はそのまま。Mastery は能力の回数に数えない。
  - 例: Lv9 ★5 を付けて、さらに同じカードを取っても Lv9。

## 15. Final Evolution の入口(本体は別工程)

`CardProgression`:

| 入口 | 意味 |
|---|---|
| `IsMaxReached` / `MasteryLevel` / `MasteryProgress` / `MasteryNeed` / `IsAwakened` | メタ育成 |
| `RunAbilityLevel` / `ReachedMaxInRun` | ラン中の能力Lv |
| `RunStartLevel` / `MaxAtRunStart` | キャラカードを付けた直後(ランの開始時点)の能力Lv。CONTINUE でも同じ |
| `FinalEvolutionEligible` | 暫定: ランの中で Lv9 に届いた(開始時に既に Lv9 だった分は除く)|
| `FinalEvolutionAwakenedBonus` | AWAKENED 済みなら追加の特典を付けられる(必須ではない)|

**現行仕様との関係(報告)**:
- 所持 Lv9 のカード(強化量9)をキャラカードに付けると、ランの開始時点で能力 Lv9 になる。
- デッキの Lv9 カードをレベルアップで選ぶと、1回で Lv9 になる。
- 「所持Lv9 だからランの最初から資格」にはならないよう、開始時の Lv9 は資格に数えない形を暫定にした。
- ただし、その場合キャラカードに Lv9 を付けたカードは、そのランで資格を得られない。どう扱うかは Final Evolution の工程で決める。

## 16. #100 ULTIMATE

- 同じ育成(Lv1〜9 → Lv9 MAX → ★1〜5 → AWAKENED)にそのまま対応する(カードとして特別な処理なし)。
- AWAKENED の固有効果は「必殺技の最終演出/専用オーラ/BUFF演出/締めのVFX」として予定に登録した。ダメージは上げない。
- ULTIMATE の動き自体は変えていない(回帰テストで確認)。

## 17. 開発メニュー「MASTERY TEST」(開発版のみ)

DEBUG → 「MASTERY TEST…」。

- テストの間は DEBUG RUN(保存を止める)。終える、またはパネルを閉じると、始める前の所持カードと Mastery へ戻る。
- 操作:
  - カードの選択
  - Lv8×1 + Lv1×3 を付与
  - 合成 Lv8 + Lv1 → Lv9
  - 合成 Lv9 + Lv1(+1)
  - Lv5×2 → 合成(余りの繰り越し)
  - Lv9 + Lv9(+9)
  - ★を0へ
  - Mastery +1 / +3 / +5 / +15
  - Save → Load の一致
- 表示: 所持(Lvごとの枚数)/ Lv9 MAX / ★ / 進み / 保管 / AWAKENED / 全体の MAX・AWAKENED / 必要量。

## 18. 必要量やルールを変える時

- 必要量: `MasteryTuning.need`(★の数は保存しているので、後で必要量を変えても★は下がらない。AWAKENED も外れない)。
- 固有効果: `AwakenedEffects` の表に追加して、ゲーム側で `Active(cardId, kind)` を見る。

## 19. 自動テスト(Windows、`-qaMastery <dir> [-qaMasteryOnly ABCDEFGHIJKLMN] [-qaMasteryShots 1]`)

全項目合格・例外0。

| | 内容 |
|---|---|
| A | Lv8 + Lv1 → Lv9 MAX(Lv9 到達の記録、★0)|
| B | Lv9 MAX + 重複1枚 → ★1(メインは残り、素材だけ消費)|
| C | 繰り越し(★1 0/2 に +3 → ★2 1/3)/ Lv5 + Lv5 → Lv9 MAX(強化量9)+ Mastery 1 |
| D | +9 を一度に → ★2 1/3 から ★4 3/5(★を2つ跨ぐ)|
| E | ★5 = AWAKENED |
| F | AWAKENED の後もカードは Lv9(Lv10 以上なし)|
| G | AWAKENED と ★0 で、Lv9 のカードの全部の数値が同じ |
| H | 旧セーブ(schema 2、Mastery のキーなし)→ 3: Lv/枚数はそのまま、既存 Lv9 は MAX の記録・★0。2回目の起動は何も変えない。壊れた Mastery の JSON は修復され、所持カードは無事 |
| I | 保存 → 読み直し(再起動相当)で一致 / JSON の往復で一致 / CONTINUE をはさんでも一致 |
| J | AWAKENED の Lv9 をキャラカードに付けて、さらに同じカードを取っても能力は Lv9(上限を超えない)。Final Evolution の入口: 開始時 Lv9 は資格なし・AWAKENED の特典は読める。キャラカードなしでランの中で Lv9 に届くと資格あり |
| K | ガチャで Lv9 MAX のカードの重複を引いても、カードとして残る(勝手に Mastery へ消費しない)。合成で Mastery に使える(デッキで唯一の1枚を使っている時は「使用中」で止まる)|
| L | AWAKENED への合成は止まり何も消費しない / ★5 後の加算と Lv9 超えの合成は保管(overflow)へ |
| M | 総数はカードの一覧から(100、#100 ULTIMATE を含む)/ 節目の判定 / 調整用のアセットが読める |
| N | 合成の画面: Lv9 MAX を選ぶと MASTERY、合成前に +1 を表示、画面から合成して★が進む。コレクション: 見出しの MAX/AWAKENED、AWAKENED の見た目 |

## 20. 回帰

| テスト | 結果 |
|---|---|
| ULTIMATE(-qaUltimate ALGSEBRT)| 合格 |
| カード v3(-qaCardV3 TDHXPSBECR: 99枚/死にカード/HP封印/12キャラ互換/属性・PHOENIX/速度/DPS・ボス/EXP/負荷/CONTINUE)| 合格 |
| セーブ(-qaSave)| 合格(移行の多段/失敗のテストを、版3が実際の移行になったのに合わせて「版3以降の仮の手順」へ更新)|
| CONTINUE(-qaResume)/ CARD TEST(-qaCardTest)/ ボス(-qaBoss)| 合格 |
| CardFix(-qaCardFix)| 既存の不合格1件のみ(洞窟の走行距離 1572m。今回の前から同じ)|
| 合成(Editor の FusionAutoTest)| Card Balance V3 以降動いていなかった(AttackPower→AttackPct)のを直して実行。新しいルールを含めて合格。残り1件は 2026-10-02 のキャラごとのキャラカード枠に追いついていない古い確認(今回と無関係、別タスク)|
| マルチ 2プロセス | HOST/JOIN とも例外0・エラー0 |
| リリースビルドのコンパイル | 合格 |

| APK | versionCode 256 |
