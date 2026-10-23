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
- **BGM/SE**: ゲームの音(Tools/audio のスクリプトで作った音、ファイルの説明は「仮音源」)をそのまま使用。商用の紹介動画に使ってよいかマスターの判断をお願いします
- **英語版のスクリーンショット**: 今の候補は日本語の UI。英語の UI で撮り直すのは `-scLang en` で同じ場面を撮るだけ(英語の動画の中の画面も日本語の UI のまま)
- **キャラが小さい**: 1080p の画面で主人公は約 60〜80px(高速時はカメラが少し引く)。製品の実際の見え方のままにしている。PC 向けにカメラを寄せる設定を作るかは別の判断
- **マルチプレイ**: まだ撮っていない(2つのプロセスで正常に動くのを確かめてから)
- **空中コンボの単体の場面**: 普通の敵は数発で倒れるので長いコンボにならず、BONUS ZONE の表示も重なった。紹介ではボスの BREAK 中の空中コンボ(実際の仕様)を使用
- 動画の最後の脱出の説明の場面で、字幕の帯が説明の板の下を少し隠す
- エステルさんへの見た目の相談(字幕/紹介画像のレイアウト)はまだ。ネタバレ(ラスダン/三姉妹/エンディング/100km で解放のキャラ)は含めていない
