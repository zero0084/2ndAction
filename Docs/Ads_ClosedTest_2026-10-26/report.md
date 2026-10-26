# クローズドテスト用の広告の組み込みと確認(2026-10-26)

方針: クローズドテストでは、全テスターに **テスト広告**(実際の AdMob アプリ ID + Google 公式のテスト用広告ユニット)を表示する。
本番広告への切り替え・本番商品の有効化・ストアへの公開は、していない。

## 1. 今回変更した内容

| 項目 | 内容 | ファイル |
|---|---|---|
| 広告の設定を2つに分けた | 既定 = **テスト広告**(Google のテスト用ユニット)。本番広告は Android の定義に `OMM_ADS_PROD` を足した製品版だけで使う(開発版は常にテスト広告)。いただいた本番の広告ユニット ID は、将来の切り替え用の値として保存しただけで、今のビルドでは使っていない | `Scripts/Monetization/MonetizationServices.cs`(`MonetizationConfig`) |
| AdMob アプリ ID | テスト用のサンプル ID → 実際のアプリ ID `ca-app-pub-2863190967263764~2717237567`(テスト広告の版もこの ID) | `Editor/MonetizationSetup.cs`、`GoogleMobileAds/Resources/GoogleMobileAdsSettings.asset` |
| ReleaseBuildGuard | 単純に無効にはせず、「テスト広告の版」を正しく扱うように変更: アプリ ID は必須。本番広告の版(`OMM_ADS_PROD`)だけ本番の広告ユニット ID と課金の検証(GooglePlayTangle)を必須にする。テスト広告の版は課金の検証が無くても作れる(パスは「購入の準備中」のまま)。ビルドの記録に広告の設定(TEST ADS / PRODUCTION ADS)を出す | `Editor/ReleaseBuildGuard.cs` |
| クローズドテスト用のビルド | `BuildAndroid.BuildClosedTest`: 製品版(Development Build / debuggable なし)、テスト広告。アップロード鍵を渡した時だけ AAB(Split あり)と APK を同じ鍵で作り、渡さない時はデバッグ署名の確認用 APK だけ。署名の設定はビルド後に必ず元へ戻す(鍵の場所・パスワードを ProjectSettings に残さない)。使った設定を `_build.txt` に記録(パスワードは書かない)。同じ起動の中でアプリ ID を書き換えた直後は古い ID が入ったため、その時はビルドを止めて再実行を求める | `Editor/BuildAndroid.cs` |
| 署名のためのスクリプト | マスターが自分の PC で実行し、鍵のパスワードを画面で入力する(この1回の Unity のビルドにだけ渡す。チャット・ファイル・ログに残らない) | `Tools/ads/build_closedtest.ps1` |
| 読み込みの再試行 | 約2秒ごとに要求し続けていた → 用意できない間は 10秒 → 20 → 40 → … 最大 5分 と間隔を延ばす。用意できたら元へ。広告が無くてもゲームは止まらない(今までどおり) | `Scripts/Monetization/AdManager.cs` |
| 同意(UMP) | 同意で要求できない間は広告を要求しない。開発版だけ、欧州の同意画面を試す起動引数(`-umpGeo eea` / `-umpTestDevice <ID>` / `-umpReset 1`)。製品版には入らない | `Scripts/Monetization/AdMobAdService.cs` |
| 確認の追加 | 本物の SDK の試験 `-qaAdSdk` を拡張(連打、途中で閉じる、外へ出て戻る、回転、1日3回、通信を切る/戻す)。外の操作は `Tools/ads/addrive.sh` | `Scripts/QaSweep.AdSdk.cs` |
| app-ads.txt | 設置する内容と、無料で置ける方法・手順 | `app-ads.txt手順.md` |

## 2. ビルド

| 種類 | ファイル | 版 | 広告の設定 | 署名 |
|---|---|---|---|---|
| クローズドテスト用 製品版(確認用 APK) | **`Unity/2ndAction/Builds/ClosedTest/OneMoreMile_ct384_debugsigned.apk`** + `OneMoreMile_ct384_build.txt`(380 の後の修正と著作権・案内の追加を含む最新) | versionCode 384 / 2026-10-26 2111 | **テスト広告**(`adsProfile=test`、Google のテスト用ユニット、アプリ ID は実際の ID) | デバッグ鍵(**Google Play には上げられない**) |
| 開発版 APK(試験用) | `Unity/2ndAction/Builds/Android/2ndAction.apk` | 381 | テスト広告 | デバッグ鍵 |
| **AAB(Play へ上げる物)** | **未作成** | — | — | **アップロード鍵が無いため**(4章) |

