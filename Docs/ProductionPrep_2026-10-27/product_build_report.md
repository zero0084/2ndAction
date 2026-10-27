# 製品版ビルドと最終確認の報告(2026-10-28)

マスターの指示で `ProductionReady = true` にして製品版をビルドし、production の本物のサービスで最終確認をしました。

## 1. 製品版

| 項目 | 内容 |
|---|---|
| Commit ID | `975d4967`(ProductionReady = true)/ `cdc316af`(APK 391 の版の記録。中身は版番号の2行だけ) |
| APK versionCode | **391**(versionName `2026-10-27 2311`、パッケージ `com.zero0084.action2nd`) |
| ビルド場所(Android) | `Unity/2ndAction/Builds/ClosedTest/OneMoreMile_ct391_debugsigned.apk`(412MB)/ 使った設定の記録 `OneMoreMile_ct391_build.txt` |
| ビルド場所(Windows) | `Unity/2ndAction/Builds/WindowsProduct/OneMoreMile.exe` |
| 種類 | 製品版(Development Build なし・debuggable なし)、テスト広告(本番広告への切り替えはしていない)、**デバッグ鍵の署名**(アップロード鍵が無いため。Play には上げられない) |
| ProductionReady | `AccountDeletion.ProductionReady = true`。製品版の 設定 → データとプライバシー → データの削除 で、アプリの中で削除できる |

## 2. 最終確認

Windows の製品版は、別名の試験用(製品名「One More Mile Test」/「One More Mile Test 2」)で試しました。中身は製品版そのままで、確認用の追加の定義はありません。

| 確認 | 結果 |
|---|---|
| データ削除が製品版で有効 | 製品版で削除の画面が出て、「削除する」でサーバーのデータとアカウントが消えた(2人分)。ログ `online data deleted (cloud save 3, leaderboards 1)` |
| production へ接続 | 起動ログ `UGS Environment = production (release build, release build (fixed))`(2つとも) |
| ランキングが読める | 参加 → 表を読み込み「まだ記録がありません」(production の表は空で正しい)。読み込みの失敗なし |
| 開発用 DIAG が出ない | ランキングの画面に DIAG のボタンなし。ホームに DEBUG なし。DIAG のコードは製品版では消える作り(`#if UNITY_EDITOR \|\| DEVELOPMENT_BUILD`)。診断の重ね表示は DEBUG モードの時だけで、製品版では DEBUG モードにできない |
| 確認用の自動操縦が入っていない | APK と Windows 製品版の中を検索して `ProdVerifyPilot` は0件(確認用ビルドには入っていることも確かめた)。実際に操作しないランは 31m で倒れて FAILED |
| Secret / Service Account の鍵が APK・ログに無い | APK 全体と Windows 製品版の全ファイルを検索: `OMM_ADMIN_BASIC`・秘密鍵の書式(BEGIN PRIVATE KEY / "private_key")・service account は0件。配置・監査の道具(Editor 専用)も入っていない。試験のログにも無し |
| 「HOSTとの接続が切れました」 | ONLINE と LOCAL の両方で、HOST が部屋を閉じると JOIN 側にこの文で表示 → OK で閉じられ、元の画面へ戻る |
| 自分で退出した時 | ONLINE / LOCAL とも、退出してもエラーの画面は出ない(ログ `Disconnected after local leave`) |
| Single Player | 出発 → 走行 → 倒れる → FAILED → ホームへ戻る。記録の対象外(脱出していない)で正しい |
| ONLINE PLAY | Relay(asia-northeast1)で部屋の作成 → 検索 → 参加(2/8)→ 同時スタート → お互いの姿 → 両方に同じ CO-OP RESULT。遅延 60〜115ms |
| LOCAL PLAY | 暗号化(DTLS)で部屋の作成 → 検索 → 参加 → 同時スタート → お互いの姿 → 両方に同じ結果。遅延 61〜75ms |
| 例外 | 4つのログで実際のエラーは0。1件だけ出た `lobby not found` は、HOST が先に ONLINE の部屋を消した後に JOIN が抜けようとした時の記録で、動作に影響なし |
| production に試験の記録を残さない | 試験で作ったプレイヤー2人をアプリの削除で消し、読むだけの監査で確認: 4表とも0件、昨日の3人と今日の2人の計5人とも 記録・Cloud Save・アカウントなし |
| 本物の保存 | 本物の「One More Mile」の保存は前後で1バイトも変わっていない。試験用の保存は試験前の状態へ戻した |

### 補足
- 製品版の Windows ビルドの出力にある `One More Mile_BurstDebugInformation_DoNotShip` フォルダは配らない物(Unity が作るデバッグ情報)。配る時は除く。
- 試験の途中で、2つ目の試験用の exe に Windows ファイアウォールの「ブロック」の規則ができました(確認の画面が閉じられた時にできた物と思われます)。設定は変えずに、HOST と JOIN の役を入れ替えて試験しました。不要なら Windows のファイアウォールの設定から消せます(マスターの操作)。

## 3. 公開前に残っている作業

| 作業 | 担当 |
|---|---|
| アップロード鍵の作成と、それで署名した AAB(Split Application Binary)のビルド(`Tools/ads/build_closedtest.ps1`、パスワードは画面で入力) | マスター(鍵)→ アイリーン(ビルド) |
| プライバシーポリシーと Play Console のデータセーフティの「削除の方法」を、アプリの中で削除できる形に合わせて直す(今はメールでの依頼の書き方) | マスター(公開中のページ・Console)。文案はアイリーンが用意できます |
| 本番広告への切り替え(AdMob の本番の広告ユニット、`OMM_ADS_PROD`)と app-ads.txt | マスターの指示待ち |
| 広告なしパスの購入の有効化(Google Play の公開鍵 → IAP Tangle、Play Console の商品) | マスター |
| 実機での最終確認(起動ロゴ・布の表現・操作・LOCAL PLAY の端末2台) | マスター |
| ストアの掲載情報・スクリーンショット・紹介動画の最終の差し替え | マスター判断 |
| 一般公開 | マスター |
