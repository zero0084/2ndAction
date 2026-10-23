# 紹介動画 第5稿 / 音の権利 / ライセンス表示 / 製品版の回帰確認(2026-10-23 夜)

## 完成候補(エステル: 第5稿を正式採用の本命、締めの一言あり)
| ファイル | 長さ | 音 |
|---|---|---|
| `trailer/omm_trailer_draft5_ja_endline_1080p60.mp4`(**日本語 本命**) | 68.0 秒 | -14.2 LUFS / -1.3 dBTP |
| `trailer/omm_trailer_draft5_en_endline_1080p60.mp4`(**英語 本命**) | 64.5 秒 | -14.3 LUFS / -1.4 dBTP |
| `trailer/omm_trailer_draft5_ja_1080p60.mp4` / `_en_`(締めの一言なし、比較用) | 同じ | 同じ |
| 第4稿 `omm_trailer_draft4_{ja,en}_1080p60.mp4` は上書きせず残した | | |

1920x1080、60fps、H.264 High + AAC 256kbps。git の外(*.mp4)。保管: `C:/GameProject/SteamCaptureArchive/2026-10-23/final/`

## 第4稿 → 第5稿の修正(構成は維持)
1. **洞窟の見え方**(比較: `compare_v4_v5/ja_1_cave.png`, `en_1_cave.png`)
   - 暗い時間の長いカットをやめ、連続ヒット・BREAK・クリスタルの技など戦闘がはっきり見える瞬間へ差し替え
   - 洞窟ボス 6.0秒 → 4.1秒(2カット: CRYSTAL PRISON の技 / BREAK からの空中コンボ)
   - 洞窟の場面だけ暗部をわずかに持ち上げ(ガンマ 1.08・彩度 1.04)。洞窟の暗さは残した
   - 英語版は撮影の中で戦闘の少ない時間があったため、ヒットのある瞬間を選び直した
2. **脱出判断**(最優先、比較: `ja_2_escape.png`, `en_2_escape.png`)
   - ボス撃破後の「ESCAPE AVAILABLE」→ 実際の説明板「脱出できるようになりました」(字幕なし)→ 長押しで脱出(持ち帰る MILE の内訳と溜めの表示)と同時に字幕「報酬を持ち帰るか、さらに先へ進むか。」→ RESULT(字幕なし)
   - 説明板と字幕は重ならない。字幕は 1 行
   - 締めの一言「決めるのはあなたです。」/「The choice is yours.」: ロゴのフェードイン後 2.4 秒、ロゴの下・中央に控えめな白金色で静かにフェード(エステル推奨で採用)
3. **ロゴ**(比較: `logo_size_compare_600px.png`, `ja_3_logo.png`)— 現状/1.2倍/1.4倍を 600px 表示で比較し **1.2倍** を採用(1.4倍は左右の飾りが画面の端に近い)。公式ロゴ TitleLogoV2 のまま縦横比も維持、表示 4.5 秒
4. **音** — BGM と SE の比は変えず、全体の大きさは -14 LUFS 前後のまま、真のピークを -1 dBTP 以下へ
   - 原因: 以前の音量合わせ(loudnorm)は上げ幅が大きいと別の処理へ切り替わり、AAC にした後のピークが 0dB を超えた
   - 対策: 全体を上げた後に 4 倍の細かさで見るリミッター、さらに AAC にした後に測ってリミッターの上限だけ下げる(`Tools/steam/finalize_audio.py`)
5. **カメラ** — 撮影用の寄せは使わず、製品版と同じ見え方のまま

## RESULT の「GAME CLEAR」(調査のみ、ゲームの文言は変えていない)
- 見出しは `GameManager.DrawResults` の文字だけ。勝ち負け/記録の判定(`IsWin`、脱出の成功時だけ `Win()` が呼ばれる)とは無関係 → 文字を変えても成功・失敗の判定や保存には影響しない
- 30 言語すべてに訳があり、どれも「ゲームをクリア」の意味(独 SPIEL GESCHAFFT、中 游戏通关 など)。変えるなら新しい文(例「脱出成功」/「ESCAPED!」)と 30 言語の訳、RESULT の幅の確認が必要
- エステルの判断: 今回は変えない。製品版の用語整理として別途マスターに判断してもらう

