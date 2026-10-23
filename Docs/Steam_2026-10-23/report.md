# Steam ストア素材 第1稿(2026-10-23)

## 撮影の仕組み(製品とは分離)
- **撮影用ビルド** `Tools/2ndAction/Build Windows Store Capture`(BuildStoreCapture)。開発版にしない(Unity の「Development Build」の透かしが出ない)。このビルドだけに記号 `DEVELOPMENT_BUILD` `OMM_CAPTURE` を足して、撮影の進行役と開発用の機能を入れる。製品のビルドにはこの記号を付けないので、撮影の仕組みは入らない
- `DevGate.On` = 開発版 または 撮影用ビルド。距離ワープ・BEST の設定・解放の上書きだけをこれで許可(製品では今までどおり `Debug.isDebugBuild` と同じ)
- 撮影中(`StoreCapture.Clean`)は「build 番号」「DEV BUILD · UGS」「TEST FILE」を出さない。DEBUG ボタン/診断/ワープ UI は撮影用ビルドでは元から出ない
- 撮影: `OneMoreMile.exe -fileSave <専用の保存> -storeCapture <dir> -scShots run_wasteland,aerial,cards,...`
  - 1920x1080、**60fps を固定の時間の流れで**書き出し(処理が重くてもコマ落ちしない)。映像は劣化なし(RGB)の mkv、音は AudioRenderer で同じ時間の流れの wav(SE/環境音のみ。BGM は編集で通しで重ねる。`-scBgm 1` で BGM 入り)
  - 新しい専用の保存を「遊び込んだ人の状態」に: BEST 40km 台、ガチャで集めた中盤のデッキ(炎/雷/氷/風など)。レベルアップの3択は実際の抽選
  - ボスは関門と同じ生成(BossManager)。撃破 → 報酬 → 脱出の説明(初回だけ出る実物)→ 長押しで脱出 → RESULT まで実際の流れ
  - 撮影中は無敵(撮影が倒れて止まらないように。被弾の見た目は出る)。補助(製品の設定にある自動のジャンプ/攻撃)を ON にして走る場面が多い

## できたもの
### スクリーンショット候補(`screenshots/ja/`、1920x1080 PNG、日本語の UI)
| ファイル | 内容 |
|---|---|
| omm_ss_01_wasteland_speed | 昼の荒野 100km/h で連続攻撃(15 HIT) |
| omm_ss_02_aerial_combo | ボスを打ち上げて空中コンボ(22 HIT / AIR 13、夕焼け) |
| omm_ss_03_boss_break | 巨大ボス(サイクロプス)を BREAK |
| omm_ss_04_card_choice | レベルアップの3択(ATTACK UP / INFERNO / FROST EDGE) |
| omm_ss_05_cave | 自然洞窟の戦闘(忍者、16 HIT / AIR 9) |
| omm_ss_06_sky_boss | 天空回廊のボス(激昂) |
| omm_ss_07_gunslinger | 二丁拳銃士(夕焼けの村、14 HIT / AIR 10) |
| omm_ss_08_twin_blades_crowd | 双剣士、複数の敵へ(14 HIT) |
| omm_ss_09_escape_decision | ボス撃破後、長押しで脱出(持ち帰る MILE の内訳) |
| omm_ss_10_cave_boss | 洞窟ボス(クリスタルゴーレム)と CRYSTAL PRISON |
| omm_ss_alt_escape_guide | 予備: 「脱出できるようになりました」の説明 |

### 紹介動画 第1稿(git には入れない)
- `trailer/omm_trailer_draft2_ja_1080p60.mp4` / `_en_`(73.3 秒、1920x1080、60fps、H.264 High + AAC 48kHz、約 28Mbps、-17 LUFS)
- 構成: 0-5 疾走 → 5-16 4方向の攻撃・空中コンボ → 16-27 カード選択・別のキャラ → 27-41 荒野/洞窟/天空 → 41-63 巨大ボス(荒野/天空/洞窟、BREAK → 撃破)→ 63-69 脱出の判断 → 公式ロゴ
- BGM はゲームの BGM(荒野 → ボス)を通しで、SE は撮影の時の実際の音。字幕は `text/captions.tsv`(ja/en)から。文字を直したら `Tools/steam/trailer.py trailer/edl_v2.json <ja|en> <out>` で作り直せる
- 収録素材(劣化なしの mkv + wav)は作業フォルダに約 25GB。採用が決まったショットだけ保存先へ移す(大きいので git には入れない)

### システム紹介の画像(`about/out/`、幅 1232 と Steam の表示幅 616、ja/en)
- 01 自動で走る+4方向の攻撃 / 02 空中コンボ / 03 3枚から1枚 / 04 ボス撃破 → 持ち帰るか、もう1マイルか / 05 マップとキャラ
- 文字は `text/about.tsv`、配置は `about/layout.json`。`Tools/steam/about.py about/layout.json <ja|en> about/out` で作り直せる
- 短いアニメーション(GIF 616 幅 / WebM 1232 幅): 空中コンボ・カード選択・走って戦う・脱出(`about/anim/`、git には入れない)

