# Google Play 配信用 AAB(2026-11-15)

## 基準
- 実機の確認が済んだ確認用 APK 454 と同じ中身です(ガチャまわりの配置 456de2f0 まで)。
- 違いは次の4つだけです。
  - 広告: 本番の広告ユニット(OMM_ADS_PROD は、このビルドの間だけ足します)。
  - 署名: Play で使っているアップロード鍵。
  - お知らせの版: 配信の番号(455)。
  - ビルドの確認: 本番用の番号のずれと失敗を止める確認を足しました。

## 作り方(マスターの PC で。パスワードは画面で入力)
1. Unity のエディターを閉じます。
2. PowerShell で、`C:\GameProject\2ndAction` から次を実行します。
   ```
   powershell -ExecutionPolicy Bypass -File Tools\ads\build_closedtest.ps1 -Keystore "C:\GameProject\OneMoreMileKeys\onemoremile-upload.jks" -Alias upload -Production
   ```
3. キーストアと鍵のパスワードを入力します。鍵を開けることを確かめてからビルドします(30〜40分)。
4. 最後に自動の確認(verify_closedtest.ps1)が出ます。「すべて OK」と「後片付け: すべて完了」を確かめてください。

- 出力先は `Unity\2ndAction\Builds\Production\` です。
  - `OneMoreMile_v455.aab`: Play へ上げる物。
  - `OneMoreMile_v455.apk`: 同じ中身の APK。本番広告なので、広告を何度も出す確認には使いません。
  - `OneMoreMile_v455_build.txt`: 使った設定の記録(パスワードは書きません)。
- パスワードは、この1回のビルドにだけ渡します。ログ・Unity のビルドの計画・Gradle の作業ファイルからは、後片付けで消します(今までのクローズドテスト用と同じ仕組み)。

## versionCode
- 今回は **455** です(ProjectSettings の 454 + 1)。
- 手元の記録で、アップロード鍵で作った AAB の一番大きい番号は 450(11/08、ct450)です。451〜454 は、デバッグ鍵の確認用 APK だけです。
- **Play Console へ実際に上げた一番大きい番号は、アイリーンには確認できません**(Play Console を開けるアカウントが無い)。455 は手元の記録のどれよりも大きい番号です。
- お知らせの版(PatchNotesUi)も 455 です。
  - 本番用のビルドは、お知らせの版と今回の versionCode が違うと止まります。
  - ビルドが失敗した時は番号を進めないので、作り直しても 455 のままです。

## 確認したこと(アイリーン、パスワードなしでできる範囲)
- 本番設定の点検(ProductionConfigAudit): 結果は下に書きます。
- 使い捨ての鍵での通し: build_closedtest.ps1 -Production を、パスワードの入力だけ差し替えて最後まで通しました。結果は下に書きます。

## 確認の結果(2026-11-15 夜)
- 本番設定の点検(ProductionConfigAudit)の結果:
  - Android の項目はすべて OK です。
    - 製品版・本番広告の版で、本番広告が有効になります(UseProductionAds=True)。
    - 開発版の型 33 個は、製品版に入りません。
    - UGS は、製品版では必ず production で、起動引数でも変えられません。
    - データ削除の案内は production の用意ができています。
    - 撮影用・確認用の印はありません。
  - NG は「iOS の AdMob アプリ ID がテスト用」の1件だけです。iOS 公開前の項目で、Android には関係しません。
- 使い捨ての鍵での通し(build_closedtest.ps1 -Production。パスワードの入力だけ差し替え)は、すべて OK でした。
  - 鍵の確認 → ビルド(終了コード 0)→ 後片付けを通しました。
    - Gradle・ビルドの計画・翻訳済みスクリプトのパスワードを消しました。
    - ProjectSettings の鍵の場所は空のままです。
  - 自動の確認(verify_closedtest.ps1)の結果:
    - AAB/APK の署名と、証明書がその鍵と一致することを確かめました。
    - versionCode 455、Target API 36、debuggable なしです。
    - AdMob の本番アプリ ID が入っていて、テスト用 ID はありません。
    - 本番広告の版で、広告ユニットは本番の ID です。
    - パスの購入欄は出しません。開発版ではありません。
  - 大きさは AAB 487.9 MB、APK 488.9 MB です。AAB は Split Application Binary を ON で作ります(ct450 と同じ作り。今回 base の大きさは測っていません)。
  - 試しの成果物(使い捨ての鍵で署名)と使い捨ての鍵は消しました。ProjectSettings は versionCode 454 に戻したので、マスターのビルドが 455 になります。
