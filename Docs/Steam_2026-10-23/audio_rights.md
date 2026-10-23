# One More Mile 音(BGM・効果音)の権利確認(2026-10-23)

> 調査のみ。音のファイル・スクリプト・シーン・設定は何も変えていない(作り直しの確認は作業用フォルダへ書き出して比べた)。
> 対象: `Unity/2ndAction/Assets` の中の音 **291 件**(`Audio/` 2曲、`Audio/SE/` 16件、`Audio/Placeholder/` 273件)。Packages・Plugins・Resources にほかの音のファイルは無い。
> 表の `P/` は `Assets/Audio/Placeholder/` の略。

## 1. 結論

- **Placeholder の 273 件のうち 258 件は、このプロジェクトのスクリプト(`Tools/audio/gen_audio*.py`)が数式だけで作った音**。外部の音・音源(サウンドフォント)・MIDI・ダウンロードした素材は使っていない。商用利用に問題は見つからなかった(**自作**)。
- ただし **「全部スクリプトで作った音」ではない**。次の 33 件は外から持ち込んだ音、またはそれを音量調整しただけの音で、**マスターの確認が必要**。
  1. `Audio/SE/*.wav` の **16 件**: スクリプトでは作っていない。**ChatGPT が作った WAV** をマスターが 2026-08-24〜09-01 に会話へ添付したもの(ダウンロード元は chatgpt.com、中身も完全一致)。ChatGPT の中でどう作ったか(外の素材を混ぜていないか)は記録が無い。
  2. `P/SE/old_*_n.wav` の **15 件**: スクリプト(`gen_audio_v2.py`)が上の 16 件を**読み込んで音量だけ揃えた**もの。名前は Placeholder でも、中身は ChatGPT の音。
  3. `Audio/TitleBgm.wav` / `Audio/GameplayBgm.wav` の **2 曲**: ChatGPT が作った MIDI を、マスターが MuseScore 4 で音にしたもの。音源 **MS Basic(MIT ライセンス)は、作品に謝辞と著作権表示を入れることを求めている**。GameplayBgm の書き出しに使った道具は状況証拠だけ。
- **今の紹介動画の BGM**(`bgm_wasteland_middle_n.wav`、`bgm_boss_strong_n.wav`)は **自作で問題なし**。
- **紹介動画の効果音には ChatGPT の音が入っている**: 撮影した音を調べたところ、`old_player_damage_n`(被弾の音)と `old_CardSelectSe_n`(カードを選ぶ音)がはっきり見つかった。剣の 1・2 段目の音(`old_AttackSe1_n` / `old_AttackSe2_n`)、着地、死亡の音もゲーム中で鳴る設定なので、撮り直すと入る可能性がある。
- 第1稿の `plan.md` / `report.md` の「ゲームの BGM/SE はスクリプトで作った音(権利はプロジェクトのもの)」は正しくなかった(上の 33 件があるため)。両方の文書に訂正を入れた。

## 2. 調べ方

| 調べたこと | 方法 |
|---|---|
| どのスクリプトが作ったか | 3 本のスクリプトを全部読み、書き出す名前を一つずつ対応付けた。さらに **スクリプトを作業用フォルダで実際に動かし、ゲームのファイルとバイト単位で比べた**(下の「再実行での一致」) |
| 外からの入力 | スクリプトの読み込み処理(`open` / `wave.open` / `load` / `glob`)、import、URL・サウンドフォント・MIDI の有無を確認 |
| スクリプト以外の音の出どころ | git の履歴、Downloads の元ファイル(中身の一致・Windows が記録したダウンロード元)、Claude Code の会話記録(2026-08)、WAV の中の情報、MuseScore の記録、既存の調査 `Docs/AssetProvenance_2026-10-07.md` |
| ゲームで鳴るか | 唯一のビルド対象 `Scenes/Main.unity` と `Resources` 以下から参照をたどってビルドに入るか判定。さらに `AudioLibrary.asset`(音の割り当て表)とコード(`AudioManager` / `RewardCardSequence` / `GameManager`)で、実際に鳴らす音か、予備の音かを確認 |
| 紹介動画に入っているか | 編集データ(`trailer/edl_v2.json`, `edl_v3_ja.json`)の BGM を確認。効果音は、撮影した音(作業用フォルダの `cap_a*/raw/*.wav`)のうち **動画で使った区間だけ**から、外部由来の音の波形を探した(音程の揺らぎ ±6% まで) |

### 再実行での一致(Placeholder 273 件)

| 結果 | 件数 | 意味 |
|---|---|---|
| バイト単位で完全一致 | 248 | スクリプトがそのファイルを作ったことが確定 |
| 曲の旋律だけ違う | 18(BGM の元曲) | `song()` の旋律の乱数が Python の文字列ハッシュ(実行ごとに変わる)で決まるため。長さは完全一致、伴奏・太鼓は同じで、全体の相関 0.74〜0.95。作り方は同じと判断 |
| 乱数の並びが違う | 7(`gen_audio_v2.py` の環境音の単発 7 件) | 雑音の乱数の順番が、当時の実行の順番に左右されるため。式はスクリプトの通りで、外からの入力は無い |
| 合計 | 273 | 名前の分からないファイル・スクリプトで作れないファイルは無い |

git でも、273 件はすべて対応するスクリプトと同じコミットで追加され、その後書き換えられていない(f7e8a9a 66件 / 69fe5f1 4件 / 43ad355 179件 / 1128410 24件)。

## 3. グループ別のまとめ

| フォルダ・名前 | 件数 | 作ったもの | 外からの入力 | ゲームで | 紹介動画 | 判定 |
|---|---|---|---|---|---|---|
| `P/Bgm/bgm_*.wav`(元の曲) | 18 | `gen_audio.py` `song()`(16)/ `gen_audio_v2.py` `gen_new_songs()`(2) | なし | 使っていない(ビルドに入らない) | なし | 自作 |
| `P/Bgm/bgm_*_n.wav`(音量を揃えた版) | 18 | `gen_audio_v2.py` `normalize_bgm()` | なし(自作の元曲だけ) | 鳴る(全ステージ・ボス・闘技場など) | `bgm_wasteland_middle_n` と `bgm_boss_strong_n` を使用 | 自作 |
| `P/Jingle/*.wav` | 2 | `gen_audio.py` `jingle()` | なし | 鳴る(RESULT / GAME OVER) | なし | 自作 |
| `P/Ambience/*.wav`(元) | 13 | `gen_audio.py` `loop_noise()` / `amb_shot()` | なし | 使っていない | なし | 自作 |
| `P/Ambience/*_n.wav` と v2 の新規 | 20 | `gen_audio_v2.py` `gen_ambient()` | なし | 鳴る | 可能性あり(撮影の音に環境音を含む) | 自作 |
| `P/SE/*.wav`(第1世代) | 39 | `gen_audio.py` `gen_all()` | なし | 使っていない(音量を揃えた版が使われる) | なし | 自作 |
| `P/SE/*.wav`(第2世代、old_ 以外) | 124 | `gen_audio_v2.py` `gen_se()` | なし(自作の第1世代を読み込むものはある) | 120 件が鳴る / 4 件は使っていない | 可能性あり(`mv_land`, `atk_gun_0` は検出) | 自作 |
| `P/SE/*_v3.wav` | 24 | `gen_audio_v3.py`(`boss_impact` / `jump_light` / `jump_double` / ボスの声 7 種×3) | なし | 鳴る | 可能性あり | 自作 |
| **`P/SE/old_*_n.wav`** | **15** | `gen_audio_v2.py` `gen_se()`「Old pack」= `Audio/SE` の音を読み込み音量調整 | **あり: ChatGPT の WAV** | **7 件が鳴る** / 8 件は使っていない | **`old_player_damage_n`・`old_CardSelectSe_n` を検出**、ほか 5 件は可能性あり | **要確認** |
| **`Audio/SE/*.wav`** | **16** | スクリプトでは作っていない | **ChatGPT の WAV** | `CardSelectSe` は HOME のタップ音として鳴る / 15 件はビルドに入るが予備で通常は鳴らない | なし(HOME の場面なし) | **要確認** |
| **`Audio/TitleBgm.wav`** | **1** | スクリプトでは作っていない | **ChatGPT の MIDI + MuseScore 4 + 音源 MS Basic** | 鳴る(HOME、ラスダンのエンドロール) | なし | **要確認(表示が必要)** |
| **`Audio/GameplayBgm.wav`** | **1** | スクリプトでは作っていない | **ChatGPT の MIDI + 書き出しの道具は未確定(MuseScore 4 + MS Basic の可能性が高い)** | 鳴る(荒野街道 0〜10,000m) | なし | **要確認** |

