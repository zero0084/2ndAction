# ストア素材の更新(全12人の布の見直しの正式採用に合わせて、2026-10-26)

旧い絵(布が破れ・ほつれの描き方)が写っている素材を洗い出し、正式採用した絵で作り直した。
構成・字幕・BGM・ロゴ・紹介文・並び順は、今までのエステル採用版をそのまま流用し、**場面だけ** を新しい撮影から選び直した。
撮影は、採用した絵の入った撮影用ビルド(透かし・開発用の表示なし、ゲームの中身は製品と同じ)で、日本語/英語の UI をそれぞれ撮った。

## 1. 更新した物の一覧

| 種類 | ファイル | 内容 | プレビュー |
|---|---|---|---|
| アプリのアイコン(ストア用) | `Docs/AppIcon_2026-10-26/store_icon_512.png`、`icon_square_1254.png`、`icon_adaptive_background_1254.png` | 黒剣士の赤いマントを一枚布に(構図・城・月・光の道は元の画素) | `Docs/AppIcon_2026-10-26/preview_icon_sizes_old_new.png`(48/96/192px、丸・角丸の切り抜き)、`preview_icon_old_new.jpg`、`preview_adaptive_old_new.jpg` |
| Google Play フィーチャー画像 | `Docs/AppIcon_2026-10-26/store_feature_1024x500.png`、`store_feature_2048x1000.png` | 同上 | `Docs/AppIcon_2026-10-26/preview_feature_old_new.jpg` |
| Steam 紹介動画(第1トレーラー) | `Docs/Steam_2026-10-23/trailer/omm_trailer_v7_{ja,en}_1080p60.mp4`(68.0秒 / 64.5秒、git の外) | v6 と同じ構成・字幕・BGM。22カットすべて新しい撮影から | `Docs/Steam_2026-10-23/review_for_estelle/v7/trailer_v6_v7_{ja,en}.jpg`(上 v6 / 下 v7、2秒ごと) |
| Steam 設定紹介(第2トレーラー) | `Docs/Steam_2026-10-23/trailer_settings/omm_settings_s3_{ja,en}_1080p60.mp4`(34.4秒、git の外) | s2 と同じ構成・字幕。8カットすべて新しい撮影から | `Docs/Steam_2026-10-23/review_for_estelle/v7/settings_s2_s3_ja.jpg` |
| Steam スクリーンショット | `Docs/Steam_2026-10-23/screenshots/{ja,en}/omm_ss_01〜10.png` + `_extra/` 2枚 | 同じ場面の種類で選び直し(1920x1080) | `review_for_estelle/v7/steam_screenshots_{ja,en}.jpg` |
| Steam 紹介画像(システム紹介) | `Docs/Steam_2026-10-23/about/out/omm_about_01〜05_{ja,en}(_616).png` | 文字・配置はそのまま、中の画面だけ新しいスクリーンショットから | `Docs/Steam_2026-10-23/about/preview_about_{ja,en}_2026-10-26.jpg` |
| Steam 紹介画像の短いアニメ | `Docs/Steam_2026-10-23/about/anim/omm_anim_*.{gif,webm}`(4種、git の外) | 同じ長さ・大きさで新しい撮影から(旧は `about/anim_old_2026-10-23/`) | — |
| Google Play スクリーンショット | `GooglePlay/StoreScreenshots/landscape/{ja,en}/` 7枚ずつ、`landscape/ja/_alt/` 5枚、`portrait/ja/` 3枚 | 同じ場面の種類・並び順(01〜06, 08)。キャラ選択・縦画面も撮り直し | `GooglePlay/StoreScreenshots/contact_sheet_*.jpg` |

旧い版は git の履歴に残っている(上書き前のコミット 6ecc19a9 まで)。

## 2. 変えていない物(理由)

- **Google Play `_review/07_Multiplayer_ja.png`**: もともと「UI の重なりを直すまで正式採用しない」保留の画像。マルチの撮影は2つのプロセスが要るため今回は撮り直していない(旧い絵のまま、使わない)。
- **Steam スクリーンショットの予備 `omm_ss_alt_escape_guide.png`**(と紹介画像04の2コマ目): 「脱出できるようになりました」の説明の板。今回の撮影では説明が先に自動で閉じられて撮れなかったため旧い撮影のまま。説明の板が主役でキャラは小さく映るだけ。**ただし文言に注意(4. 未確認)**。
- **アプリの中のアイコン**(`Assets/Art/UI/AppIcon.png` / `AppIconAdaptive.png`): ストア用は作ったが、ゲーム本体の差し替えはマスターの確認待ち(エステルの提案どおり)。
- 文字(字幕・紹介文・ストアの説明文)、BGM、ロゴ、構成。

