# 音の権利管理(2026-10-23)

## 方針(マスター決定 2026-10-23)
- ChatGPT 由来の効果音 33 件(下)は **継続使用**。OpenAI の利用規約上、通常の生成出力は商用利用可。一斉の作り直しはしない
- 元の生成過程が分からない音は、その事実を権利管理表に記録し、元ファイルと制作の経緯を保存する(このフォルダ)
- MS Basic(MuseScore の音源)の表示をゲームのクレジットに入れる(Steam 版・Android 版)

## ファイル
| ファイル | 内容 |
|---|---|
| `audio_rights_registry.tsv` | **権利管理表**: ゲームの音 291 件すべて(1行1ファイル)。md5・作ったもの・出どころ・制作過程の確かさ・利用条件・保存した元ファイル・ゲームでの使用・紹介動画 |
| `originals/se/` | ChatGPT からダウンロードした効果音の元ファイル(12 個 + zip 1 個 = `Audio/SE/` の 16 件の元)。ダウンロードした時のまま(日時を保持) |
| `originals/bgm/` | BGM 2 曲の作曲の MIDI(ChatGPT)、タイトル曲の MuseScore の保存データ、MS Basic のライセンス文、MuseScore の最近使ったファイル |
| `tools/` | 管理表を作るスクリプト、BGM の再書き出しの比較(結果の記録つき) |
| `../Steam_2026-10-23/audio_rights.md` | 監査の報告(調べ方・根拠の詳細) |

## 制作の経緯(分かっていること / 分からないこと)

### 効果音 `Audio/SE/` 16 件と `Placeholder/SE/old_*_n` 15 件 — 制作過程: **不明**
- 2026-08-24〜09-01 に、マスターが ChatGPT の会話で作らせた WAV。どのファイルも Windows のダウンロードの記録で出どころが `https://chatgpt.com/backend-api/estuary/content`(下の表)
- ChatGPT の中でどう作ったか(数式で合成したのか、外部の素材を使ったのか)は**記録が無く分からない**。WAV の中にも作者・道具の情報は無い(形は Python の wave で書いた WAV と同じ。推測であり確認はできない)
- `old_*_n` 15 件は、上の WAV をプロジェクトのスクリプト(`Tools/audio/gen_audio_v2.py`)が読み込み、音量だけ揃えたもの(中身は同じ音)

| 元ファイル | ダウンロード日時 | 出どころ | ゲームのファイル |
|---|---|---|---|
| OneMoreMile_SE_Subtle_Pack.zip | 2026-09-01 19:58 | chatgpt.com | SE/01_attack_hit, 02_player_damage, 03_enemy_defeat, 04_player_death, 05_landing |
| 2ndAction_attack1_swin_v5_quiet.wav / attack2_zwin / attack3_zwan | 2026-08-28 22:27 | chatgpt.com | SE/AttackSe1〜3 |
| 2ndAction_jump_whoosh_v4_quiet.wav / double_jump | 2026-08-28 22:21 | chatgpt.com | SE/JumpSe, DoubleJumpSe |
| 着地.wav | 2026-08-26 23:03 | chatgpt.com | SE/LandSe |
| OMM_card_confirm_glow / deck_appear / draw / flip / select.wav | 2026-08-29 12:15 | chatgpt.com | SE/CardConfirmSe, CardDeckAppearSe, CardDrawSe, CardFlipSe, CardSelectSe |

### BGM `TitleBgm.wav`(HOME・エンドロール) — 制作過程: **確定**
- 作曲: ChatGPT の MIDI `ゲームTOPBGM.mid`(2026-08-19、出どころ chatgpt.com)
- 音への書き出し: MuseScore 4(保存データの記録 4.7.4、rev 7688c00)+ 音源 **MS Basic**(保存データ `audiosettings.json` で全パートが MS Basic)。2026-08-22 22:33 に書き出し
- 確認: 同じ MIDI を 2026-10-23 に MuseScore 4.7.5 + MS Basic で書き出し直し → 元の WAV と相関 0.9999996、長さ・形式が一致(`tools/rerender_TitleBgm.txt`)

### BGM `GameplayBgm.wav`(荒野街道 序盤) — 制作過程: **確定(再現による確認)**
- 作曲: ChatGPT の MIDI `ゲームプレイBGM.mid`(2026-08-21、出どころ chatgpt.com。`One_More_Mile_Celtic_Road_Loop_v1 (1).mid` と同一)
- 音への書き出し: MuseScore 4 + 音源 MS Basic。この曲の MuseScore の保存データは残っていないが、
  - 同じ MIDI を 2026-10-23 に MuseScore 4.7.5 + MS Basic で書き出し直すと、元の WAV と相関 0.9999996、長さ・形式(44.1kHz・ステレオ・32bit 小数)が一致(`tools/rerender_GameplayBgm.txt`)。タイトル曲(書き出しの方法が記録で確定している)の対照とまったく同じ一致度
  - 残りの差(元の音量の約 -60dB)は MuseScore の版の違い(4.7.4 → 4.7.5)の範囲
  - 状況も一致: タイトル曲の書き出しの 80 秒後(22:34)に作られ、MuseScore の最近使ったファイルにこの MIDI がある。この PC の MuseScore の音源は MS Basic だけ
- 他の音源・ソフトで同じ波形になることは考えにくいため、MuseScore 4 + MS Basic と判断した

## MS Basic(クレジットに入れる内容)
- 音源のファイル: `C:/Program Files/MuseScore 4/sound/MS Basic.sf3`(MuseScore Studio 4.7.5 に同梱)。中の情報: 名前 `MuseScore_General_HQ v0.2`、著作権 `Frank Wen 2000-02, Michael Cowgill 2014-17, S. Christian Collins 2018-20`、「Released under the MIT license」
- ライセンス文: `originals/bgm/MS Basic_License.md`(「Current version: 0.2 13th May 2020」)。「上の謝辞と著作権表示を、派生した作品すべてに含めること」
- 書き出しの時(2026-08-22)の MuseScore は 4.7.4。MS Basic は MuseScore 4 の間ずっと v0.2 のまま(今の 4.7.5 の物も v0.2)
- ゲームでの表示: クレジット/ライセンスの画面に、謝辞・著作権表示と MIT ライセンス本文をそのまま収録(`Unity/2ndAction/Assets/Resources/Licenses/`)