ほかに、ファイルではない音として C# のコードで作る音がある(`AudioFactory.cs` は予備、`BonusZone.cs`, `FusionSfx.cs` は `AudioClip.Create` で数式から作る)。外部の素材は使っていない。広告(Google Mobile Ads)は広告主の音を流すことがあるが、ゲームの素材ではなく、紹介動画にも入っていない。`Unity/2ndAction/Builds/AudioCheck/gameplay_audio_2026-10-07.wav` は動作確認でゲームの音を録音したもので、Assets の外(ビルドに入らない)。

## 4. 自作と言い切れないファイル(根拠つき)

### 4-A. `Audio/SE/` の 16 件(ChatGPT が作った WAV)

| ゲームのファイル | Downloads の元ファイル(中身が完全一致) | ゲームに入れた日(会話記録) |
|---|---|---|
| `01_attack_hit` `02_player_damage` `03_enemy_defeat` `04_player_death` `05_landing` | `OneMoreMile_SE_Subtle_Pack.zip` の中身(2026-09-01) | 2026-09-01 |
| `AttackSe1` `AttackSe2` `AttackSe3` | `2ndAction_attack1_swin_v5_quiet.wav` / `attack2_zwin_v5` / `attack3_zwan_v5`(2026-08-28) | 2026-08-28 |
| `JumpSe` `DoubleJumpSe` | `2ndAction_jump_whoosh_v4_quiet.wav` / `2ndAction_double_jump_whoosh_v4_quiet.wav`(2026-08-28) | 2026-08-28 |
| `LandSe` | `着地.wav`(2026-08-26) | 2026-08-26 |
| `CardConfirmSe` `CardDeckAppearSe` `CardDrawSe` `CardFlipSe` `CardSelectSe` | `OMM_card_confirm_glow.wav` / `OMM_card_deck_appear.wav` / `OMM_card_draw.wav` / `OMM_card_flip.wav` / `OMM_card_select.wav`(2026-08-29) | 2026-08-29 |

根拠:
- 元ファイルと **中身が完全一致**(MD5)。元ファイルにはどれも、Windows が記録した **ダウンロード元 chatgpt.com** がある(zip 2 つと `着地.wav` も同じ)。
- 会話記録(2026-08-24〜09-01)で、マスターがこれらを添付して「SE を変えて」と依頼している。zip の README は「OneMoreMile restrained SE pack / WAV / mono / 22.05 kHz」で、作者・ライセンスの記載は無い。
- WAV の中は音の形式とデータだけで、作った道具・販売元の情報(LIST/INFO、bext)は無い。Python の `wave` で書いた WAV と同じ形。**推測**: ChatGPT が自分の中の Python で合成した可能性が高いが、記録で確かめられない。
- git の最初の記録は初回コミット a96f688(2026-09-06、それまでの作業をまとめたもの)。リポジトリ内に、これらのライセンス・クレジットの記載は無い(license / ライセンス / 素材 / credit / freesound / 効果音ラボ / 魔王魂 / DOVA / CC0 / CC-BY で検索)。
- 利用条件: OpenAI の利用規約による(出力の扱いは規約の定め。当時の規約の写しとプランの記録は無い。2026-09-12 時点で Plus の記録あり)。Steam の AI 利用申告の対象(AI で作った内容)になる。

ゲームでの扱い:
- `CardSelectSe.wav` だけは **HOME の部屋をタップした時の音として直接鳴る**(`GameManager.roomTapSe`)。
- ほかの 15 件は `Main.unity` に予備として設定されていてビルドには入るが、`AudioLibrary` に同じ役目の音があるため **通常は鳴らない**(`AudioManager.ClipFor` / `RewardCardSequence.PlaySe` は割り当て表を先に使う)。

### 4-B. `P/SE/old_*_n.wav` の 15 件(上の音を音量調整しただけ)

`gen_audio_v2.py` 229〜240 行の「Old pack, normalized copies」で `Audio/SE/` の音を読み込み、音量を揃えて書き出している(元の音との相関 0.999〜1.000、再実行でも完全一致)。**中身は 4-A の ChatGPT の音**なので、権利の扱いも 4-A と同じ。

| ファイル | 元の音 | ゲームで | 紹介動画 |
|---|---|---|---|
| `old_AttackSe1_n` | `AttackSe1` | **鳴る**: 剣のキャラ(黒剣士・お嬢様騎士)の攻撃 1 段目 | 動画の区間では検出されず(撮り直すと入る可能性あり) |
| `old_AttackSe2_n` | `AttackSe2` | **鳴る**: 剣のキャラの攻撃 2 段目 | 検出されず(同上) |
| `old_landing_n` | `05_landing` | **鳴る**: 着地(`mv_land` とランダム) | 被弾の音と波形が似ていて区別できない。入っている可能性が高い |
| `old_player_damage_n` | `02_player_damage` | **鳴る**: プレイヤーの被弾(この音だけ) | **検出**: `run_day`(動画の最初のカット)と `boss_sky_fight`、一致度 0.96〜0.98 |
| `old_player_death_n` | `04_player_death` | **鳴る**: プレイヤーの死亡(この音だけ) | 検出されず |
| `old_CardSelectSe_n` | `CardSelectSe` | **鳴る**: カードを選ぶ音(この音だけ) | **検出**: `cards` のカット、一致度 0.88 |
| `old_CardFlipSe_n` | `CardFlipSe` | **鳴る**: カードをめくる音(`card_flip` とランダム) | 検出されず(可能性あり) |
| `old_jump_n` `old_double_jump_n` `old_attack_hit_n` `old_enemy_defeat_n` `old_AttackSe3_n` `old_CardConfirmSe_n` `old_CardDeckAppearSe_n` `old_CardDrawSe_n` | 各元の音 | 使っていない(ビルドに入らない) | なし |

### 4-C. BGM 2 曲

| 曲 | ① 作曲(MIDI) | ② 音への書き出し | 確かさ |
|---|---|---|---|
| `TitleBgm.wav`(HOME・エンドロール) | ChatGPT の MIDI(`ゲームTOPBGM.mid`、ダウンロード元 chatgpt.com) | MuseScore 4、音源 **MS Basic** | **確認済み**: Downloads の `ゲームTOPBGM.wav` と完全一致(ダウンロード元の記録なし=PC で作った)、MuseScore の保存データ `Downloads/ゲームTOPBGM.mp4/audiosettings.json` で全楽器が MS Basic |
| `GameplayBgm.wav`(荒野街道 序盤) | ChatGPT の MIDI(`ゲームプレイBGM.mid`、ダウンロード元 chatgpt.com) | **未確定**。MuseScore 4 + MS Basic の可能性が高い | 状況証拠: Downloads の `ゲームプレイBGM.wav` と完全一致(ダウンロード元なし)。MuseScore を初めて起動したのが 2026-08-22 22:25、`ゲームTOPBGM.wav` が 22:33、`ゲームプレイBGM.wav` が 22:34 に作られ、形式も同じ(44.1kHz・ステレオ・32bit 小数)。MuseScore の最近使ったファイルにこの MIDI がある。この PC の MuseScore の音源は MS Basic だけで、Muse Sounds を入れる Muse Hub のフォルダは 08-31 作成(曲の後)。ほかの音楽ソフトは見当たらない。ただし、この曲の保存データ(音源が分かるもの)は残っていない |

利用条件:
- ① は OpenAI の利用規約による。
- ② MS Basic(`C:/Program Files/MuseScore 4/sound/MS Basic_License.md`)は MIT ライセンス。商用利用はできるが、文書に「上の謝辞と著作権表示を、派生した作品すべてに含めること」とある(FluidR3 の Frank Wen ほか)。**ゲームのクレジットやライセンス表示に入れる必要がある**。
- どちらの曲も、今の紹介動画には入っていない(撮影時は BGM を消音し、編集で自作の 2 曲を重ねている)。

## 5. 生成スクリプトの点検

| スクリプト | 使っているもの | 外から読むもの | 判定 |
|---|---|---|---|
| `gen_audio.py` | `os`, `math`, `wave`, `numpy` | なし(書き出すだけ) | 正弦波・三角波・のこぎり波・矩形波・雑音とフィルターの式だけ |
| `gen_audio_v2.py` | `os`, `sys`, `math`, `wave`, `glob`, `numpy`, `gen_audio` | `load()` で読むのは (1) `gen_audio.py` が作った自作の音と (2) **`Assets/Audio/SE/` の ChatGPT の音 15 件**(old_*_n を作る部分だけ) | (2) を除けば自作。(2) は 4-B のとおり |
| `gen_audio_v3.py` | `os`, `sys`, `math`, `numpy`, `scipy.signal`, `gen_audio_v2` | なし(`--report` は自分の出力を読むだけ) | 声も含めて式だけ(のど=のこぎり波+共鳴のフィルター)。冒頭の注記「外部の素材・サンプルは使っていない」と中身が一致 |

