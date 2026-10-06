# GameOver → FAILED → Tap to Retry が効かない件(2026-10-06)

実機(Android)の報告: 自然洞窟 61,502m、ボス戦(ラン再開済み、次の関門 59,000m を保留中)で死亡。FAILED の結果画面は出て「Tap to Retry」も表示されるが、タップしても何も起きない。
診断: timeScale=1.00 / 停止理由なし / HitStop 0 / カード選択の処理は完了(Sequence Complete)/ 補助「発動中」/ Encounter「PAUSED」。

## 直接の原因(最有力。Windows では指の入力を再現できないので実機での確認が残る)
結果画面のタップは `GameManager.Update` → `WasTappedOrClicked()` で判定していた。ここは
- **1本目の指(touch #0)が触れた瞬間**か、マウス(Android ではタッチから作られる擬似マウス = 1本目の指)の押下だけを見ていた。
  画面に別の指が残っている(死んだ瞬間にフリック中だった指/画面の端に触れている手/Android がシステムのジェスチャーで指の離れを届けなかった「残ったタッチ」)と、
  新しいタップは 2本目以降の指になり、**どのタップも受け付けられない**。
- さらに `UiInputGate`(設定/DEBUG パネルを閉じた時の「指が離れるまで背後へ通さない」ラッチ)は「指が0本」になるまで外れないので、残ったタッチがあると永久に塞がる。

どちらも時間の停止/ボス/Encounter の状態とは無関係に「タップが入力として認識されない」ので、診断の内容(時間は動いている/停止理由なし)と一致する。
診断の「補助 発動中」は、ランの終わりで表示用のフラグが戻っていなかっただけ(補助の処理そのものは IsGameOver で止まっていた)。
「Encounter PAUSED」は IsGameOver で止まった正常な状態。DEBUG 表示(左の DEBUG TOOLS / 右の ENCOUNTER)は IMGUI でタップの判定(Input の読み取り)を奪わない。

## 修正
- 結果画面の入力を Gameplay から独立させた(`GameManager.ResultTapThisFrame`):
  **どの指でも**新しく触れたら/クリック/R キー。パネルを閉じた指のラッチは見ない。画面の切り替え(遷移)中と、設定パネル/DEBUG パネルが本当に開いている間だけ待つ。
  受け付けなかった時は理由を `[Death] Result tap ignored: …` として診断ログへ残す(実機で再発したら原因が確定できる)。
- 状態遷移を一本に: GAMEPLAY → PLAYER DEAD → **GAME OVER CLEANUP**(`GameOverCleanup`、冪等)→ RESULT。
  - ボス: AI と攻撃のコルーチンを止める、増援の待ち/予告/ラン再開/保留を捨てる(`BossManager.StopForRunEnd`。撃破/報酬/保存/次の関門の決定は通さない)
  - 補助: 判断と表示の状態を止める(`HighSpeedAssist.StopForRunEnd`)
  - 入力: パネルのラッチを外す、ポーズ/ホームへ戻るの確認を閉じる
  - 死神: 既存の `ReaperBase.StopAllForRunEnd`
  - 何度呼ばれても1回だけ(二重のシーン読み込み/報酬/保存は起きない)。
- 遷移の見張り(`ScreenTransitionManager.Update`): 遷移が8秒を超えて終わらない時は外す(覆いも消す)。止まった遷移がすべてのタップを塞ぐことを防ぐ。
- 記録: `Result tap detected -> Retry requested` → `Retry -> Scene/Run reset start` → 新しいシーン。GameOver cleanup の各段階も `[Death]` に残る。

## Retry 後の状態
シーンの読み直しで、ボス/Encounter/補助/保留/ラン再開/遷移はすべて新しいラン用になる(静的な状態: RushEnabled/SuppressGates 等は既存の初期化)。
自動テスト `QaSweep -qaRetry`(A〜O + 天空の竜): 死亡 → 結果画面 → 実際のタップと同じ判定を1回 → 新しいランで
時間/停止理由/ヒットストップ/ボス/保留/ラン再開/補助/遷移/入力の塞がり/距離 を確認し、次のランが普通に走ることまで確認。

## 実機で確認が必要
- 指を画面に残したまま(フリックの途中で死亡する等)で、別の指のタップで Retry できること。
- 再発した時は 診断ログ の `[Death] Result tap ...` の行を共有してください。
