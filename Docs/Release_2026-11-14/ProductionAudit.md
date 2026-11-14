# 本番設定の点検の結果(2026-11-14、ProductionConfigAudit の出力 + APK の中身)

| 区分 | 項目 | 結果 | 内容 |
|---|---|---|---|
| ビルド設定 | パッケージ名 | OK | com.zero0084.action2nd |
| ビルド設定 | 版(versionCode / versionName) | INFO | 451 / 2026-11-14 1914(本番用のビルドで +1 される) |
| ビルド設定 | 最小 / 対象 SDK | OK | min 26 / target 36(Play の新規アプリは 35 以上) |
| ビルド設定 | スクリプトの方式 | OK | IL2CPP |
| ビルド設定 | 64bit(ARM64) | OK | ARM64 |
| ビルド設定 | 署名(PlayerSettings) | OK | useCustomKeystore=False(設定には鍵を保存しない。本番用のビルドの間だけ環境変数 OMM_UPLOAD_KEYSTORE 等から入れて、終わったら戻す) |
| ビルド設定 | アップロード鍵(この起動の環境) | INFO | 未指定(本番用のビルドはマスターが鍵を指定して実行。指定が無いと BuildProduction は止まる) |
| ビルド設定 | Android の定義 | OK | OMM_ADMOB;OMM_UNITY_IAP(本番広告 OMM_ADS_PROD は設定に書かず、本番用のビルドの間だけ足す) |
| ビルド設定 | Development Build(エディターの設定) | INFO | False(本番用/クローズドテスト用のビルドは BuildOptions.None で作るので、この設定は使わない) |
| ビルド設定 | シーン | OK | Main |
| ビルド設定 | インターネット権限 | OK | forceInternetPermission=True(ビルドの時に true にする) |
| 広告 | AdMob アプリ ID(本番・テスト広告の版も同じ) | OK | ca-app-pub-2863190967263764~2717237567 |
| 広告 | GoogleMobileAdsSettings のアプリ ID | OK | ca-app-pub-2863190967263764~2717237567 |
| 広告 | 本番の広告ユニット | OK | 強制 ca-app-pub-2863190967263764/5403818490 / 報酬 ca-app-pub-2863190967263764/3391276558 |
| 広告 | テストの広告ユニット | OK | Google 公式のテスト用ユニット |
| 課金 | レシートの検証(GooglePlayTangle) | INFO | 未設定 → 製品版では広告なしパスの購入欄を出さない(販売停止の状態)。既に持っている権利は保たれる(確認できない時は外さない) |
| ビルドの止め | 翻訳の反映待ち(LocPendingReview) | OK | 0 件 |
| ビルドの止め | プロフィールの訳の下書き | OK | 済み |
| コンパイル | Android 開発版 / 製品版(テスト広告)/ 製品版(本番広告) | OK | dev=ok release_testads=ok release_prodads=ok |
| 広告 | テスト広告の版: 本番広告の切り替え | OK | UseProductionAds=False |
| 広告 | 本番広告の版: 本番広告の切り替え | OK | UseProductionAds=True |
| 広告 | 開発版: 本番広告にならない | OK | 開発版に OMM_ADS_PROD を足しても UseProductionAds=false(#if OMM_ADS_PROD && !DEVELOPMENT_BUILD) |
| オンライン | UGS の環境(製品版) | OK | IsReleaseBuild=true → 必ず production(起動引数で変えられない) |
| オンライン | UGS の環境(開発版) | OK | 開発版は development(production は選べない) |
| オンライン | データ削除のアプリ内の案内(production) | OK | ProductionReady=True |
| 開発用 | 撮影用/確認用の版の印 | OK | CaptureBuild=false ReleaseProbeBuild=false(DevGate.On は製品版で Debug.isDebugBuild=false なので常に false) |
| 開発用 | 開発用の型が製品版に入っていない | OK | 開発版だけの型 33 個(QaSweep/DebugPanel/模擬の広告・課金/試験用の保存 等)は製品版に無い |
| 開発用 | 製品版に残る、名前に Debug/Qa/Mock/Test を含む型 | INFO | ColliderDebugView, DebugRun, GameFeelDebug, IBossBattleDebug, LocDebug, NetDebugUI(中身は Debug.isDebugBuild / DevGate.On で止まる物。下の一覧で確認) |
| 開発用 | 製品版に意図して残す診断 | INFO | FreezeDiagnostics: 処理落ち/位置の飛びを端末のファイル(hitch_log.txt、2MB で古い分へ回す)に記録するだけ。画面の表示は開発版の DEBUG の時だけ。送信しない |

NG 0 件 / WARN 0 件。読むだけの点検(ビルド・設定の書き換え・本番広告の表示はしていない)。

## 確認用 APK の中身(OneMoreMile_ct452_debugsigned.apk、aapt2 と unzip で確認)
- package com.zero0084.action2nd / versionCode 452 / target SDK 36 / native-code arm64-v8a だけ / debuggable なし
- AndroidManifest の com.google.android.gms.ads.APPLICATION_ID = ca-app-pub-2863190967263764~2717237567
- 権限: INTERNET / ACCESS_NETWORK_STATE / Wi-Fi(LAN マルチ)/ AD_ID と ADSERVICES(AdMob)/ BILLING(Play の課金)/ WAKE_LOCK / FOREGROUND_SERVICE
- IL2CPP のメタデータに、開発用の型の名前(QaSweep / MockAdService / MockStoreService / MemorySaveStore / FileSaveStore / QaArgs / EndgameDebug)は無い
- テストの広告ユニット(ca-app-pub-3940256099942544/…)の文字列は入っている(定数。テスト広告の版で使う。本番広告の版は本番の ID を使う = 上のコンパイルの確認)

## エミュレーターで 452 を動かして確認(Android 16、テスト広告)
- 起動ログ: Monetization mode=Store ads=admob store=unity-iap adsProfile=test / UGS Environment = production (release build, fixed)
- 更新の確認: notplay(Play 以外から入れた版なので確かめない)
- ホーム・設定: DEBUG の項目なし、広告なしパスの欄なし(販売停止の状態)
- ホームの下の「build 日時」の表示が製品版でも出ていた → 開発版だけにした(452 の後の修正。次の APK から)

## ソースの確認(Tools/release/active_scan.py: 製品版で有効になるコードだけを読む)
- 起動引数を読む所は、製品版では -netEnemyDelay / -netChoiceInvincible の2つだけだった → 開発版だけにした(PC 版で引数を渡されても効かない)
- F1〜F12 のボスの出現・無敵のキーはエディターの中だけ。闘技場の無敵は練習用の正式な機能(記録に残らない)
