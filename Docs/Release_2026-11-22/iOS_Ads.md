# iOS の広告(リワード/インタースティシャル)(2026-11-22)

iOS 版でも、Android と同じ広告の仕組み(`AdManager` / `AdMobAdService`)を使うようにしました。iOS の購入(広告なしパス)は入れていません。

## 設定
| 項目 | 値 |
|---|---|
| AdMob アプリ ID(iOS) | `ca-app-pub-2863190967263764~1910097904`(Info.plist の `GADApplicationIdentifier`。`GoogleMobileAdsSettings` の iOS 欄) |
| リワード(本番) | `ca-app-pub-2863190967263764/2045829270`(OMM_iOS_Rewarded) |
| インタースティシャル(本番) | `ca-app-pub-2863190967263764/6532915373`(OMM_iOS_Interstitial) |
| リワード(テスト・今回使う) | `ca-app-pub-3940256099942544/1712485313`(Google 公式の iOS 用) |
| インタースティシャル(テスト・今回使う) | `ca-app-pub-3940256099942544/4411468910`(Google 公式の iOS 用) |
| Bundle ID | `com.zero0084.action2nd` |

- 値は `Unity/2ndAction/Assets/Scripts/Monetization/MonetizationServices.cs` の `MonetizationConfig` にあります。

## テスト広告と本番広告の切り替え
- 切り替えは Android と同じ `OMM_ADS_PROD` の定義です。
  - iOS の定義に `OMM_ADS_PROD` が**無い**時は、TestFlight の版も含めて必ず Google の iOS 用テスト広告ユニットを使います。Editor や Development Build の判定には頼りません。
  - リポジトリの ProjectSettings にも、この PC の iOS の定義にも `OMM_ADS_PROD` はありません。Unity Cloud の iOS の設定にも足さないでください。
- 確かめ方:
  - iOS のビルドの記録(Unity Cloud のログ)に `[IosAds] adsProfile=test units=ios-test rewarded=…/1712485313 interstitial=…/4411468910` が出ます。
  - アプリの「お問い合わせ」のメールの下書きの「広告の記録」に `ads iOS profile=test units=ios-test` が残ります。
- **本番へ切り替える時(App Store で公開する版だけ)**
  1. Unity Cloud の、App Store 用の iOS ビルドの設定の Scripting Define Symbols に `OMM_ADS_PROD` を足します。TestFlight の試験用の設定には足しません。
  2. そのビルドの記録で `[IosAds] adsProfile=production units=ios-production` を確かめます。
  3. 本番広告の版は、開発者・テスターが何度も広告を出す試験に使いません(AdMob の無効なトラフィックの扱いになるため)。

## 動き(Android と同じ仕様)
- インタースティシャル: FAILED の結果画面からだけ。起動から 5 分、前回の広告から 5 分あける。
- リワード: 成功したランの MILE 2 倍、ホームの広告ガチャ(1 日 3 回、日本時間 0 時に回復)。
- 報酬は SDK の報酬通知でだけ渡し、1 回の表示で 1 回だけです。閉じただけ・失敗では渡しません。通知が遅れても重なっても 1 回です。
  - AdMob のリワードの設定「1 / Reward」は使いません(数も種類も読まない)。中身はゲーム側の仕様(MILE 2 倍・ガチャ 1 回)です。
- 広告の間は、音を止めて画面の入力を止めます。閉じた時に戻し、押しっぱなしの指は離すまで無視します。
- 読み込みに失敗した時は「広告を準備中です」で押せず、少し後に読み直します(10 秒から最大 5 分まで間隔を延ばす)。

### iOS だけの違い
- iOS では、広告はゲームの上に重ねて表示され、Unity は止まりません(Android は止まる)。
- 広告を押して App Store / Safari へ行き、戻った時に、Android と同じく「4 秒で通知が無ければ終わり」と扱うと、広告がまだ出ているのに音と入力が戻ってしまいます。
  - iOS では、戻った時から見張り(180 秒)を数え直すだけにしました。
- 広告なしパス: iOS には購入の仕組みが無いので、設定画面にパスの欄を出しません(広告のプライバシーの行は必要な地域だけ出ます)。ログイン特典も iOS には付きません。

## 同意(UMP)と ATT
- UMP(同意の画面)は Android と同じ処理で、iOS でも起動ごとに確かめます。同意で広告を要求できる時だけ、広告の SDK を始めます。
  - 欧州などで必要な時は、設定に「広告のプライバシー」が出ます。
  - **AdMob の「プライバシーとメッセージ」で、iOS アプリ用の GDPR のメッセージを作って公開してください**(マスター)。無いと、欧州など同意が必要な地域で同意の画面が出ず、広告が正しく出ないことがあります。
