# 公開前の案内・表記の整備(2026-10-26)

ゲーム内の実装と確認まで。ストアへの提出・公開はしていない。

## 1. 追加したもの

| 項目 | 内容 | 場所 |
|---|---|---|
| 著作権表示 | タイトル(ホーム)の画面の下の中央に「© 2026 MIXGame」を控えめに(半透明の小さな字)。ランの中・ほかの画面を開いている間は出さない。設定の「このゲームについて」とクレジットの最後にも同じ表記 | `GameManager.cs`、`Settings/AppInfo.cs` |
| バージョン表示 | 設定 →「このゲームについて」→「バージョン」に、ビルドの情報から自動で出す(手で書かない): `Application.version`(ビルドの時に日時で付く)+ Android は端末の版番号(例「2026-10-26 2035 (382)」)。「コピー」ボタン付き | `Settings/AppInfo.cs`、`Settings/SettingsPanel.cs` |
| 本体のアイコン | 本体のアイコンは旧版のままだった(2026-10-23 の絵と画素が一致)→ ストアで採用した新しいアイコン(マントを一枚布にした版)に差し替えた。正方形のアイコンとアダプティブの背景の2枚。ファイル・設定はそのままで絵だけ | `Assets/Art/UI/AppIcon.png`、`AppIconAdaptive.png` |
| 設定の「このゲームについて」 | 既存の画面を使って、バージョン / 制作クレジット / ライセンス / お問い合わせ を1か所に。ライセンスの行は「データとプライバシー」から移した(同じ画面を開く。重複して作っていない) | `Settings/SettingsPanel.cs` |
| 制作クレジット | 既存のライセンスの画面で、エンドロールと同じ表(StaffCreditsData)を読み物として表示。日本語の時は役職の説明を添える | `Settings/LicensesUi.cs` |
| お問い合わせ | 「メールを送る」でメールアプリを開く。件名「[OneMoreMile] お問い合わせ」、本文に「ゲームのバージョン / 利用環境(端末・OS)/ お問い合わせ内容」の欄(バージョンと環境は自動で入る。日本語と英語を並べて書く)。開かない時のためにアドレスを表示+コピー | `Settings/AppInfo.cs` |
| 第三者ライセンス | 出どころ調査と実際のビルドの中身に照らして追加(下の2章)。両方のビルドの分と Android だけの分を分け、Android の時だけ後者も表示。約50万字を最後まで読めるよう、画面は見えている部分だけを描く形に変えた | `Resources/Licenses/ThirdPartyNotices.txt`、`ThirdPartyNotices_Android.txt` |
| データ削除の案内 | 削除の仕組みは development でしか配置・確認していない(production は未配置)ので、production につながる製品版では「アプリの中からの削除は準備中です。プレイヤーIDを書いてメールでご依頼ください」と案内し、削除のボタンを出さない(公開中のポリシーも「メールで依頼」)。開発版(development)は今までどおり | `Online/AccountDeletion.cs`、`Settings/DataDeletionUi.cs` |
| 翻訳 | 新しい文 13件(ID 10135〜10147)をエステルが 29言語へ訳し、30言語へ反映 | `Tools/loc/keys_extra27.tsv`、`Tools/loc/est27/` |

## 2. 第三者ライセンス(調べた結果: `license_audit.md`)

実際のビルド(Android の APK・Windows の製品版)と、Android の依存の一覧(Gradle、90個)から調べた。

**両方のビルドに入っていて、表記が必要なもの**
- Newtonsoft.Json(MIT)
- websocket-sharp(MIT、Unity のサービスの中)
- unity-websocket-webgl(Apache-2.0)
- Google Mobile Ads Unity Plugin 11.5.0(Apache-2.0。Windows にも DLL が入る)
- 念のため: musl(MIT、Burst の生成物)、Unity Companion License の8パッケージ

**Android にだけ入っていて、表記が必要なもの**
- Google Play services / UMP / Billing などの中に入っているオープンソースの表記(18個のライブラリの同梱の文、Google は「表示は開発者の責任」としている)
- AndroidX、Android Game Development Kit、Kotlin、coroutines、Guava(Apache-2.0)、checker-qual(MIT)、jsr305

**載せなくてよいもの**: Google の SDK の規約本体・Unity のサービスの規約、エディター専用のパッケージなど(理由は `license_audit.md` 4章)。フォントの同梱は無い(Unity 内蔵の字 / 端末の字)。