## 音の権利(マスターの方針: 継続使用)→ `Docs/AudioProvenance_2026-10-23/`
- 権利管理表 `audio_rights_registry.tsv`(291 件、1行1ファイル、md5・制作過程の確かさ・利用条件・元ファイル)
- ChatGPT 由来の 33 件: 元ファイル(ダウンロードした時のまま)を `originals/` に保存。生成過程が不明なことも表に記録
- **ゲーム中の BGM(GameplayBgm)も MuseScore 4 + MS Basic と確認**: 同じ MIDI を MuseScore 4.7.5 + MS Basic で書き出し直すと、元と相関 0.9999996・長さ/形式一致(記録で確定しているタイトル曲の対照と同じ一致度)。保存データは残っていないため「再現による確認」と記録
- MS Basic: `MuseScore_General_HQ v0.2`(ライセンス文「Current version: 0.2 13th May 2020」)、著作権 Frank Wen 2000-02 / Michael Cowgill 2014-17 / S. Christian Collins 2018-20 ほか

## MS Basic のクレジット(Steam 版・Android 版の両方)
- 設定 → データとプライバシー →「ライセンス」: 謝辞・著作権表示と MIT ライセンスの本文を**原文のまま**(`Resources/Licenses/ThirdPartyNotices.txt`、元の文と完全一致を確認)
- エンドロール: SOUNDFONT / MS BASIC(「BGMの音源(MuseScore, MIT ライセンス)」)
- 確認: 開発版で画面を開閉(横/縦 × 日本語/英語、戻るで閉じる)`-qaLicenses` 0 失敗。Windows 製品版と APK 358 の中に本文・エンドロールの項目が入っていることを確認
- 画面の文 3 件(ライセンス/表示する/説明)はエステルのレビュー待ち(`Docs/LocReview_2026-10-23d`)。訳が入るまで日本語のまま出る

## 製品版の回帰確認(Windows 製品ビルド、保存は試験の前に控えを取り、後で完全に元へ戻した)
| 確認 | 結果 |
|---|---|
| 試験用の起動オプション(-storeCapture -qaTap -fileSave -netAuto host -netAutoOnline -netAutoCards -netAutoSpeed -audioTest -bonusShots -runBuildHudTest -encounterForce -encounterSeed -qaLicenses)を全部付けて起動 | 普通のホーム。試験のファイルは作られず、ログに試験の動きなし |
| オンライン接続 | ONLINE PLAY → production の UGS に匿名サインイン成功、CO-OP の部屋の検索も成功(0 件)。部屋は作っていない |
| セーブ / MILE | ガチャ 1 回で 3,403 → 2,903 MILE、レジストリの保存も 2903 に。カードが増える |
| 無敵が入っていないこと | 操作なしのランは 67m で倒れて FAILED。失敗のランは MILE が増えない(2,903 のまま)・記録に残らない |
| 製品版に入らない物 | 撮影/試験/開発用の表示の文字は 0。LicensesUi は入る |
| 後片付け | レジストリと保存の控えのフォルダ(LocalLow)を試験の前と完全に同じに戻した |

## 保管(`C:/GameProject/SteamCaptureArchive/2026-10-23/`、git の外)
- masters/: 採用した撮影素材 25 ショット(元の劣化なし約 26GB → H.264 4:4:4 crf10 約 4.1GB)。**全ショットのフレーム数と音(wav)が元と一致**を確認。第5稿が使うショットはすべて入っている
- edit/: 編集データ(edl_v1〜v5)、字幕/紹介文の表、道具(trailer / finalize_audio / compare / about)、公式ロゴ、BGM 2 曲、第5稿の最終の音(mix_v5_ja/en.wav)
- final/: 第4稿・第5稿の完成 mp4
- README.md: 作り直し方

## 残る課題
- 締めの一言の採用・GAME CLEAR は、マスターの最終判断で確定
- 「ライセンス」画面の文 3 件と、データの削除の画面の訳(2026-10-19 のレビュー表)は、エステルのレビュー待ち
- 音は数値で確認済み。通常の音量と小さい音量で聞いての確認は、マスターかエステルの耳でお願いしたい
- 設定の画面は合成した入力ではスクロールできず、製品版で「ライセンス」の画面を開く所までは操作していない(画面は開発版で確認、本文は製品版の中に入っていることを確認)
