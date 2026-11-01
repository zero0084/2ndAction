# エステルへ: Google Play 素材 最終版の候補(レビュー第3回、2026-11-01 夜)

第2回のレビュー(マスターとエステルの指定)を反映した、最終版の候補です。ゲーム画面と操作の仕様は変えていません。変えたのは、撮影の場面の選び方と字幕だけです。公開は保留のままです。

## 1. 紹介動画 40秒(横 1920x1080・60fps)
- 動画: `Docs/GooglePlay_2026-11-01/video/omm_gp_promo40_final_{ja,en}.mp4`
  - 確認用の軽い版(30fps): `..._review.mp4`
- 2秒ごとの一覧: `promo40_grid_ja.jpg` / `promo40_grid_en.jpg`
- 全体の構成と尺(40.0秒)は第2回のままです。
- 変えた所:
  - 4方向の字幕: 「4方向のフリックで戦え」/「Swipe in four directions to fight.」
  - 脱出の英語の字幕: 「Escape with your rewards—or run one more mile?」(Cash out は使いません)
    - 画面の幅に入りきらないので、この字幕だけ文字を少し小さくしています。
  - 空中コンボの場面: BONUS ZONE の帯が写らない撮影に差し替えました。
    - 撮影の時だけ BONUS ZONE を起こさない設定にしています。BONUS が起きない区間は通常のプレイでもあります。
    - 日本語は 20 HIT、英語は 16 HIT まで写っています。
- 字幕の表は `video/captions_gp.tsv` です。Steam 版の表(エステル採用)を写して、上の2つだけ変えました。Steam 版の表は変えていません。
- そのほか(字幕・BGM・ロゴ・HUD の使い分け)は第2回のままです。
- 音の大きさ: -14.1 LUFS、真のピーク(日)-1.5 / (英)-1.8 dBTP

## 2. 操作説明ショート 20.2秒(縦 1080x1920・60fps)
- 動画: `video/omm_gp_short20_final_{ja,en}.mp4`(軽い版は `_review.mp4`)
- 一覧: `short_grid_ja.jpg` / `short_grid_en.jpg`
- 長さは 20.2秒のままです。
- 英語の字幕は、指定どおりに統一しました。

  | 秒 | 英語 |
  |---|---|
  | 0〜2 | Your character runs automatically. |
  | 2〜5 | Swipe forward: Attack ahead |
  | 5〜7 | Swipe back: Attack behind |
  | 7〜9 | Swipe up: Jump / Launch enemies |
  | 9〜14 | Keep hitting to build combos |
  | 14〜17 | Swipe down: Dive while airborne |
  | 17〜20 | How far will you go? |

- 追加したこと:
  - 最後のロゴの下に、小さく「技はキャラクターによって異なります / Moves vary by character.」
  - 冒頭の4秒、画面の下に小さく「※操作説明のため、映像を拡大しています / Footage zoomed in to show the controls」(**英語は仮訳**)
  - BONUS の表示(英語の BONUS MIMIC BASH を含む)が写らない撮影に、日英とも差し替えました。
- 音の大きさ: -14.0〜-14.2 LUFS(第2回の -15.3 から、紹介動画と同じ大きさにそろえました)、真のピーク -1.4〜-1.6 dBTP

## 3. スクリーンショット(日英 8枚)
- 一覧: `GooglePlay/StoreScreenshots_2026-11-01/contact_sheet_landscape_{ja,en}.jpg`

| # | 状態 |
|---|---|
| 1, 2, 4, 5, 7 | 第1回のまま(採用) |
| 3 空中コンボ | BONUS の帯が無い撮り直しに差し替え(日: 20 HIT / AIR 2、英: 15 HIT / AIR 4)。変更前後: `compare_03_aerial.jpg` |
| 6 脱出 | **提案: RESULT「脱出成功! / ESCAPED!」に差し替え**(下の説明) |
| 8 キャラ選択 | 第2回の Android 版の幻想画(採用) |

### 6枚目の提案
- 比較: `compare_06_small.jpg`(Google Play の小さい表示に近い幅 480px / 320px で並べた物)
- 説明の画面(A)は、幅 320px では本文がほとんど読めません。目に入るのは小さい見出しと板の形だけです。
- RESULT(B)は、幅 320px でも次が読めます。
  - 「脱出成功! / ESCAPED!」
  - 距離「30,827 m」
  - 持ち帰った「+1,019 / +1,013 MILE」
  - 強化カードの並び
- 「走って、持ち帰る」がひと目で伝わるので、**B を提案します**。いまは B を `06_EscapeResult.png` に入れてあります。
- A は `landscape/{ja,en}_alt/alt_EscapeGuide.png` に残してあり、すぐ戻せます。

## 4. 確認してほしい所
1. 6枚目を B(RESULT)にしてよいか。
2. ショート冒頭の注記の英語(仮訳)「Footage zoomed in to show the controls」。
3. 最終版として、この4本(日英の紹介動画とショート)でよいか。

## 5. 再生について
- 4本とも、一般的な再生環境でそのまま再生できる形式です(H.264 / AAC / yuv420p、faststart)。
- 紹介動画は1本約 110MB で、GitHub の1ファイルの上限(100MB)を超えます。そのためリポジトリには入れていません。
  - マスターの PC の `C:\GameProject\2ndAction\Docs\GooglePlay_2026-11-01\video` にあります。
  - 30MB 前後の `_review.mp4` もあります。
- 第2回の仮編集は `video/draft1/` に移しました。