本文はすべて元のファイルから写した(言い換えなし)。MS Basic の部分は今までどおり原文のまま。

## 3. 既存のままでよかったもの

- **プライバシーポリシー**: 設定 →「データとプライバシー」からブラウザで開く(開けない時の URL コピーあり)。公開中のページ(2026-10-18 更新)の内容は、広告(AdMob・UMP)・課金(Google Play Billing、カード情報を受け取らない)・オンライン(Unity の匿名 ID、Relay の暗号化、ランキングは任意参加・Cloud Code / Cloud Save / Leaderboards)・端末内の保存・時刻の確認・LOCAL・13歳以上・削除はメールで依頼、で、今の実装と合っていた。**推測で書かれた収集データや、使えない削除機能の案内は無かった**。
- 問い合わせ先のアドレスはマスター確認済み(データ削除の画面にも今までどおり)。
- 既存の制作名義(エンドロール「CREATED BY マジル旅人」、起動ロゴ「MIXGame by マジル旅人」)と「© 2026 MIXGame」は食い違わない(MIXGame はマジル旅人さんの名義)。変更はしていない。
- 開発版のホームの「build …」の表示(製品版にも出ている)はそのまま。

## 4. 確認

| 確認 | 結果 |
|---|---|
| Windows 横 1280x720(日本語/英語) | 設定の「このゲームについて」、クレジット(上端・最後)、ライセンス(上端・最後)、戻るで閉じて設定に戻る、問い合わせ(本当には開かない試験)、© の文字。失敗 0 |
| Windows 縦 608x1080 | 同上、見切れなし。失敗 0 |
| Android の表示(Windows で Android の設定を再現) | Android だけの表記が出て、最後の項目(JSR 305)まで読める。失敗 0 |
| Android エミュレーター 横/縦(開発版 385、試験用の保存) | 同じ確認を本物の Android で。バージョン「2026-10-26 2125 (385)」(版番号まで表示)、Android の表記を含む約50万字を最後まで表示、戻るで閉じる。失敗 0。横画面の2列で版番号が切れていたのを直した(入らない時は字を小さく) |
| 製品版 384(エミュレーター) | ホームの「© 2026 MIXGame」、設定の「このゲームについて」(バージョン「2026-10-26 2111 (384)」)、production 接続なのでデータ削除は「メールでご依頼いただけます」の案内 |
| 本物の保存 | 試験はすべて別の名前の試験用ビルドで、本物の保存は前後で同一 |

## 5. マスターの対応が必要なもの

1. **アイコンの実機確認**: 端末のランチャーでの丸・角丸の切り抜き、明るい/暗い壁紙での見え方(エステルの指摘)。
2. **ライセンスの判断**(`license_audit.md` 5章): Unity 本体の部分・Windows の D3D12Core.dll など・Android の libc++_shared.so に表記が要るか(手元の資料では確認できなかった)、Google 推奨の oss-licenses-plugin を使うか、「念のため」の項目を残すか。
3. **エンドロールの文字(Palatino Linotype を画像にした物)**: Windows のフォントを画像にして配ってよいかは未確認(前回からの残り)。自由に使えるフォントで作り直す案もある。
4. **データ削除**: production に削除の仕組みを配置して確かめるまで、製品版はメールでの依頼を案内する(この後の作業「development でのデータ削除の確認」の後に、production の扱いを決めてください)。
5. プライバシーポリシーの更新は今は不要。ただし本番広告・購入を有効にする時に、もう一度照らし合わせる。

## 6. ビルド

| 種類 | ファイル | 版 |
|---|---|---|
| クローズドテスト用 製品版(確認用 APK、テスト広告、デバッグ署名) | `Unity/2ndAction/Builds/ClosedTest/OneMoreMile_ct384_debugsigned.apk`(+ `_build.txt`) | 384 / 2026-10-26 2111 |
| 開発版 APK(確認の画面の自動試験用) | `Unity/2ndAction/Builds/Android/2ndAction.apk` | 385 |
| Windows 開発版(試験用の別名) | `Unity/2ndAction/Builds/WindowsDevSandbox/` | — |

確認用の画面の撮影: Windows `scratchpad`(qa_info_land2 / qa_info_port2 / qa_info_andcaps)、Android(infoand_land / infoand_port)の一覧は `Docs/Licenses_2026-10-26/shots/`。