- サウンドフォント・MIDI 音源・fluidsynth・学習済みのモデル・ネットからのダウンロード・音の素材集は **どれも使っていない**。numpy / scipy / wave は計算と書き出しの道具で、出力に条件は付かない。
- 注意点(権利ではなく再現性): BGM の旋律は Python の文字列ハッシュで決まるので、同じ曲をもう一度作ることはできない。今のファイルを大事に保管すること。

## 6. 紹介動画への影響

| 音 | 入っているか | 出どころ |
|---|---|---|
| BGM `bgm_wasteland_middle_n.wav`(0〜41秒)、`bgm_boss_strong_n.wav`(41秒〜) | 入っている(編集データで確認) | 自作 |
| 効果音・環境音 | 撮影した音(BGM は消音)をそのまま使用 | ほとんど自作。ただし **`old_player_damage_n` と `old_CardSelectSe_n`(ChatGPT 由来)を検出** |

検出の詳しい結果(`draft2` と `draft3` で同じ区間): `old_player_damage_n` 0.98(`cap_a2/raw/boss_sky_fight` 1.3秒)・0.96(`cap_a2/raw/run_day` 2.5秒)、`old_CardSelectSe_n` 0.88(`cap_a3/raw/cards` 4.6秒)。比べるため、自作の `mv_land` 0.92 も検出された。剣の攻撃音・死亡の音・カードをめくる音は 0.6 未満で、見つからなかった(打ち消し合いなどで見落とす可能性は残る)。英語版の撮影(`cap_en1`)は同じゲームの音なので、同じ音が入りうる(未検査)。

## 7. マスターに確認してほしいこと

1. **ChatGPT の効果音 16 件**(`Audio/SE/`)を ChatGPT にどう作らせたか。ChatGPT の会話が残っていれば、ChatGPT が Python で合成したのか、外の音を使ったのかを確認したい。確認できない場合、次のどちらかを選んでください。
   - (a) このまま使う(Steam の AI 利用申告に「効果音の一部を ChatGPT で生成」と入れる)
   - (b) スクリプトで作った音に差し替える。ゲームで鳴っているのは 8 件だけ: `old_AttackSe1_n`, `old_AttackSe2_n`, `old_landing_n`, `old_player_damage_n`, `old_player_death_n`, `old_CardSelectSe_n`, `old_CardFlipSe_n`, `Audio/SE/CardSelectSe.wav`(HOME のタップ)。着地とカードをめくる音には自作の音も並んでいるので外すだけで済む。差し替えた場合、**紹介動画の効果音も撮り直し(または該当箇所の差し替え)が必要**。
2. **GameplayBgm** を音にした道具(MuseScore 4 + MS Basic だったか)。
3. **MS Basic の表示**をゲームのクレジット(またはライセンス画面)に入れること。TitleBgm は確定、GameplayBgm も 2 の答えしだいで同じ。

## 付録. 全 291 件の一覧

「再実行での一致」: 完全一致=作業用フォルダでスクリプトを動かし、バイト単位で同じ。「ゲームでの使用」の「鳴る:」の後ろは割り当て表(AudioLibrary)の役目(SeId・武器・ステージ)。「紹介動画」の「検出」は撮影した音の中で見つかったもの、「可能性あり」はゲーム中で鳴る音なので撮影した場面で鳴っていれば入っているもの。