- APK の中を確認した: パッケージ `com.zero0084.action2nd`、AndroidManifest の AdMob アプリ ID = 実際の ID、製品版は debuggable なし(開発版は debuggable)。
- 378 は、アプリ ID の書き換えと同じ起動の中でビルドしたため、古い(サンプルの)アプリ ID が入っていた → 使わない。380 で直っている。

## 3. 確認した環境と結果

### Windows(模擬、開発版の試験用ビルド「One More Mile Test」、本物の保存は前後で同一)
| 試験 | 結果 |
|---|---|
| `-qaMonet -monetizationMock 1`(強制広告の条件、MILE 2倍、広告ガチャ、購入/復元の模擬、音・入力、テスト用データの分離 など全項目) | 失敗 0 |
| `-qaMonetNone`(Steam/PC: 広告も課金も始めない) | 失敗 0 |

### Android エミュレーター(本物の Google Mobile Ads SDK 11.5 + テスト広告、保存は試験用ファイル)
| 確認 | 結果 |
|---|---|
| 広告の設定 | `adsProfile=test`、ユニットは Google のテスト用。AdMob の初期化と同意の確認に成功 |
| MILE 2倍(結果画面のボタンを5回連打) | 広告は1回だけ。報酬の通知で +200 が **1回だけ**(200 → 400)。同じランでもう一度押しても増えない |
| 広告ガチャ | 3回とも 1回ずつカード +1・残り回数 −1。**4回目は広告を出さず、回数・カードも変わらない**(1日3回) |
| 広告の表示中にホームへ出て5秒後に戻る | 広告の続きが出て、閉じた後に報酬が1回。固まらない |
| 広告の表示中に画面を回す | 同上 |
| 強制広告(ゲームオーバー → ホーム) | 広告 → 閉じるとホーム。時間の速さ 1、操作できる |
| 広告の後の音・入力・時間 | 毎回、広告中は音停止・入力停止 → 閉じた後に戻る |
| 通信を切る(機内モード) | 読み込みの要求は 75秒で 6回(10 → 20 → 40 → 80秒と延びた)。MILE 2倍は「広告を表示できませんでした」で、MILE は変わらず操作も止まらない。ゲームオーバー → ホームは広告を待たずに 2.0秒 |
| 通信を戻す | 64秒後に報酬広告がまた用意できた |
| 欧州(EEA)の同意(開発版の検証用の設定で EEA 扱い、エミュレーターだけ) | **公開済みの同意メッセージが実際のアプリ ID で表示された**(「Welcome to One More Mile」、Consent / Do not consent / Manage options)。「Do not consent」→ 広告は要求可能(制限付き)、設定に「Ad Privacy → Privacy Settings」が出る → 押すと同意の画面がまた開き、同意へ変更できた |
| 製品版 380 | 起動 → テスト広告の設定・AdMob 初期化 → ホームの「広告で1回引く」で **「Test Ad」表示の報酬広告** → 報酬 → カード。パスの画面は「購入の準備中」。DEBUG ボタン・開発版の透かしなし |
| 自動試験(開発版 381、`-qaAdSdk`) | 上の各項目は通過。失敗 1件: 「MILE 2倍を受け取った後に、同じランでもう一度呼ぶ」と**もう1本広告が出た**(報酬は正しく無視され MILE は増えない。画面のボタンは受け取り後に無効なので、プレイヤーの操作では起きない)。関数の中でも受け取り済みなら広告を出さないように直した → 開発版 383 で再実行し **失敗 0**(全項目通過) |

**本物の SDK で確かめられなかった物**
- **報酬の前に閉じる**: Google のテスト用の報酬広告は約5秒で報酬が出るため、それより前に閉じる操作が間に合わなかった(戻るキーの時点で「Reward granted」)。報酬の前に閉じた時に何も付かないことは、模擬の試験(Windows)で確認済み。
- **在庫なし**: テスト用ユニットは常に在庫があるため、本物では再現できない。読み込み失敗(通信なし)で同じ「用意できていない」の流れを確認した。
- 欧州の同意は「検証用の設定で EEA 扱い」にしたエミュレーターでだけ確認(製品版にはこの設定は入らない)。