## 未対応 / 確認したいこと
- **BGM/SE**: 【2026-10-23 訂正】ほとんどはスクリプトで作った音だが、一部は ChatGPT 由来(紹介動画に被弾の音とカードを選ぶ音が入っている)。詳しくは audio_rights.md
- **英語版のスクリーンショット**: 今の候補は日本語の UI。英語の UI で撮り直すのは `-scLang en` で同じ場面を撮るだけ(英語の動画の中の画面も日本語の UI のまま)
- **キャラが小さい**: 1080p の画面で主人公は約 60〜80px(高速時はカメラが少し引く)。製品の実際の見え方のままにしている。PC 向けにカメラを寄せる設定を作るかは別の判断
- **マルチプレイ**: まだ撮っていない(2つのプロセスで正常に動くのを確かめてから)
- **空中コンボの単体の場面**: 普通の敵は数発で倒れるので長いコンボにならず、BONUS ZONE の表示も重なった。紹介ではボスの BREAK 中の空中コンボ(実際の仕様)を使用
- 動画の最後の脱出の説明の場面で、字幕の帯が説明の板の下を少し隠す
- エステルさんへの見た目の相談(字幕/紹介画像のレイアウト)はまだ。ネタバレ(ラスダン/三姉妹/エンディング/100km で解放のキャラ)は含めていない

---

# 第2弾(2026-10-23 午後): 仕上げ・英語版・安全確認・保管

## 1. 音の権利 → `audio_rights.md`
- 291 件を照合。258 件はスクリプトの数式だけで作った自作の音(再実行でバイト一致 248 件)
- 自作でないもの 33 件: `Audio/SE/` 16 件(ChatGPT の WAV)、その音量調整版 `P/SE/old_*_n` 15 件(7 件はゲームで鳴る)、TitleBgm/GameplayBgm(ChatGPT の MIDI を MuseScore 4 で書き出し。音源 MS Basic は MIT で、作品に謝辞の表示が必要)
- 紹介動画の効果音に ChatGPT 由来の音(被弾・カードを選ぶ)が入っている。BGM 2 曲は自作
- 判断待ち: ChatGPT の効果音を使い続けるか差し替えるか / GameplayBgm の書き出しの道具 / MS Basic の表示

## 2. 日本語版の仕上げ(エステルのレビュー反映、`omm_trailer_draft4_ja_1080p60.mp4` 70.3 秒)
- 脱出の説明の板の上には字幕を出さない。カード選択の場面は画面の上に出す(下の説明の板と重ならない)
- 字幕を大きく(64 → 80px、太いゴシック)、1 つ最大 4.5 秒。600px の再生画面で読めることを確認
- 冒頭 1〜2 秒をキャラと敵が近い戦闘の場面に。ボス紹介を短く(似た場面を 1 つ外して約 19 秒)
- 音: BGM と SE を分けて測り、SE が約 3dB 上になるよう BGM を下げた。全体 -14 LUFS、ピーク -0.5dBFS 以下
- 小さい再生画面: 寄せた版も試したが HUD が切れるため、エステルの判断で寄せない版を採用

## 3. 英語版(`omm_trailer_draft4_en_1080p60.mp4` 66.7 秒)
- 英語の UI で全場面を撮り直し(`-scLang en`)。ゲームの中の画面もすべて英語
- 撮影で見つかった訳の抜けを直した: ラン中の「持ち帰り予定」→ Take-home MILE、キャラ選択の「キャラカード」(どちらもレビュー済みの訳へつないだ)
- 「データの削除」の画面の 6 件は新しい訳が必要 → `Docs/LocReview_2026-10-23c/`(エステルのレビュー待ち、ID 10056〜10061)
- 字幕: Choose 1 of 3. ほか(エステル)。スクリーンショットも英語版を作り直し(`screenshots/en/`)

## 4. 紹介画像(構成 5 種は維持、エステルのレビュー反映)
- 本文 28 → 31px(616px 表示で約 15px)、番号の丸を約 80% にして左下へ(HUD を隠さない)
- 04 の真ん中のコマは、脱出の説明の板の部分を大きく(実際の画面の切り抜きだけ、合成なし)
- 英語の見出し: Auto-run. Attack in four directions. / Beat the boss. Cash out — or run one more mile?

## 5. 製品版の安全確認 → `product_entry_audit.md`
- 通常の Windows 製品ビルド(`Tools/2ndAction/Build Windows Product`)を作成。開発版の印の無かった QA の 4 ファイルのせいで、製品版はコンパイルできない状態だった → 修正
- 製品版でも起動引数で動く試験の入口が 5 つあった(マルチの試験ボット=本番のオンラインに入る・無敵など、敵の出方の固定、MILE が増える試験 3 つ)→ 開発版だけに
- 確認: 製品版の中に試験/撮影/開発用の表示の文字が無い。試験の引数を付けて起動しても普通のホーム、ファイルも作られない
- 撮影用ビルドにだけある処理: `DevGate`(距離ワープ/BEST/解放の上書き)、`StoreCapture.Clean`(開発用の表示を隠す)、`QaSweep -storeCapture`。製品版には入らない

## 6. 保管
- 完成動画: `trailer/omm_trailer_draft4_{ja,en}_1080p60.mp4`(git の外)
- 編集データ: `trailer/edl_v4_{ja,en}.json`、字幕/紹介文 `text/*.tsv`、紹介画像の配置 `about/layout.json`、道具 `Tools/steam/*.py`(git)
- 撮影素材: 採用したショット(日英)を `C:/GameProject/SteamCaptureArchive/2026-10-23/masters/` へ、見た目で劣化の無い H.264 4:4:4 + 元の wav で保存(元の約 1 割の大きさ)。EDL の `raw` をこのフォルダにすれば同じ動画を作り直せる
- エステル確認用: `review_for_estelle/`(480p の動画、JPG)