| # | ファイル | 作ったもの(スクリプト/関数) | 再実行での一致 | 出どころ | ゲームでの使用 | 紹介動画 | 判定 |
|---|---|---|---|---|---|---|---|
| 1 | `GameplayBgm.wav` | スクリプトでは作っていない(外部から持ち込み) | - | ChatGPT の MIDI → MuseScore 4 で書き出し(音源 MS Basic)※書き出しの道具は状況証拠のみ | 鳴る: 荒野街道 序盤(0〜10,000m) | なし(撮影時は BGM を消音) | 要確認(外部素材) |
| 2 | `P/Ambience/amb_bat_flutter.wav` | gen_audio_v2.py gen_ambient() | 乱数の並びの違いで不一致 | 自作スクリプト | 鳴る: natural_cave | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 3 | `P/Ambience/amb_bird_0.wav` | gen_audio.py amb_shot() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 4 | `P/Ambience/amb_bird_0_n.wav` | gen_audio_v2.py gen_ambient()(gen_audio の元ファイルの音量調整版) | 完全一致 | 自作スクリプト | 鳴る: homeAmbience, wasteland_road | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 5 | `P/Ambience/amb_bird_1.wav` | gen_audio.py amb_shot() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 6 | `P/Ambience/amb_bird_1_n.wav` | gen_audio_v2.py gen_ambient()(gen_audio の元ファイルの音量調整版) | 完全一致 | 自作スクリプト | 鳴る: homeAmbience, sky_corridor | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 7 | `P/Ambience/amb_bird_2.wav` | gen_audio.py amb_shot() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 8 | `P/Ambience/amb_bird_2_n.wav` | gen_audio_v2.py gen_ambient()(gen_audio の元ファイルの音量調整版) | 完全一致 | 自作スクリプト | 鳴る: homeAmbience, wasteland_road | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 9 | `P/Ambience/amb_cave_air.wav` | gen_audio.py loop_noise() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 10 | `P/Ambience/amb_cave_air_n.wav` | gen_audio_v2.py gen_ambient()(gen_audio の元ファイルの音量調整版) | 完全一致 | 自作スクリプト | 鳴る: natural_cave.loop | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 11 | `P/Ambience/amb_chain.wav` | gen_audio_v2.py gen_ambient() | 乱数の並びの違いで不一致 | 自作スクリプト | 鳴る: last_corridor | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 12 | `P/Ambience/amb_chime.wav` | gen_audio_v2.py gen_ambient() | 乱数の並びの違いで不一致 | 自作スクリプト | 鳴る: sky_corridor | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 13 | `P/Ambience/amb_drip_0.wav` | gen_audio.py amb_shot() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 14 | `P/Ambience/amb_drip_0_n.wav` | gen_audio_v2.py gen_ambient()(gen_audio の元ファイルの音量調整版) | 完全一致 | 自作スクリプト | 鳴る: natural_cave | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 15 | `P/Ambience/amb_drip_1.wav` | gen_audio.py amb_shot() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 16 | `P/Ambience/amb_drip_1_n.wav` | gen_audio_v2.py gen_ambient()(gen_audio の元ファイルの音量調整版) | 完全一致 | 自作スクリプト | 鳴る: natural_cave | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 17 | `P/Ambience/amb_drone_swell.wav` | gen_audio_v2.py gen_ambient() | 乱数の並びの違いで不一致 | 自作スクリプト | 鳴る: last_corridor | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 18 | `P/Ambience/amb_far_thunder.wav` | gen_audio_v2.py gen_ambient() | 乱数の並びの違いで不一致 | 自作スクリプト | 鳴る: sky_corridor | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 19 | `P/Ambience/amb_grass_rustle.wav` | gen_audio.py amb_shot() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 20 | `P/Ambience/amb_grass_rustle_n.wav` | gen_audio_v2.py gen_ambient()(gen_audio の元ファイルの音量調整版) | 完全一致 | 自作スクリプト | 鳴る: wasteland_road | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 21 | `P/Ambience/amb_hawk.wav` | gen_audio_v2.py gen_ambient() | 乱数の並びの違いで不一致 | 自作スクリプト | 鳴る: sky_corridor, wasteland_road | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 22 | `P/Ambience/amb_home_breeze.wav` | gen_audio.py loop_noise() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 23 | `P/Ambience/amb_home_breeze_n.wav` | gen_audio_v2.py gen_ambient()(gen_audio の元ファイルの音量調整版) | 完全一致 | 自作スクリプト | 鳴る: homeAmbience.loop | なし(HOME の場面なし) | 自作 |
| 24 | `P/Ambience/amb_last_hall.wav` | gen_audio.py loop_noise() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 25 | `P/Ambience/amb_last_hall_n.wav` | gen_audio_v2.py gen_ambient()(gen_audio の元ファイルの音量調整版) | 完全一致 | 自作スクリプト | 鳴る: last_corridor.loop | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 26 | `P/Ambience/amb_rock_far.wav` | gen_audio.py amb_shot() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 27 | `P/Ambience/amb_rock_far_n.wav` | gen_audio_v2.py gen_ambient()(gen_audio の元ファイルの音量調整版) | 完全一致 | 自作スクリプト | 鳴る: last_corridor, natural_cave | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 28 | `P/Ambience/amb_sky_wind.wav` | gen_audio.py loop_noise() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 29 | `P/Ambience/amb_sky_wind_n.wav` | gen_audio_v2.py gen_ambient()(gen_audio の元ファイルの音量調整版) | 完全一致 | 自作スクリプト | 鳴る: sky_corridor.loop | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 30 | `P/Ambience/amb_speed_wind.wav` | gen_audio_v2.py gen_ambient() | 乱数の並びの違いで不一致 | 自作スクリプト | 鳴る: speedWind.speedWind | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 31 | `P/Ambience/amb_wasteland_wind.wav` | gen_audio.py loop_noise() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 32 | `P/Ambience/amb_wasteland_wind_n.wav` | gen_audio_v2.py gen_ambient()(gen_audio の元ファイルの音量調整版) | 完全一致 | 自作スクリプト | 鳴る: wasteland_road.loop | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 33 | `P/Ambience/amb_wind_whistle.wav` | gen_audio.py amb_shot() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 34 | `P/Ambience/amb_wind_whistle_n.wav` | gen_audio_v2.py gen_ambient()(gen_audio の元ファイルの音量調整版) | 完全一致 | 自作スクリプト | 鳴る: last_corridor, sky_corridor | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 35 | `P/Bgm/bgm_arena.wav` | gen_audio_v2.py gen_new_songs() → gen_audio.song() | 旋律だけ不一致(相関0.74〜0.87) | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 36 | `P/Bgm/bgm_arena_n.wav` | gen_audio_v2.py normalize_bgm()(同名の元曲の音量調整版) | 完全一致 | 自作スクリプト | 鳴る: arenaBgm.arenaBgm | なし(撮影時は BGM を消音) | 自作 |
| 37 | `P/Bgm/bgm_bonus_zone.wav` | gen_audio.py song()(gen_all) | 旋律だけ不一致(相関0.78〜0.95) | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 38 | `P/Bgm/bgm_bonus_zone_n.wav` | gen_audio_v2.py normalize_bgm()(同名の元曲の音量調整版) | 完全一致 | 自作スクリプト | 鳴る: bonusZoneBgm.bonusZoneBgm | なし(撮影時は BGM を消音) | 自作 |
| 39 | `P/Bgm/bgm_boss_death.wav` | gen_audio.py song()(gen_all) | 旋律だけ不一致(相関0.78〜0.95) | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 40 | `P/Bgm/bgm_boss_death_n.wav` | gen_audio_v2.py normalize_bgm()(同名の元曲の音量調整版) | 完全一致 | 自作スクリプト | 鳴る: bossDeath.bossDeath | なし(撮影時は BGM を消音) | 自作 |
| 41 | `P/Bgm/bgm_boss_final.wav` | gen_audio_v2.py gen_new_songs() → gen_audio.song() | 旋律だけ不一致(相関0.74〜0.87) | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 42 | `P/Bgm/bgm_boss_final_n.wav` | gen_audio_v2.py normalize_bgm()(同名の元曲の音量調整版) | 完全一致 | 自作スクリプト | 鳴る: bossFinal.bossFinal | なし(撮影時は BGM を消音) | 自作 |
| 43 | `P/Bgm/bgm_boss_normal.wav` | gen_audio.py song()(gen_all) | 旋律だけ不一致(相関0.78〜0.95) | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 44 | `P/Bgm/bgm_boss_normal_n.wav` | gen_audio_v2.py normalize_bgm()(同名の元曲の音量調整版) | 完全一致 | 自作スクリプト | 鳴る: bossNormal.bossNormal | なし(撮影時は BGM を消音) | 自作 |
| 45 | `P/Bgm/bgm_boss_special.wav` | gen_audio.py song()(gen_all) | 旋律だけ不一致(相関0.78〜0.95) | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 46 | `P/Bgm/bgm_boss_special_n.wav` | gen_audio_v2.py normalize_bgm()(同名の元曲の音量調整版) | 完全一致 | 自作スクリプト | 鳴る: bossSpecial.bossSpecial | なし(撮影時は BGM を消音) | 自作 |
| 47 | `P/Bgm/bgm_boss_strong.wav` | gen_audio.py song()(gen_all) | 旋律だけ不一致(相関0.78〜0.95) | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 48 | `P/Bgm/bgm_boss_strong_n.wav` | gen_audio_v2.py normalize_bgm()(同名の元曲の音量調整版) | 完全一致 | 自作スクリプト | 鳴る: bossStrong.bossStrong | BGM として使用 | 自作 |
| 49 | `P/Bgm/bgm_cave_early.wav` | gen_audio.py song()(gen_all) | 旋律だけ不一致(相関0.78〜0.95) | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 50 | `P/Bgm/bgm_cave_early_n.wav` | gen_audio_v2.py normalize_bgm()(同名の元曲の音量調整版) | 完全一致 | 自作スクリプト | 鳴る: natural_cave.early | なし(撮影時は BGM を消音) | 自作 |
| 51 | `P/Bgm/bgm_cave_late.wav` | gen_audio.py song()(gen_all) | 旋律だけ不一致(相関0.78〜0.95) | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 52 | `P/Bgm/bgm_cave_late_n.wav` | gen_audio_v2.py normalize_bgm()(同名の元曲の音量調整版) | 完全一致 | 自作スクリプト | 鳴る: natural_cave.late | なし(撮影時は BGM を消音) | 自作 |
| 53 | `P/Bgm/bgm_cave_middle.wav` | gen_audio.py song()(gen_all) | 旋律だけ不一致(相関0.78〜0.95) | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 54 | `P/Bgm/bgm_cave_middle_n.wav` | gen_audio_v2.py normalize_bgm()(同名の元曲の音量調整版) | 完全一致 | 自作スクリプト | 鳴る: natural_cave.middle | なし(撮影時は BGM を消音) | 自作 |
| 55 | `P/Bgm/bgm_last_early.wav` | gen_audio.py song()(gen_last_corridor) | 旋律だけ不一致(相関0.78〜0.95) | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 56 | `P/Bgm/bgm_last_early_n.wav` | gen_audio_v2.py normalize_bgm()(同名の元曲の音量調整版) | 完全一致 | 自作スクリプト | 鳴る: last_corridor.early | なし(撮影時は BGM を消音) | 自作 |
| 57 | `P/Bgm/bgm_last_late.wav` | gen_audio.py song()(gen_last_corridor) | 旋律だけ不一致(相関0.78〜0.95) | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 58 | `P/Bgm/bgm_last_late_n.wav` | gen_audio_v2.py normalize_bgm()(同名の元曲の音量調整版) | 完全一致 | 自作スクリプト | 鳴る: last_corridor.late | なし(撮影時は BGM を消音) | 自作 |
| 59 | `P/Bgm/bgm_last_middle.wav` | gen_audio.py song()(gen_last_corridor) | 旋律だけ不一致(相関0.78〜0.95) | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 60 | `P/Bgm/bgm_last_middle_n.wav` | gen_audio_v2.py normalize_bgm()(同名の元曲の音量調整版) | 完全一致 | 自作スクリプト | 鳴る: last_corridor.middle | なし(撮影時は BGM を消音) | 自作 |
| 61 | `P/Bgm/bgm_sky_early.wav` | gen_audio.py song()(gen_all) | 旋律だけ不一致(相関0.78〜0.95) | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 62 | `P/Bgm/bgm_sky_early_n.wav` | gen_audio_v2.py normalize_bgm()(同名の元曲の音量調整版) | 完全一致 | 自作スクリプト | 鳴る: sky_corridor.early | なし(撮影時は BGM を消音) | 自作 |
| 63 | `P/Bgm/bgm_sky_late.wav` | gen_audio.py song()(gen_all) | 旋律だけ不一致(相関0.78〜0.95) | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 64 | `P/Bgm/bgm_sky_late_n.wav` | gen_audio_v2.py normalize_bgm()(同名の元曲の音量調整版) | 完全一致 | 自作スクリプト | 鳴る: sky_corridor.late | なし(撮影時は BGM を消音) | 自作 |
| 65 | `P/Bgm/bgm_sky_middle.wav` | gen_audio.py song()(gen_all) | 旋律だけ不一致(相関0.78〜0.95) | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 66 | `P/Bgm/bgm_sky_middle_n.wav` | gen_audio_v2.py normalize_bgm()(同名の元曲の音量調整版) | 完全一致 | 自作スクリプト | 鳴る: sky_corridor.middle | なし(撮影時は BGM を消音) | 自作 |
| 67 | `P/Bgm/bgm_wasteland_late.wav` | gen_audio.py song()(gen_all) | 旋律だけ不一致(相関0.78〜0.95) | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 68 | `P/Bgm/bgm_wasteland_late_n.wav` | gen_audio_v2.py normalize_bgm()(同名の元曲の音量調整版) | 完全一致 | 自作スクリプト | 鳴る: wasteland_road.late | なし(撮影時は BGM を消音) | 自作 |
| 69 | `P/Bgm/bgm_wasteland_middle.wav` | gen_audio.py song()(gen_all) | 旋律だけ不一致(相関0.78〜0.95) | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 70 | `P/Bgm/bgm_wasteland_middle_n.wav` | gen_audio_v2.py normalize_bgm()(同名の元曲の音量調整版) | 完全一致 | 自作スクリプト | 鳴る: wasteland_road.middle | BGM として使用 | 自作 |
| 71 | `P/Jingle/jingle_game_over.wav` | gen_audio.py jingle() | 完全一致 | 自作スクリプト | 鳴る: gameOverJingle.gameOverJingle, musicGains.clip | なし(撮影時は BGM を消音) | 自作 |
| 72 | `P/Jingle/jingle_result.wav` | gen_audio.py jingle() | 完全一致 | 自作スクリプト | 鳴る: musicGains.clip, resultJingle.resultJingle | なし(撮影時は BGM を消音) | 自作 |
| 73 | `P/SE/arena_lose.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: ArenaLose | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 74 | `P/SE/arena_start.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: ArenaStart | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 75 | `P/SE/arena_win.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: ArenaWin | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 76 | `P/SE/atk_air.wav` | gen_audio.py gen_all() se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 77 | `P/SE/atk_air_n.wav` | gen_audio_v2.py gen_se()(gen_audio の元ファイルを読み込み音量調整) | 完全一致 | 自作スクリプト | 鳴る: AttackAir | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 78 | `P/SE/atk_blood_0.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: 武器 Blood | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 79 | `P/SE/atk_blood_1.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: 武器 Blood | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 80 | `P/SE/atk_blood_strong.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: 武器 Blood.strong | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 81 | `P/SE/atk_bow.wav` | gen_audio.py gen_all() se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 82 | `P/SE/atk_bow_0.wav` | gen_audio_v2.py gen_se()(gen_audio の atk_bow.wav を読み込み、_1 は音程違い) | 完全一致 | 自作スクリプト | 鳴る: 武器 Bow | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 83 | `P/SE/atk_bow_1.wav` | gen_audio_v2.py gen_se()(gen_audio の atk_bow.wav を読み込み、_1 は音程違い) | 完全一致 | 自作スクリプト | 鳴る: 武器 Bow | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 84 | `P/SE/atk_bow_strong.wav` | gen_audio.py gen_all() se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 85 | `P/SE/atk_bow_strong_n.wav` | gen_audio_v2.py gen_se()(gen_audio の元ファイルを読み込み音量調整) | 完全一致 | 自作スクリプト | 鳴る: 武器 Bow.strong | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 86 | `P/SE/atk_claw_0.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: 武器 Claw | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 87 | `P/SE/atk_claw_1.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: 武器 Claw | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 88 | `P/SE/atk_claw_strong.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: 武器 Claw.strong | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 89 | `P/SE/atk_down_n.wav` | gen_audio_v2.py gen_se()(gen_audio の元ファイルを読み込み音量調整) | 完全一致 | 自作スクリプト | 鳴る: AttackDown | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 90 | `P/SE/atk_down_slam.wav` | gen_audio.py gen_all() se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 91 | `P/SE/atk_dual_blade.wav` | gen_audio.py gen_all() se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 92 | `P/SE/atk_dual_blade_0.wav` | gen_audio_v2.py gen_se()(gen_audio の atk_dual_blade.wav を読み込み、_1 は音程違い) | 完全一致 | 自作スクリプト | 鳴る: 武器 DualBlade | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 93 | `P/SE/atk_dual_blade_1.wav` | gen_audio_v2.py gen_se()(gen_audio の atk_dual_blade.wav を読み込み、_1 は音程違い) | 完全一致 | 自作スクリプト | 鳴る: 武器 DualBlade | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 94 | `P/SE/atk_dual_blade_strong.wav` | gen_audio.py gen_all() se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 95 | `P/SE/atk_dual_blade_strong_n.wav` | gen_audio_v2.py gen_se()(gen_audio の元ファイルを読み込み音量調整) | 完全一致 | 自作スクリプト | 鳴る: 武器 DualBlade.strong | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 96 | `P/SE/atk_gun.wav` | gen_audio.py gen_all() se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 97 | `P/SE/atk_gun_0.wav` | gen_audio_v2.py gen_se()(gen_audio の atk_gun.wav を読み込み、_1 は音程違い) | 完全一致 | 自作スクリプト | 鳴る: 武器 Gun | 検出(一致度0.65) | 自作 |
| 98 | `P/SE/atk_gun_1.wav` | gen_audio_v2.py gen_se()(gen_audio の atk_gun.wav を読み込み、_1 は音程違い) | 完全一致 | 自作スクリプト | 鳴る: 武器 Gun | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 99 | `P/SE/atk_gun_strong.wav` | gen_audio.py gen_all() se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 100 | `P/SE/atk_gun_strong_n.wav` | gen_audio_v2.py gen_se()(gen_audio の元ファイルを読み込み音量調整) | 完全一致 | 自作スクリプト | 鳴る: 武器 Gun.strong | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 101 | `P/SE/atk_lance_0.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: 武器 Lance | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 102 | `P/SE/atk_lance_1.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: 武器 Lance | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 103 | `P/SE/atk_lance_strong.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: 武器 Lance.strong | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 104 | `P/SE/atk_magic.wav` | gen_audio.py gen_all() se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 105 | `P/SE/atk_magic_0.wav` | gen_audio_v2.py gen_se()(gen_audio の atk_magic.wav を読み込み、_1 は音程違い) | 完全一致 | 自作スクリプト | 鳴る: 武器 Magic | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 106 | `P/SE/atk_magic_1.wav` | gen_audio_v2.py gen_se()(gen_audio の atk_magic.wav を読み込み、_1 は音程違い) | 完全一致 | 自作スクリプト | 鳴る: 武器 Magic | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 107 | `P/SE/atk_magic_strong.wav` | gen_audio.py gen_all() se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 108 | `P/SE/atk_magic_strong_n.wav` | gen_audio_v2.py gen_se()(gen_audio の元ファイルを読み込み音量調整) | 完全一致 | 自作スクリプト | 鳴る: 武器 Magic.strong | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 109 | `P/SE/atk_ninja_0.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: 武器 Ninja | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 110 | `P/SE/atk_ninja_1.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: 武器 Ninja | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 111 | `P/SE/atk_ninja_strong.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: 武器 Ninja.strong | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 112 | `P/SE/atk_special.wav` | gen_audio.py gen_all() se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 113 | `P/SE/atk_special_0.wav` | gen_audio_v2.py gen_se()(gen_audio の atk_special.wav を読み込み、_1 は音程違い) | 完全一致 | 自作スクリプト | 鳴る: 武器 Special | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 114 | `P/SE/atk_special_1.wav` | gen_audio_v2.py gen_se()(gen_audio の atk_special.wav を読み込み、_1 は音程違い) | 完全一致 | 自作スクリプト | 鳴る: 武器 Special | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 115 | `P/SE/atk_special_strong.wav` | gen_audio.py gen_all() se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 116 | `P/SE/atk_special_strong_n.wav` | gen_audio_v2.py gen_se()(gen_audio の元ファイルを読み込み音量調整) | 完全一致 | 自作スクリプト | 鳴る: 武器 Special.strong | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 117 | `P/SE/atk_spirit_0.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: 武器 Spirit | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 118 | `P/SE/atk_spirit_1.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: 武器 Spirit | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 119 | `P/SE/atk_spirit_strong.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: 武器 Spirit.strong | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 120 | `P/SE/atk_strike.wav` | gen_audio.py gen_all() se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 121 | `P/SE/atk_strike_0.wav` | gen_audio_v2.py gen_se()(gen_audio の atk_strike.wav を読み込み、_1 は音程違い) | 完全一致 | 自作スクリプト | 鳴る: 武器 Strike | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 122 | `P/SE/atk_strike_1.wav` | gen_audio_v2.py gen_se()(gen_audio の atk_strike.wav を読み込み、_1 は音程違い) | 完全一致 | 自作スクリプト | 鳴る: 武器 Strike | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 123 | `P/SE/atk_strike_strong.wav` | gen_audio.py gen_all() se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 124 | `P/SE/atk_strike_strong_n.wav` | gen_audio_v2.py gen_se()(gen_audio の元ファイルを読み込み音量調整) | 完全一致 | 自作スクリプト | 鳴る: 武器 Strike.strong | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 125 | `P/SE/atk_sword_0.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: 武器 Sword | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 126 | `P/SE/atk_sword_1.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 127 | `P/SE/atk_sword_finisher.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: 武器 Sword.strong | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 128 | `P/SE/atk_sword_strong.wav` | gen_audio.py gen_all() se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 129 | `P/SE/atk_up_launch.wav` | gen_audio.py gen_all() se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 130 | `P/SE/atk_up_n.wav` | gen_audio_v2.py gen_se()(gen_audio の元ファイルを読み込み音量調整) | 完全一致 | 自作スクリプト | 鳴る: AttackUp | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 131 | `P/SE/boss_appear.wav` | gen_audio.py gen_all() se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 132 | `P/SE/boss_appear_n.wav` | gen_audio_v2.py gen_se()(gen_audio の元ファイルを読み込み音量調整) | 完全一致 | 自作スクリプト | 鳴る: BossAppear | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 133 | `P/SE/boss_attack.wav` | gen_audio.py gen_all() se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 134 | `P/SE/boss_attack_n.wav` | gen_audio_v2.py gen_se()(gen_audio の元ファイルを読み込み音量調整) | 完全一致 | 自作スクリプト | 鳴る: BossAttack | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 135 | `P/SE/boss_break.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: BossBreak | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 136 | `P/SE/boss_charge.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: BossCharge | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 137 | `P/SE/boss_collapse.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: BossCollapse | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 138 | `P/SE/boss_defeat.wav` | gen_audio.py gen_all() se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 139 | `P/SE/boss_defeat_n.wav` | gen_audio_v2.py gen_se()(gen_audio の元ファイルを読み込み音量調整) | 完全一致 | 自作スクリプト | 鳴る: BossDefeat | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 140 | `P/SE/boss_dissolve.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: BossDissolve | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 141 | `P/SE/boss_final_hit.wav` | gen_audio.py gen_all() se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 142 | `P/SE/boss_final_hit_n.wav` | gen_audio_v2.py gen_se()(gen_audio の元ファイルを読み込み音量調整) | 完全一致 | 自作スクリプト | 鳴る: BossFinalHit | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 143 | `P/SE/boss_finish_hit.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: BossFinishHit | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 144 | `P/SE/boss_hit.wav` | gen_audio.py gen_all() se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 145 | `P/SE/boss_hit_n.wav` | gen_audio_v2.py gen_se()(gen_audio の元ファイルを読み込み音量調整) | 完全一致 | 自作スクリプト | 鳴る: BossHit | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 146 | `P/SE/boss_impact_v3.wav` | gen_audio_v3.py boss_impact() | 完全一致 | 自作スクリプト | 鳴る: BossWarning | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 147 | `P/SE/boss_phase.wav` | gen_audio_v2.py gen_se()(gen_audio の boss_attack.wav を一部に使用) | 完全一致 | 自作スクリプト | 鳴る: BossPhase | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 148 | `P/SE/boss_telegraph.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: BossTelegraph | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 149 | `P/SE/boss_telegraph_heavy.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: BossTelegraphHeavy | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 150 | `P/SE/boss_ultimate.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: BossUltimate | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 151 | `P/SE/boss_warning.wav` | gen_audio.py gen_all() se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 152 | `P/SE/boss_warning_n.wav` | gen_audio_v2.py gen_se()(gen_audio の元ファイルを読み込み音量調整) | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 153 | `P/SE/bv_beast_death_v3.wav` | gen_audio_v3.py VOICES[beast](death) | 完全一致 | 自作スクリプト | 鳴る: BossDeathBeast | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 154 | `P/SE/bv_beast_growl_v3.wav` | gen_audio_v3.py VOICES[beast](growl) | 完全一致 | 自作スクリプト | 鳴る: BossGrowlBeast | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 155 | `P/SE/bv_beast_roar_v3.wav` | gen_audio_v3.py VOICES[beast](roar) | 完全一致 | 自作スクリプト | 鳴る: BossRoarBeast | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 156 | `P/SE/bv_dragon_death_v3.wav` | gen_audio_v3.py VOICES[dragon](death) | 完全一致 | 自作スクリプト | 鳴る: BossDeathDragon | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 157 | `P/SE/bv_dragon_growl_v3.wav` | gen_audio_v3.py VOICES[dragon](growl) | 完全一致 | 自作スクリプト | 鳴る: BossGrowlDragon | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 158 | `P/SE/bv_dragon_roar_v3.wav` | gen_audio_v3.py VOICES[dragon](roar) | 完全一致 | 自作スクリプト | 鳴る: BossRoarDragon | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 159 | `P/SE/bv_golem_death_v3.wav` | gen_audio_v3.py VOICES[golem](death) | 完全一致 | 自作スクリプト | 鳴る: BossDeathGolem | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 160 | `P/SE/bv_golem_growl_v3.wav` | gen_audio_v3.py VOICES[golem](growl) | 完全一致 | 自作スクリプト | 鳴る: BossGrowlGolem | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 161 | `P/SE/bv_golem_roar_v3.wav` | gen_audio_v3.py VOICES[golem](roar) | 完全一致 | 自作スクリプト | 鳴る: BossRoarGolem | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 162 | `P/SE/bv_humanoid_death_v3.wav` | gen_audio_v3.py VOICES[humanoid](death) | 完全一致 | 自作スクリプト | 鳴る: BossDeathHumanoid | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 163 | `P/SE/bv_humanoid_growl_v3.wav` | gen_audio_v3.py VOICES[humanoid](growl) | 完全一致 | 自作スクリプト | 鳴る: BossGrowlHumanoid | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 164 | `P/SE/bv_humanoid_roar_v3.wav` | gen_audio_v3.py VOICES[humanoid](roar) | 完全一致 | 自作スクリプト | 鳴る: BossRoarHumanoid | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 165 | `P/SE/bv_insect_death_v3.wav` | gen_audio_v3.py VOICES[insect](death) | 完全一致 | 自作スクリプト | 鳴る: BossDeathInsect | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 166 | `P/SE/bv_insect_growl_v3.wav` | gen_audio_v3.py VOICES[insect](growl) | 完全一致 | 自作スクリプト | 鳴る: BossGrowlInsect | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 167 | `P/SE/bv_insect_roar_v3.wav` | gen_audio_v3.py VOICES[insect](roar) | 完全一致 | 自作スクリプト | 鳴る: BossRoarInsect | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 168 | `P/SE/bv_mech_death_v3.wav` | gen_audio_v3.py VOICES[mech](death) | 完全一致 | 自作スクリプト | 鳴る: BossDeathMech | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 169 | `P/SE/bv_mech_growl_v3.wav` | gen_audio_v3.py VOICES[mech](growl) | 完全一致 | 自作スクリプト | 鳴る: BossGrowlMech | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 170 | `P/SE/bv_mech_roar_v3.wav` | gen_audio_v3.py VOICES[mech](roar) | 完全一致 | 自作スクリプト | 鳴る: BossRoarMech | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 171 | `P/SE/bv_reaper_death_v3.wav` | gen_audio_v3.py VOICES[reaper](death) | 完全一致 | 自作スクリプト | 鳴る: BossDeathReaper | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 172 | `P/SE/bv_reaper_growl_v3.wav` | gen_audio_v3.py VOICES[reaper](growl) | 完全一致 | 自作スクリプト | 鳴る: BossGrowlReaper | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 173 | `P/SE/bv_reaper_roar_v3.wav` | gen_audio_v3.py VOICES[reaper](roar) | 完全一致 | 自作スクリプト | 鳴る: BossRoarReaper | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 174 | `P/SE/card_appear.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: CardAppear | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 175 | `P/SE/card_flip.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: CardFlip | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 176 | `P/SE/card_get.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: CardGet | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 177 | `P/SE/card_hover.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: CardHover | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 178 | `P/SE/card_rare.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: CardRare | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 179 | `P/SE/combo_formed.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: ComboFormed | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 180 | `P/SE/enemy_attack.wav` | gen_audio.py gen_all() se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 181 | `P/SE/enemy_attack_big.wav` | gen_audio.py gen_all() se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 182 | `P/SE/enemy_attack_big_n.wav` | gen_audio_v2.py gen_se()(gen_audio の元ファイルを読み込み音量調整) | 完全一致 | 自作スクリプト | 鳴る: BigEnemyAttack | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 183 | `P/SE/enemy_attack_n.wav` | gen_audio_v2.py gen_se()(gen_audio の元ファイルを読み込み音量調整) | 完全一致 | 自作スクリプト | 鳴る: EnemyAttack | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 184 | `P/SE/enemy_defeat_0.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: EnemyDefeat | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 185 | `P/SE/enemy_defeat_1.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: EnemyDefeat | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 186 | `P/SE/enemy_shot.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: EnemyShot | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 187 | `P/SE/enemy_telegraph.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: EnemyTelegraph | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 188 | `P/SE/fe_activate.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: FinalEvolutionActivate | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 189 | `P/SE/fe_ready.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: FinalEvolutionReady | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 190 | `P/SE/finish_burst.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: FinishBurst | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 191 | `P/SE/finish_hit.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: FinishHit | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 192 | `P/SE/fusion_charge.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: CardFusion | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 193 | `P/SE/fusion_fail.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: FusionFail | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 194 | `P/SE/fusion_success.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: FusionSuccess | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 195 | `P/SE/hit_0.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: EnemyHit, Hit | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 196 | `P/SE/hit_1.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: Hit | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 197 | `P/SE/hit_2.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: EnemyHit, Hit | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 198 | `P/SE/hit_launch.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: HitLaunch | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 199 | `P/SE/hit_projectile_0.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: HitProjectile | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 200 | `P/SE/hit_projectile_1.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: HitProjectile | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 201 | `P/SE/hit_slam.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: SlamImpact | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 202 | `P/SE/hit_strong.wav` | gen_audio.py gen_all() se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 203 | `P/SE/hit_strong_n.wav` | gen_audio_v2.py gen_se()(gen_audio の元ファイルを読み込み音量調整) | 完全一致 | 自作スクリプト | 鳴る: StrongHit | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 204 | `P/SE/home_coin.wav` | gen_audio.py gen_all() se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 205 | `P/SE/home_coin_n.wav` | gen_audio_v2.py gen_se()(gen_audio の元ファイルを読み込み音量調整) | 完全一致 | 自作スクリプト | 鳴る: Coin | なし(HOME の場面なし) | 自作 |
| 206 | `P/SE/home_deck_edit.wav` | gen_audio.py gen_all() se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 207 | `P/SE/home_deck_edit_n.wav` | gen_audio_v2.py gen_se()(gen_audio の元ファイルを読み込み音量調整) | 完全一致 | 自作スクリプト | 鳴る: DeckEdit | なし(HOME の場面なし) | 自作 |
| 208 | `P/SE/home_door.wav` | gen_audio.py gen_all() se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 209 | `P/SE/home_door_n.wav` | gen_audio_v2.py gen_se()(gen_audio の元ファイルを読み込み音量調整) | 完全一致 | 自作スクリプト | 鳴る: Door | なし(HOME の場面なし) | 自作 |
| 210 | `P/SE/home_gacha.wav` | gen_audio.py gen_all() se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 211 | `P/SE/home_gacha_n.wav` | gen_audio_v2.py gen_se()(gen_audio の元ファイルを読み込み音量調整) | 完全一致 | 自作スクリプト | 鳴る: Gacha | なし(HOME の場面なし) | 自作 |
| 212 | `P/SE/level_up.wav` | gen_audio.py gen_all() se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 213 | `P/SE/level_up_n.wav` | gen_audio_v2.py gen_se()(gen_audio の元ファイルを読み込み音量調整) | 完全一致 | 自作スクリプト | 鳴る: LevelUp | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 214 | `P/SE/mastery_up.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: MasteryUp | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 215 | `P/SE/max_level.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: MaxLevel | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 216 | `P/SE/mile_get.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: MileGet | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 217 | `P/SE/milestone.wav` | gen_audio.py gen_all() se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 218 | `P/SE/milestone_clear.wav` | gen_audio.py gen_all() se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 219 | `P/SE/milestone_clear_n.wav` | gen_audio_v2.py gen_se()(gen_audio の元ファイルを読み込み音量調整) | 完全一致 | 自作スクリプト | 鳴る: MilestoneClear | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 220 | `P/SE/milestone_n.wav` | gen_audio_v2.py gen_se()(gen_audio の元ファイルを読み込み音量調整) | 完全一致 | 自作スクリプト | 鳴る: Milestone | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 221 | `P/SE/mv_double_jump.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 222 | `P/SE/mv_double_jump_v3.wav` | gen_audio_v3.py jump_double() | 完全一致 | 自作スクリプト | 鳴る: DoubleJump | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 223 | `P/SE/mv_jump.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 224 | `P/SE/mv_jump_v3.wav` | gen_audio_v3.py jump_light() | 完全一致 | 自作スクリプト | 鳴る: Jump | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 225 | `P/SE/mv_land.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: Land | 検出(一致度0.92) | 自作 |
| 226 | `P/SE/mv_land_heavy.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: LandHeavy | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 227 | `P/SE/net_down.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: NetDown | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 228 | `P/SE/net_join.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: NetJoin | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 229 | `P/SE/net_leave.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: NetLeave | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 230 | `P/SE/net_revive.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: NetRevive | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 231 | `P/SE/old_AttackSe1_n.wav` | gen_audio_v2.py gen_se()「Old pack」: Audio/SE/AttackSe1.wav を読み込んで音量だけ調整 | 完全一致 | ChatGPT が作った WAV(を音量調整) | 鳴る: 武器 Sword | 可能性あり(撮影した場面で鳴れば入る) | 要確認(外部素材) |
| 232 | `P/SE/old_AttackSe2_n.wav` | gen_audio_v2.py gen_se()「Old pack」: Audio/SE/AttackSe2.wav を読み込んで音量だけ調整 | 完全一致 | ChatGPT が作った WAV(を音量調整) | 鳴る: 武器 Sword | 可能性あり(撮影した場面で鳴れば入る) | 要確認(外部素材) |
| 233 | `P/SE/old_AttackSe3_n.wav` | gen_audio_v2.py gen_se()「Old pack」: Audio/SE/AttackSe3.wav を読み込んで音量だけ調整 | 完全一致 | ChatGPT が作った WAV(を音量調整) | 使っていない(ビルドに入らない) | なし | 要確認(外部素材) |
| 234 | `P/SE/old_CardConfirmSe_n.wav` | gen_audio_v2.py gen_se()「Old pack」: Audio/SE/CardConfirmSe.wav を読み込んで音量だけ調整 | 完全一致 | ChatGPT が作った WAV(を音量調整) | 使っていない(ビルドに入らない) | なし | 要確認(外部素材) |
| 235 | `P/SE/old_CardDeckAppearSe_n.wav` | gen_audio_v2.py gen_se()「Old pack」: Audio/SE/CardDeckAppearSe.wav を読み込んで音量だけ調整 | 完全一致 | ChatGPT が作った WAV(を音量調整) | 使っていない(ビルドに入らない) | なし | 要確認(外部素材) |
| 236 | `P/SE/old_CardDrawSe_n.wav` | gen_audio_v2.py gen_se()「Old pack」: Audio/SE/CardDrawSe.wav を読み込んで音量だけ調整 | 完全一致 | ChatGPT が作った WAV(を音量調整) | 使っていない(ビルドに入らない) | なし | 要確認(外部素材) |
| 237 | `P/SE/old_CardFlipSe_n.wav` | gen_audio_v2.py gen_se()「Old pack」: Audio/SE/CardFlipSe.wav を読み込んで音量だけ調整 | 完全一致 | ChatGPT が作った WAV(を音量調整) | 鳴る: CardFlip | 可能性あり(撮影した場面で鳴れば入る) | 要確認(外部素材) |
| 238 | `P/SE/old_CardSelectSe_n.wav` | gen_audio_v2.py gen_se()「Old pack」: Audio/SE/CardSelectSe.wav を読み込んで音量だけ調整 | 完全一致 | ChatGPT が作った WAV(を音量調整) | 鳴る: CardSelect | 検出(一致度0.88) | 要確認(外部素材) |
| 239 | `P/SE/old_attack_hit_n.wav` | gen_audio_v2.py gen_se()「Old pack」: Audio/SE/01_attack_hit.wav を読み込んで音量だけ調整 | 完全一致 | ChatGPT が作った WAV(を音量調整) | 使っていない(ビルドに入らない) | なし | 要確認(外部素材) |
| 240 | `P/SE/old_double_jump_n.wav` | gen_audio_v2.py gen_se()「Old pack」: Audio/SE/DoubleJumpSe.wav を読み込んで音量だけ調整 | 完全一致 | ChatGPT が作った WAV(を音量調整) | 使っていない(ビルドに入らない) | なし | 要確認(外部素材) |
| 241 | `P/SE/old_enemy_defeat_n.wav` | gen_audio_v2.py gen_se()「Old pack」: Audio/SE/03_enemy_defeat.wav を読み込んで音量だけ調整 | 完全一致 | ChatGPT が作った WAV(を音量調整) | 使っていない(ビルドに入らない) | なし | 要確認(外部素材) |
| 242 | `P/SE/old_jump_n.wav` | gen_audio_v2.py gen_se()「Old pack」: Audio/SE/JumpSe.wav を読み込んで音量だけ調整 | 完全一致 | ChatGPT が作った WAV(を音量調整) | 使っていない(ビルドに入らない) | なし | 要確認(外部素材) |
| 243 | `P/SE/old_landing_n.wav` | gen_audio_v2.py gen_se()「Old pack」: Audio/SE/05_landing.wav を読み込んで音量だけ調整 | 完全一致 | ChatGPT が作った WAV(を音量調整) | 鳴る: Land | 可能性あり(撮影した場面で鳴れば入る) | 要確認(外部素材) |
| 244 | `P/SE/old_player_damage_n.wav` | gen_audio_v2.py gen_se()「Old pack」: Audio/SE/02_player_damage.wav を読み込んで音量だけ調整 | 完全一致 | ChatGPT が作った WAV(を音量調整) | 鳴る: PlayerDamage | 検出(一致度0.98) | 要確認(外部素材) |
| 245 | `P/SE/old_player_death_n.wav` | gen_audio_v2.py gen_se()「Old pack」: Audio/SE/04_player_death.wav を読み込んで音量だけ調整 | 完全一致 | ChatGPT が作った WAV(を音量調整) | 鳴る: PlayerDeath | 可能性あり(撮影した場面で鳴れば入る) | 要確認(外部素材) |
| 246 | `P/SE/ring_burst.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: RingBurst | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 247 | `P/SE/ring_pass.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: RingPass | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 248 | `P/SE/sonic_boom.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: SonicBoom | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 249 | `P/SE/ui_cancel.wav` | gen_audio.py gen_all() se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 250 | `P/SE/ui_cancel_n.wav` | gen_audio_v2.py gen_se()(gen_audio の元ファイルを読み込み音量調整) | 完全一致 | 自作スクリプト | 鳴る: Cancel | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 251 | `P/SE/ui_character_select.wav` | gen_audio.py gen_all() se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 252 | `P/SE/ui_character_select_n.wav` | gen_audio_v2.py gen_se()(gen_audio の元ファイルを読み込み音量調整) | 完全一致 | 自作スクリプト | 鳴る: CharacterSelect | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 253 | `P/SE/ui_close.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: UiClose | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 254 | `P/SE/ui_countdown.wav` | gen_audio.py gen_all() se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 255 | `P/SE/ui_countdown_n.wav` | gen_audio_v2.py gen_se()(gen_audio の元ファイルを読み込み音量調整) | 完全一致 | 自作スクリプト | 鳴る: CountdownTick | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 256 | `P/SE/ui_decide.wav` | gen_audio.py gen_all() se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 257 | `P/SE/ui_decide_n.wav` | gen_audio_v2.py gen_se()(gen_audio の元ファイルを読み込み音量調整) | 完全一致 | 自作スクリプト | 鳴る: Decide | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 258 | `P/SE/ui_deny.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: UiDeny | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 259 | `P/SE/ui_open.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: UiOpen | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 260 | `P/SE/ui_pause.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: Pause | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 261 | `P/SE/ui_resume.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: Resume | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 262 | `P/SE/ui_run_go.wav` | gen_audio.py gen_all() se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 263 | `P/SE/ui_run_go_n.wav` | gen_audio_v2.py gen_se()(gen_audio の元ファイルを読み込み音量調整) | 完全一致 | 自作スクリプト | 鳴る: RunStart | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 264 | `P/SE/ui_screen_whoosh.wav` | gen_audio.py gen_all() se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 265 | `P/SE/ui_screen_whoosh_n.wav` | gen_audio_v2.py gen_se()(gen_audio の元ファイルを読み込み音量調整) | 完全一致 | 自作スクリプト | 鳴る: ScreenClose, ScreenOpen | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 266 | `P/SE/ui_slider.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: UiSlider | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 267 | `P/SE/ui_stage_select.wav` | gen_audio.py gen_all() se() | 完全一致 | 自作スクリプト | 使っていない(ビルドに入らない) | なし | 自作 |
| 268 | `P/SE/ui_stage_select_n.wav` | gen_audio_v2.py gen_se()(gen_audio の元ファイルを読み込み音量調整) | 完全一致 | 自作スクリプト | 鳴る: StageSelect | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 269 | `P/SE/ui_tab.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: UiTab | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 270 | `P/SE/ui_tap.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: UiTap | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 271 | `P/SE/ui_toggle.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: UiToggle | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 272 | `P/SE/ult_activate.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: UltimateActivate | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 273 | `P/SE/ult_impact.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: UltimateImpact | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 274 | `P/SE/ult_ready.wav` | gen_audio_v2.py gen_se() | 完全一致 | 自作スクリプト | 鳴る: UltimateReady | 可能性あり(撮影した場面で鳴れば入る) | 自作 |
| 275 | `SE/01_attack_hit.wav` | スクリプトでは作っていない(外部から持ち込み) | - | ChatGPT が作った WAV | ビルドに入るが通常は鳴らない(Main.unity の予備。AudioLibrary が優先) | なし | 要確認(外部素材) |
| 276 | `SE/02_player_damage.wav` | スクリプトでは作っていない(外部から持ち込み) | - | ChatGPT が作った WAV | ビルドに入るが通常は鳴らない(Main.unity の予備。AudioLibrary が優先) | なし | 要確認(外部素材) |
| 277 | `SE/03_enemy_defeat.wav` | スクリプトでは作っていない(外部から持ち込み) | - | ChatGPT が作った WAV | ビルドに入るが通常は鳴らない(Main.unity の予備。AudioLibrary が優先) | なし | 要確認(外部素材) |
| 278 | `SE/04_player_death.wav` | スクリプトでは作っていない(外部から持ち込み) | - | ChatGPT が作った WAV | ビルドに入るが通常は鳴らない(Main.unity の予備。AudioLibrary が優先) | なし | 要確認(外部素材) |
| 279 | `SE/05_landing.wav` | スクリプトでは作っていない(外部から持ち込み) | - | ChatGPT が作った WAV | ビルドに入るが通常は鳴らない(Main.unity の予備。AudioLibrary が優先) | なし | 要確認(外部素材) |
| 280 | `SE/AttackSe1.wav` | スクリプトでは作っていない(外部から持ち込み) | - | ChatGPT が作った WAV | ビルドに入るが通常は鳴らない(Main.unity の予備。AudioLibrary が優先) | なし | 要確認(外部素材) |
| 281 | `SE/AttackSe2.wav` | スクリプトでは作っていない(外部から持ち込み) | - | ChatGPT が作った WAV | ビルドに入るが通常は鳴らない(Main.unity の予備。AudioLibrary が優先) | なし | 要確認(外部素材) |
| 282 | `SE/AttackSe3.wav` | スクリプトでは作っていない(外部から持ち込み) | - | ChatGPT が作った WAV | ビルドに入るが通常は鳴らない(Main.unity の予備。AudioLibrary が優先) | なし | 要確認(外部素材) |
| 283 | `SE/CardConfirmSe.wav` | スクリプトでは作っていない(外部から持ち込み) | - | ChatGPT が作った WAV | ビルドに入るが通常は鳴らない(Main.unity の予備。AudioLibrary が優先) | なし | 要確認(外部素材) |
| 284 | `SE/CardDeckAppearSe.wav` | スクリプトでは作っていない(外部から持ち込み) | - | ChatGPT が作った WAV | ビルドに入るが通常は鳴らない(Main.unity の予備。AudioLibrary が優先) | なし | 要確認(外部素材) |
| 285 | `SE/CardDrawSe.wav` | スクリプトでは作っていない(外部から持ち込み) | - | ChatGPT が作った WAV | ビルドに入るが通常は鳴らない(Main.unity の予備。AudioLibrary が優先) | なし | 要確認(外部素材) |
| 286 | `SE/CardFlipSe.wav` | スクリプトでは作っていない(外部から持ち込み) | - | ChatGPT が作った WAV | ビルドに入るが通常は鳴らない(Main.unity の予備。AudioLibrary が優先) | なし | 要確認(外部素材) |
| 287 | `SE/CardSelectSe.wav` | スクリプトでは作っていない(外部から持ち込み) | - | ChatGPT が作った WAV | 鳴る: HOME の部屋のタップ音(GameManager.roomTapSe)。カード選択の予備も兼ねる | なし(HOME の場面なし) | 要確認(外部素材) |
| 288 | `SE/DoubleJumpSe.wav` | スクリプトでは作っていない(外部から持ち込み) | - | ChatGPT が作った WAV | ビルドに入るが通常は鳴らない(Main.unity の予備。AudioLibrary が優先) | なし | 要確認(外部素材) |
| 289 | `SE/JumpSe.wav` | スクリプトでは作っていない(外部から持ち込み) | - | ChatGPT が作った WAV | ビルドに入るが通常は鳴らない(Main.unity の予備。AudioLibrary が優先) | なし | 要確認(外部素材) |
| 290 | `SE/LandSe.wav` | スクリプトでは作っていない(外部から持ち込み) | - | ChatGPT が作った WAV | ビルドに入るが通常は鳴らない(Main.unity の予備。AudioLibrary が優先) | なし | 要確認(外部素材) |
| 291 | `TitleBgm.wav` | スクリプトでは作っていない(外部から持ち込み) | - | ChatGPT の MIDI → MuseScore 4 で書き出し(音源 MS Basic) | 鳴る: HOME の曲(+ラスダンのエンドロール) | なし(撮影時は BGM を消音) | 要確認(外部素材) |