### Android 実機
未確認(マスターが確認)。

## 4. マスターに必要な作業

### A. アップロード鍵(Play へ上げる AAB に必須)
今あるのは開発用のデバッグ鍵(`~/.android/debug.keystore`)だけで、Play 用のアップロード鍵はありません。作り直しや置き換えはしていません。

1. マスターの PC で鍵を作ります(Unity に付いている keytool。パスワードは画面で入力し、チャットには書かないでください):
   ```
   "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Data\PlaybackEngines\AndroidPlayer\OpenJDK\bin\keytool.exe" -genkeypair -v -keystore D:\keys\onemoremile-upload.jks -alias upload -keyalg RSA -keysize 2048 -validity 10000
   ```
   - 保存場所はリポジトリの外(例 `D:\keys\`)。鍵のファイルとパスワードは別の場所にも控えてください(失うと更新を出せなくなります。Play アプリ署名を使えば鍵の再発行の申請はできます)。
2. Unity を閉じて、`powershell -ExecutionPolicy Bypass -File Tools\ads\build_closedtest.ps1 -Keystore "D:\keys\onemoremile-upload.jks" -Alias upload` を実行します。パスワードを2回聞かれます。
   - 出力: `Builds\ClosedTest\OneMoreMile_ct<番号>.aab`(Play へ上げる)、`.apk`(実機で直接確かめる)、`_build.txt`(広告の設定が test であることを確認できる)
3. Play Console で最初の AAB を上げる時に「Play アプリ署名」を有効にします(推奨。アプリの署名鍵は Google が管理し、マスターの鍵はアップロード専用になる)。

### B. 管理画面
- **Play Console**: アプリ(`com.zero0084.action2nd`)・クローズドテストのトラックとテスター・アプリのコンテンツ(広告あり / 広告 ID / データセーフティ / プライバシーポリシー URL)。
- **AdMob とストアの紐付け**(保留中): Play で公開(クローズドテストを含む)された後に、パッケージ名 `com.zero0084.action2nd`、URL `https://play.google.com/store/apps/details?id=com.zero0084.action2nd` のアプリを選んでください。検索で出た同名の別アプリは選ばないでください(パッケージ名で見分けられます)。
- **app-ads.txt**: `app-ads.txt手順.md` のとおり(内容は `google.com, pub-2863190967263764, DIRECT, f08c47fec0942fa0`。AdMob の画面に出る行と同じか確認してください)。
- AdMob の審査状況・本人確認は管理画面で確認してください。

### C. 判断していただきたいこと
- **オンライン機能の接続先**: 製品版(クローズドテストの版も)は UGS の **production** に接続する作りです(開発版は development)。クローズドテストのテスターのランキング記録などは production に入ります。データ削除の仕組みは development でしか確認していない(production には未配置)ので、クローズドテストの前に production へ配置するか、クローズドテストの版も development に接続するかを決めてください。
- 本番広告への切り替え(指示の後に `OMM_ADS_PROD` を付ける)・広告なしパスの有効化。

## 5. 実機で確認するための短いチェックリスト

1. 起動 → ホーム。設定の一番下などに開発用の表示が無い
2. ホームの「広告で1回引く」→ 「Test Ad」と書かれた広告 → 報酬の後に閉じる → カードが1枚、残り回数が1減る(1日3回、4回目は出ない)
3. ランを脱出成功で終える → 結果画面の「広告を見て MILE 2倍」を何度か押す → 広告は1回、MILE は1回分だけ増える
4. 広告の表示中にホームボタンで外へ出て戻る/画面を回す → 広告が続く・閉じた後に固まらない・音が戻る
5. 2回目以降のランで倒れる(起動から5分・前の広告から5分以上)→ 結果画面からホームへ戻る時に強制広告 → 閉じるとホーム。初回のランでは出ない
6. 機内モードで同じ操作 → 広告が出なくても止まらずに進む
7. 設定 →「広告なしパス」が「購入の準備中」
8. (EEA の端末・VPN がある場合のみ)同意の画面が出て、設定の「広告のプライバシー」から変更できる