- ATT(トラッキングの許可のダイアログ)は使いません。
  - 理由: ゲームは IDFA を使った追跡をしていません。広告は IDFA なしでも出ます(パーソナライズの度合いは下がります)。許可のダイアログは、使う理由が無いのに出すべきではありません。
  - Info.plist に `NSUserTrackingUsageDescription` は入れていません(`GoogleMobileAdsSettings` の欄も空)。
  - **AdMob の「プライバシーとメッセージ」で「IDFA の説明メッセージ」は作らないでください。** 作る時は、先に ATT の対応(Info.plist の文言)が必要です。
- SKAdNetwork の一覧は、Google Mobile Ads のプラグインが Info.plist へ自動で入れます。
- App Store Connect の「App のプライバシー」は、AdMob が集めるデータ(識別子・使用状況・診断・おおよその位置など)に合わせて申告が必要です(マスター)。

## 正式公開(App Store)までに残っていること
1. AdMob の iOS アプリを App Store のアプリと結び付ける(公開後に AdMob の画面で)。それまで本番の広告は出にくい。
2. AdMob の iOS 用の GDPR メッセージの作成・公開。
3. App Store Connect の「App のプライバシー」の申告。トラッキングは「しない」(ATT を使わないため)。
4. App Store 用のビルドだけ `OMM_ADS_PROD` を足す(上の手順)。
5. `app-ads.txt`(開発者のウェブサイトに置く。AdMob の推奨。Android と共通の課題)。
6. 設定の「ライセンス」の表示: 今は Android 用の一覧が iOS にも出ます。また `Google Mobile Ads Unity Plugin` の行は「Android, Windows」と書かれています。iOS の正式公開の前に、iOS に入る物に合わせて見直します。
7. iOS の購入(広告なしパス)は対象外のまま。iOS で売る時は、App Store Connect の商品と、iOS 用の検証の仕組みを別に作ります。

## ローカルの確認(2026-11-22、この PC)
| 確認 | 結果 |
|---|---|
| iOS の Xcode プロジェクトの書き出し(`IosVersionCheck`) | 成功・エラー 0 |
| Info.plist | Version `1.0.0` / Build `7` / `GADApplicationIdentifier` = `ca-app-pub-2863190967263764~1910097904` / SKAdNetwork の一覧あり / `NSUserTrackingUsageDescription` なし |
| Bundle ID | `com.zero0084.action2nd` |
| Podfile | `Google-Mobile-Ads-SDK ~> 13.9`、`GoogleUserMessagingPlatform 3.1.0` |
| iOS のビルドの広告 | `[IosAds] adsProfile=test units=ios-test`(両方とも Google の iOS 用テストユニット)。iOS の定義は空 |
| 広告のコード | iOS の出力に `AdMobAdService` が入っている |
| ビルドの後 | 共通の版(bundleVersion)と iOS のビルド番号が元へ戻り、Android の versionCode(458)・Bundle ID は不変。ProjectSettings.asset は書き出しの前後で同一 |
| Android・Windows | コンパイル(Android 製品版・開発版、Windows 製品版)合格。Windows 開発版の広告・パスの模擬試験(-qaMonet)合格、Android 用テストユニットのまま(`units=android-test`) |

iPhone の実機での広告の表示・報酬は、まだ確かめていません(未確認)。

## TestFlight の実機で確かめること(マスター)
1. 起動して、しばらく後(初回は同意の画面が出る地域もある)、ホームの「広告で1回引く」を押す → 「Test Ad」と表示された広告 → 最後まで見て閉じる → ガチャが 1 回引ける。残りの回数が 1 減る。
2. 広告ガチャで、広告を途中で閉じる(最後まで見ない)→ ガチャは引けない。回数も減らない。
3. ランに成功する → 結果画面の「MILE 2倍」→ 最後まで見る → MILE が 2 倍(1 回だけ)。
4. 起動から 5 分以上たってから、ランで倒れる(FAILED)→ 結果画面の後に「Test Ad」のインタースティシャルが出る → 閉じると結果画面に戻り、音と操作が戻る。
5. 広告の表示中に広告を押して App Store / Safari へ行き、アプリへ戻る → 広告がまだ出ていて、閉じるまでゲームの音が鳴らない。閉じた後は普通に戻る。
6. 機内モードで「広告で1回引く」→「広告を準備中です」で押せない(固まらない)。通信を戻すと、少し後に引けるようになる。
7. 設定 → 広告なしパスの欄が出ない(欧州などでは「広告のプライバシー」の行だけ出る)。
8. 本物の広告(「Test Ad」の表示が無い広告)が出たら、すぐに知らせてください(テスト用の設定が効いていない)。
