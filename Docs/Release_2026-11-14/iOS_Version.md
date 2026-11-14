# iOS の版(Version)の直し(2026-11-15)

## 起きたこと
- Unity Build Automation の初回 iOS ビルド「Default iOS / #1」が、Postprocess built player で止まりました。
  - エラー: `UnityException: iOS Version has not been set up correctly ... '2026-11-06 2229' contains invalid characters.`
- 原因: Unity の「Version」(PlayerSettings.bundleVersion)は、Android・Windows・iOS で共通の1つの値です。
  - Android は、ビルドのたびに `BuildAndroid` が日付の版(例 `2026-11-15 0117`)を書いています(Android の versionName)。
  - リポジトリの ProjectSettings に入っている値は `2026-11-06 2229` でした。
  - iOS の版(CFBundleShortVersionString)は、数字と点だけ・3つまでの決まりがあります。日付の形はこれに合わず、クラウドの iOS ビルドで止まりました。
- 手元の iOS 用の `BuildIOS.Build`(Xcode のプロジェクトを書き出すメニュー)には、日付の数字の版(`yyyyMMdd.HHmm`)を書く処理がありました。ただ、クラウドビルドはこれを通りません。

## 直したこと
- `Assets/Editor/IosVersion.cs`(新規)
  - iOS をビルドする時だけ、ビルドの直前に版を `IosVersion.MarketingVersion`(**1.0.0**)へ差し替えます。ビルドの後は元の値へ戻します。
  - クラウドビルドでも手元でも、ビルドの前の処理(IPreprocessBuildWithReport)として必ず通ります。
  - ProjectSettings に保存する値(Android・Windows が使う版)は書き換えません。Android・Windows のビルドでは何もしません。
  - ビルド番号(CFBundleVersion = PlayerSettings.iOS.buildNumber)・証明書・Bundle ID・署名・Unity/Xcode の版には触れません。
  - 版を上げる時(例 1.0.0 → 1.0.1)は、このファイルの `MarketingVersion` を変えます。形が正しくない時は、ビルドを止めて理由を出します。
- `Assets/Editor/BuildIOS.cs`: 日付の版を書く処理をやめ、版は上の仕組みに任せました(ビルド番号を1つ進める今の処理はそのまま)。
- `Assets/Editor/IosVersionCheck.cs`(新規、手元の確認用): iOS の Xcode プロジェクトを書き出すだけの確認です。
  - ビルド番号は変えません。
  - 書き出した Info.plist の版が 1.0.0 で、ビルドの後に元の版へ戻ったことを確かめます。

## 次回の iOS ビルドで使われる値
- Version(CFBundleShortVersionString): **1.0.0**
- Build(CFBundleVersion): リポジトリの ProjectSettings の iOS のビルド番号(今は **4**)。クラウドの設定でビルド番号を自動で付けている場合は、その値になります(今回は変えていません)。

## 確認
(結果は下に追記)
### 手元での確認(2026-11-15、Windows の Unity 6000.5.6f1、iOS の Xcode プロジェクトの書き出しまで)
- 実行: `Unity.exe -batchmode -buildTarget iOS -executeMethod IosVersionCheck.Run`
- 確かめる前の版は `2026-11-15 0117`(iOS では使えない形。クラウドで止まった時と同じ状態)です。
- 結果は、ビルド成功・Postprocess built player も通過でした(前回はここで版の例外で止まった)。
  - 書き出した Info.plist: CFBundleShortVersionString = **1.0.0**、CFBundleVersion = **4**
  - ビルドの後、版は `2026-11-15 0117` に戻りました。iOS のビルド番号も 4 のまま、Bundle ID・署名の設定は変わっていません。
- Mac での compile・署名・App Store への提出は、手元ではしていません(Windows のため)。

### 手元で見つかった別のエラー(版とは別の件。直していません)
- Google の広告の部品(Google Mobile Ads)の iOS 用の処理が、「iOS Google Mobile Ads app ID is empty」というエラーを1件出しました(GoogleMobileAdsSettings の iOS のアプリ ID が空)。
- 手元ではビルドは「成功」のまま最後まで進みました。ただ、クラウドビルドではこれが次の停止原因になる可能性があります。
- iOS 用の AdMob アプリ ID は、AdMob の管理画面で iOS のアプリを作ると発行される値です。マスターの判断が要ります。
  - (a) AdMob で iOS アプリを作って ID を入れる。
  - (b) iOS 版で広告をまだ使わないなら、Google 公式のテスト用のアプリ ID を入れておく(今の iOS のビルドは広告のコードを使っていない = 定義 OMM_ADMOB は Android だけ)。
  - (c) iOS の時だけ、この確認を止める設定にする。

### クラウドでの確認
- まだしていません(有料枠のため、マスターが実行)。