## 3. 無かった物(作っていない)

- **Steam のカプセル画像(ヘッダー/小・中・大カプセル/ライブラリ用)・バナー**: リポジトリに今まで作った物が無い。作る場合はアイコンのキーアート(新しいマント)を元にエステルと相談して作れる。
- **App Store 用の素材**(スクリーンショット・アイコン・プレビュー動画): まだ一度も作っていない(iOS 版の準備をする時に)。

## 4. 未確認 / 注意

- **脱出の説明の文言**: 旧い撮影は「画面を3秒長押し」(タッチの文言)。10-25 の操作の整理で、PC ではマウス/キーの割り当てに合わせた文言(例「マウスの左ボタンを3秒長押し」)が出る。**Google Play 用は今のままで正しいが、Steam 用(紹介画像04・予備画像)は PC の文言で撮り直すのがよい**(エステルも「公開前に説明板の文言が今の仕様と一致しているか確認」を指摘)。
- 設定紹介の動画: 設定の画面が 10-25 に PC 用の並び(表示モード・解像度・キー設定)へ変わり、PC の撮影では「画面の向き/縦画面のラン表示」の行が出ない。表示の場面は PC の並びで撮り直した(画面揺れ・警告・発光が見える所)。縦画面の場面は Android の設定画面で撮った(`-caps android`。縦画面は Android の機能のため)。
- 動画の実際の再生(テンポ・音の同期)は数値と2秒ごとの静止画で確認した。エステルの指摘: 16〜18秒のカード選択、48〜50秒の暗い洞窟は実再生でテンポを確認したい。
- 音: v7 日本語 -14.1 LUFS / 真のピーク -1.6 dBTP、英語 -14.1 / -1.5、s3 日本語 -14.3 / -1.5、英語 -14.2 / -1.5(どれも -1 dBTP 以下)。
- ストアへの登録・公開はしていない(マスターが行う)。

## 5. エステルのレビュー

- アイコン/アダプティブ/フィーチャー: 3種とも **採用**(48px でも「赤いマントの黒剣士が青い光の道を走る」が読める。マントと鎧の区別は旧より良い)。
- 紹介動画 v7・Steam スクリーンショット・紹介画像: **採用**。Google Play 7枚: **条件付き採用**(スマホの縮小表示で 01 のキャラが小さい点の最終確認。並び順 01→02→03→04 は変えない)。
- 必須の撮り直しは無し。設定紹介(s3)は撮り直し後に別途確認予定。

## 6. 作り方(作り直す時)

- 撮影: 撮影用ビルド `BuildStoreCapture.Build` → `-storeCapture <dir> -scShots run_day,run_wasteland,dual,gunslinger,aerial,crowd,cards,cave,sky,boss,boss_cave,boss_sky,result [-scLang en]`、設定は `set_auto,set_display,set_volume`、縦画面は 608x1080 の窓で `-caps android -scShots set_portrait`、Google Play の縦は `-scSuper 2`
- 編集データ: `trailer/edl_v7_{ja,en}.json`、`trailer_settings/edl_s3_{ja,en}.json`(v6/s2 の素材名を置き換え、各カットの開始秒を選び直した)
- 道具(新規): `Tools/steam/shotgrid.py`(ショットを時刻つきで並べる)、`recut.py`(旧/新のカットを並べて比べる)、`sscand.py` + `ssexport.py`(スクショの候補と書き出し、選んだ秒は `screenshots/{ja,en}_select_2026-10-26.json`)、`anims.sh`(紹介画像のアニメ)、`gp_rebuild.py`(Google Play の作り直し、差し替え表 `GooglePlay/StoreScreenshots/replace_2026-10-26.json`)
- 撮影素材の保管: `C:/GameProject/SteamCaptureArchive/2026-10-26/masters/`(H.264 4:4:4 crf10 + wav)
