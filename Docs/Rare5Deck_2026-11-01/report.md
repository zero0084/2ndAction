# デッキ編集で★5カードが表示されない不具合(2026-11-01)

## 原因
- デッキ編集の「所持カード」一覧は、シーンを作る時に用意した **160 個の固定の枠**(`SceneBuilder.cs` の `ownedPoolSize = 160`、2026-09-26 のカード合成改修から)に、
  所持の束(同じカードでも Lv 違い・合成の違いは別の束)を **並び順(sortOrder)の先頭から** 入れていた
- 160 を超えた束は枠が無く、表示されなかった。並び順の最後が★5(PHOENIX 161 … #100 ULTIMATE 183)なので、★5 から先に消えた
- 種類の絞り込みも「160 の枠のうち、合わない枠を隠す」だけだったため、★5 の種類で絞っても出なかった
- カード合成は一覧のカードを毎回必要な数だけ作る方式なので、上限が無く★5 も出ていた(画面ごとの違いはこれ)
- 2026-10-31 に属性カード 12 枚が加わって 112 種になり、所持の束が 160 を超えやすくなったことで表に出たと考えられる
  (10-30〜31 のデッキ変更 = 重複禁止・プリセット・タイトル/分解・Windows 操作は、一覧の作り方・絞り込み・並べ替えを変えていない)

### 問題のあったコード(修正前の DeckEditUI.Refresh)
```
displayedStacks.AddRange(CardInventory.Stacks);
displayedStacks.Sort(... sortOrder → Lv ...);
for (int i = 0; i < ownedCards.Length; i++)   // ownedCards は 160 個
{
    if (i >= displayedStacks.Count || displayedStacks[i].count <= 0) { 隠す; continue; }
    ...
    if (!MatchesFilter(card.category, activeFilter)) { 隠す; continue; }   // 絞り込みは枠を隠すだけ
```

## ★5 を外す条件・ULTIMATE だけの条件
- **ありませんでした。** 一覧(デッキ編集/合成)、デッキに入れる処理(`GameManager.AddToDeck`)、ドラッグ、長押し、分解のどこにも、
  レア度(rarity<=4 など)・★5 除外・ULTIMATE 専用の条件はありません。削除した条件はありません(仕様の変更もなし)
- #100 ULTIMATE(character_ultimate)の特別な扱いは、どれも既存のゲームの決まりで、デッキ編集の表示・追加・分解には関係しません(今回は変更なし)
  - マルチプレイ中はラン中の3択に出さない(`UltimateArt.Offerable`)
  - COMBO の構成に使わない(`ComboSystem`)/ FINAL EVOLUTION の対象外(`FinalEvolutionTuning`)。詳細画面にも「対象外」と表示される
  - AWAKENED の専用演出(`CardProgression`)、ガチャ/詳細の★5 の光り方(見た目だけ)

## 修正
- `DeckEditUI.OwnedStacksSorted()`: 所持一覧の元を1か所に(1枚以上持っていて、カードとして読める束。sortOrder → Lv の順。レア度・種類・解放の条件は付けない)
  - デッキ編集とカード合成の両方がこれを使う(合成の絞り込み・並べ替えは合成画面のまま。大きな作り直しはしていない)
- デッキ編集の一覧は「所持の束を作る → 種類で絞る → 足りない枠は複製して増やす」の順に
  - 「所持カード (n)」は今までどおり絞り込みに関係なく全部の数
- 変更したファイル: `DeckEditUI.cs`、`CardFusionUI.cs`(試験: `QaSweep.Rare5.cs`、`QaSweep.cs` に `-qaRare5`)
- 既存のデッキ・セーブの形式・読み込み・重複の移行は変更していません

## 確認(Windows、試験用の製品名の別ビルド + -fileSave の試験用データ。本物のセーブは前後で変化なし)
所持の束を 225(以前の 160 枠を超える)にした状態で `-qaRare5` を横 1280x720 / 縦 486x1080 で実行。全項目合格・例外 0。

| | 内容 | 結果 |
|---|---|---|
| A | ★1〜★4(speed_up / jump_count_up / berserker / momentum)が「全て」に表示 | OK |
| B | ★5 全8枚(PHOENIX / ALMIGHTY / ONE MORE MILE / DEATH'S CONTRACT / NO TURNING BACK / THE LONG ROAD / PANDEMONIUM / #100 ULTIMATE)が所持していれば表示。合成の一覧の元にも全部入る | OK(ガチャの解放条件を満たしていない試験データでも、所持していれば表示) |
| C | 「全て」で所持の束 225/225 が表示 | OK |
| D | 種類(Heal / Special / Risk / Growth)ごとに、その★5が表示 | OK |
| E | タップで追加(PHOENIX) | OK |
| F | ドラッグで空き枠へ追加(ALMIGHTY) | OK |
| G | 同じ★5の別Lv(PHOENIX Lv3 の合成のキー)をタップ/ドラッグ → 重複禁止で入らない | OK |
| H | 長押しで大きな詳細(#100 ULTIMATE、NO TURNING BACK) | OK |
| I | 未使用の★5を分解 → 1枚減って +250 MILE | OK |
| J | デッキで使用中/キャラカードに設定中の★5は分解できない | OK |
| K | ★5(Lv3 の合成のキー・ULTIMATE)を入れたデッキとキャラカードが再起動後もそのまま。重複の移行で何も外れない | OK |
| L | デッキの★5をラン中に取得 → 3枚とも強化として反映 | OK |
| M | #100 ULTIMATE: 表示・タップ追加・長押し・ラン中に取得するとカードを持った状態になりゲージが溜まる | OK |

回帰(同じ修正のビルド): -qaDeckV2(横/縦)・-qaDeckDup all・-qaDeckPreset・-qaKeyNav・-qaPad すべて合格・例外 0

## 画像
- r5_all.png(全て)/ r5_category_Heal.png(種類で絞った時の★5)/ r5_big_detail_ultimate.png(縦画面の長押し)/ r5_after_restart.png

## Android エミュレーター(APK 408、2026-11-01 0038、試験用の保存ファイル -fileSave)
| 試験 | 結果 |
|---|---|
| -qaRare5 横 2400x1080 | 35 項目すべて合格・例外 0・Unity のエラー 0 |
| -qaRare5 縦 1080x2400 | 35 項目すべて合格・例外 0・エラー 0 |
| -qaRare5 plant → アプリを終了して起動し直し → check | 合格(デッキ `v2|phoenix|3|...`(Lv3 の合成)/ character_ultimate / one_more_mile、キャラカード deaths_contract がそのまま)・例外 0 |

Windows 製品版(One More Mile Test)のビルド: エラー 0。タイトル → ホームの開始(Enter/クリック/Space)も従来どおり。本物のセーブは前後で変化なし。

実機は未確認です(マスターの実機での最終確認をお願いします)。
