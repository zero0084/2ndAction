# 案A の実施結果・将来の作業・クローズドテストの開始条件(2026-10-31)

## 1. 案A: Target API を 36 に固定(実施済み)

- 変更: `ProjectSettings.asset` の `AndroidTargetSdkVersion: 0`(Automatic)→ **`36`** の1行だけ。ゲームの仕様・バランス・通信方式は変更なし。
- 確認(番号を進めない確認用の APK `Builds/SizeProbe/target36.apk` を `BuildAndroid.BuildProbe` で作成):
  - aapt2: **targetSdkVersion 36** / minSdkVersion 26 / compileSdkVersion 36。
  - AndroidManifest の中身(権限・部品・属性)が **APK 402 と完全に同じ**(行番号以外の差なし)。= 今回の変更は「今と同じ 36 を、今後も勝手に変わらないように書いた」だけ。
  - AAB(クローズドテスト用)も同じ PlayerSettings から作られるので 36 になります(署名済み AAB を作る時に `_build.txt` と合わせて確認)。
- LOCAL PLAY(DTLS)・ONLINE PLAY の確認:

| 確認 | 環境 | 結果 |
|---|---|---|
| LOCAL PLAY: IP 直接入力で HOST/JOIN | Windows 試験版 2 プロセス | HOST・JOIN とも `secure transport DTLS`、44 秒一緒に走行、例外 0・エラー 0 |
| LOCAL PLAY: 証明書の作成(DTLS の準備) | Android エミュレーター(target36.apk、IL2CPP) | `cert test: OK 4.24s` |
| ONLINE PLAY: 部屋の作成 → 参加 → 走行 | Windows 試験版 2 プロセス(UGS development、別の匿名プレイヤー) | 2 回とも 2/8 人で 57〜59 秒走行。1 回目は JOIN の最初の Relay 接続が失敗 → 組み込みの再試行(APK 296 から)で接続。2 回目は 1 回で接続。例外 0 |
| ONLINE PLAY: サービスの初期化・匿名サインイン | Android エミュレーター(target36.apk) | `-qaOnline` 全項目合格・例外 0・異常終了 0 |
| 本物の保存 | Windows | 試験の前後でレジストリが同一 |

- 補足: 同じ PC の 2 プロセスで「部屋の一覧から JOIN」(`-netAutoJoin lan`)を試すと、この PC はネットワークが複数(LAN と 100.x の VPN)あるため、見つけた部屋のアドレスが途中で 127.0.0.1 に置き換わり、その宛先の証明書が無く接続できませんでした。**同じ PC で試す時だけの条件**で(別の端末どうしでは HOST の LAN の IP で届く)、今回の設定変更とは関係ありません。実機 2 台の LOCAL PLAY の確認(マスター)で、部屋の一覧からの参加も確かめてください。

## 2. 将来の作業(今回は実装しない)

### 案B: API 37 向け ACCESS_LOCAL_NETWORK(2027年8月の target 37 必須化までに)
- AndroidManifest に `android.permission.ACCESS_LOCAL_NETWORK` を追加(target 37 にする時だけ。36 の間は追加しない)。
- LOCAL PLAY を開く時(CREATE / FIND / IP 入力の前)に許可を求める(`UnityEngine.Android.Permission.RequestUserPermission` で足りる見込み)。
- 拒否時: 「同じ Wi-Fi の端末と通信する許可が必要」の説明 + 端末の設定を開くボタン + ONLINE PLAY へ戻る導線。30 言語の文言(エステルのレビュー)。
- Play Console の「ローカルネットワーク権限」の申告(説明の案は report.md §2)。
- 試験: Android 16 端末で `adb shell am compat enable RESTRICT_LOCAL_NETWORK com.zero0084.action2nd` → 再起動で先に試せる。HOST(待ち受け)・JOIN・部屋の検索・IP 入力・テザリングのそれぞれ。

### 案C: 通信が拒否された時の理由の表示
- 部屋の検索の送受信の失敗(EPERM)と、接続のタイムアウトを「許可がない」と区別して画面に出す(LanDiscovery / NetSession)。案B と同時に。

### 案D: R8 — 導入しない(DEX 17.8MB でゲームの基準 50MB 未満)。Play Console の App bundle explorer に警告が出たら再検討。

### メモリ: クローズドテスト開始後、Play Console の Android vitals の「メモリ」(Anon RSS + Swap の P90)を見る。8 人マルチ・1 時間のランは実機で。

## 3. クローズドテストの開始条件(Target API の変更とは別)

| # | 条件 | 状態 | 担当 |
|---|---|---|---|
| 1 | **アップロード鍵の作成**(keytool。保存はリポジトリの外、鍵とパスワードは別の場所にも控える) | 未 | マスター |
| 2 | **署名済み AAB の作成**: `powershell -ExecutionPolicy Bypass -File Tools\ads\build_closedtest.ps1 -Keystore <鍵> -Alias <名前>`(Unity は閉じておく。パスワードは画面で入力)。出力 `Builds/ClosedTest/OneMoreMile_ct<番号>.aab`(Split Application Binary、製品版、テスト広告)。versionCode はこのビルドで +1(今 402 → 403) | 未(鍵待ち) | マスターが実行(アイリーンは結果の確認) |
| 3 | Play Console: アプリの作成(com.zero0084.action2nd)、最初のアップロードで **Play アプリ署名** を有効化 | 未確認 | マスター |
| 4 | クローズドテストのトラック・**テスター 12 人**(メールのリスト or Google グループ)・参加用リンクの共有。個人の開発者アカウントは「12 人以上が 14 日間続けて参加」が製品版を申請する条件 | 未確認 | マスター |
| 5 | アプリのコンテンツ: プライバシーポリシーの URL、**データ セーフティ**(削除の方法をアプリ内削除の形へ書き直すのが残り)、広告あり、**広告 ID**(使用: 広告)、コンテンツのレーティング(IARC)、ターゲット層 | 一部残り | マスター(文案はアイリーン) |
| 6 | ストアの掲載情報(名前・説明・アイコン 512・フィーチャー グラフィック・スクリーンショット)。素材は Docs/StoreAssets_2026-10-26 | 素材あり | マスター |
| 7 | 広告: クローズドテストは **テスト広告のまま**で可(本番広告・app-ads.txt は公開前、指示待ち) | 対応済み | — |
| 8 | 広告なしパス: 今は「準備中」(購入なし)。テスターに購入を試してもらうなら、Play Console の商品・ライセンス テスター・Google Play の公開鍵(IAP Tangle)が必要 | 判断待ち | マスター |
| 9 | 製品版は UGS **production** に接続(ProductionReady=true)。テスターのラン・ランキングは本番に記録される(以前の判断どおりで良いかの確認) | 確認 | マスター |
| 10 | Play Console の通知の確認: **Trader の申告**(11月から)・**日本の決済の認証**(12/1、課金を有効にするなら特に) | 未確認 | マスター |
| 11 | 実機での最終確認(起動ロゴ・布の表現・操作・LOCAL PLAY の端末 2 台・ONLINE PLAY) | 未 | マスター |

Target API(案A)は完了したので、**今いちばん先に必要なのは 1(アップロード鍵)→ 2(署名済み AAB)** です。
