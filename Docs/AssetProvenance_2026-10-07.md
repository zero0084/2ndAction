# One More Mile 素材・依存関係の出どころ調査(2026-10-07 時点・第3版)

> 調査と記録のみ。素材・コード・シーン・設定は一切変更していない。
> 区分: **確認済み**(根拠あり)/ **推測** / **不明**。署名付きURL・アカウントID・会話IDは載せていない。

## 0. 根拠の種類と「ビルドに入るか」の判定方法

| 記号 | 根拠 |
|---|---|
| **Z** | `C:/Users/0084k/Downloads` のファイルについて Windows が記録したダウンロード元(Zone.Identifier の HostUrl / ReferrerUrl)。全565件を集計: chatgpt.com 445、grok.com 38、assets.grok.com 20(Grok で生成した動画/GIF の保存先)、grok-sandbox.com 6、記録なし 30、その他少数(Google Drive、Gmail 添付、Apple、GitHub、MuseHub など) |
| **H** | 中身が完全一致(SHA1/MD5 同値)。Downloads とプロジェクトの間で **124件** 一致 |
| **N** | 名前・日付・会話記録上の「この元ファイル → この出力フォルダ」の対応。加工(切り抜き・透過・コマ分割)済みのため H では一致しないもの |
| **T** | Claude Code の会話記録(`C:/Users/0084k/.claude/projects/C--GameProject-1stRPG/*.jsonl`)。時刻は UTC |
| **G** | git コミット(`C:/GameProject/2ndAction`) |
| **M** | 自動メモ `memory/project_2ndaction.md`(行番号) |
| **R** | リポジトリ内のスクリプト・記録ファイル |
| **C** | 画像に埋め込まれた来歴情報(C2PA。PNG の `caBX` チャンク)。Downloads の `ChatGPT Image *.png` は **174件すべて**に C2PA があり、OpenAI / ChatGPT / gpt-image、digitalSourceType = trainedAlgorithmicMedia(AI 生成)と記録されている(`selmain_*`, `scn_*`, `arena_*` 等も同じ)。プロジェクト内の画像では **138/1346 件が OpenAI の C2PA を残している**(Art 104、Resources 22、ArtSource 12。ダウンロードしたままのもの)。切り抜き・透過などで加工した画像では消えている |
| **B** | ビルドに入るかの判定: 唯一のビルド対象シーン `Assets/Scenes/Main.unity`(`ProjectSettings/EditorBuildSettings.asset`)と、`Resources` 配下すべてを起点に、`.unity/.prefab/.asset/.mat/.controller/.anim` 内の GUID 参照をたどって集計。アプリアイコンは `ProjectSettings.asset` からの参照で判定。スクリプトだけから読み込む例外は見落としている可能性がある |

---

## 1. 画像素材の一覧

### 1-A. Grok 由来(2026-08、マスターが Grok で作り Downloads に保存した動画・GIF・静止画)

この時期(2026-08-14〜09-03)の Grok ファイルは、Claude がブラウザを操作したのではなく、マスターが自分で作って Claude との会話に添付したもの(T 0a4c9a70)。Claude が Grok に接続したのは 2026-09-13 が最初(M `reference_asset_generation_tools.md`)。プロンプトは記録上見つからない。

| 素材名 | ファイルパス | 作成・入手方法 | サービス・入手元 | 参考画像 | 確認根拠 | 利用条件・未確認事項 / ビルド |
|---|---|---|---|---|---|---|
| ドラゴン(初期ボス)待機/炎/突進 | `Art/DragonIdle`, `DragonFire`, `DragonCharge`(元コマ `*Source/`) | 動画/GIF からコマ抽出 → 透過 | Grok: `ドラゴン羽ばたき.gif`→IdleSource、`ドラゴン攻撃.gif`→FireSource(T 0a4c9a70)。`ドラゴン待機.mp4`/`ドラゴン.mp4`/`ドラゴン攻撃.mp4` → 各 Source(R `Editor/VideoFrameExtractor.cs` 80〜93行)。Z は assets.grok.com / grok.com | 不明 | Z, N, R, T | **ビルドに入る**(Source は入らない)。Grok の規約・当時のプランは記録なし |
| 機械竜 | `Art/Boss/MechanicalDragon.png` | 画像をそのまま使用 | Grok(`Mechanical Dragon.png`, Z grok.com, 09-03) | 不明 | **H** | **ビルドに入る** |
| 魔人 待機/攻撃 | `Art/MajinIdle`(24), `Art/MajinAttack`(8) | GIF をコマ分割(PowerShell) | Grok(`魔人待機.gif` / `魔人攻撃.gif`, Z grok.com) | 不明 | N(T 0a4c9a70 2026-08-28T14:43Z の抽出コマンド) | **ビルドに入る** |
| 砂煙・上昇の煙・着地/二段ジャンプの埃 | `Art/JumpDust`(←`砂煙.gif`)、`Art/AscensionSmoke`(←`煙2.gif`)、`Art/LandDust`(←`ジャンプ着地.jpg`)、`Art/DoubleJumpDust`(←`二段ジャンプ.jpg`) | GIF/画像 → コマ化 | Grok(Z) | 不明 | N(T 0a4c9a70 2026-08-22T07:50Z) | JumpDust / AscensionSmoke / DoubleJumpDust は**ビルドに入る**。LandDust と各 Source は入らない |
| 剣の斬撃 | `Art/AttackSlashFx`, `AttackSlashSource`(←`剣振り.gif`) | GIF → コマ化 | Grok(Z) | 不明 | N(T 同上) | **ビルドに入らない** |
| 初期の黒剣士 走り/ジャンプ/攻撃 | `Art/PlayerRunSource`(←`走る.mp4`)、`PlayerJumpSource`(←`ジャンプ.mp4`)、`PlayerAttackSource`(←`攻撃.mp4`)、`Art/PlayerRun` 等 | 動画 → コマ抽出 | Grok(assets.grok.com, 08-14〜15) | 不明 | N(R `VideoFrameExtractor.cs` 19〜32行) | **ビルドに入らない**(現行は `*_v1`)。`Art/PlayerRun/run_0*.png` は `Downloads/grokbot_run_preview/` と H 一致するが、そのフォルダの出どころは**不明**(Z なし、会話記録に言及なし) |
| 初期の背景 | `Art/Background/background.png`(←`背景2.jpg` を明るく加工) | 画像加工 | Grok(assets.grok.com, 08-15) | — | N(R `Editor/EnvironmentAssetProcessor.cs` 23〜24行) | **ビルドに入る** |
| 初期の地面・敵 | `Art/Ground/ground_tile.png`(←`雲の道.jpg`)、`Art/Enemy/enemy.png`(←`敵2.jpg`) | 画像加工 | Grok | — | N(R 同上 49〜77行) | **ビルドに入らない** |
| 旧TOP背景 | `Art/UI/TopBackground.jpg` | そのまま | Grok(`TOP画面.jpg`) | — | **H** | **ビルドに入らない** |
| 初期のカード能力画像の候補 | Downloads `攻撃力/ジャンプ力/ジャンプ回数/速度UP/速度DOWN/体力回復.jpg` | — | Grok(Z) | — | Z | プロジェクトとは未照合。現行アイコンは 1-B のとおり ChatGPT 由来 |

### 1-B. ChatGPT 由来(画像を chatgpt.com から保存。2026-08-28 以降)

生成の経路は2つある。
- **Claude がマスターの ChatGPT を操作**: 2026-08-30 から。マスターが「Chrome経由で画像生成AIサイトを僕が操作する」「ChatGPT」と答えたうえで開始(T0 0a4c9a70 08-30T13:53 / 14:12)。アカウント名は「kzm ohs」。2026-09-15〜16 は Chrome 拡張が切れたため、ChatGPT デスクトップアプリを画面操作して生成した(T1 972e48a6 09-15)。Claude が打ったプロンプトと添付した参照画像は会話記録に残っている(調査用の抜き出し: scratchpad `prov_early/p_0a4c.txt`, `p_972e.txt`)。
- **マスターが自分の ChatGPT で生成して添付**: ファイル名が `ChatGPT Image …` で、Z が chatgpt.com のもの。プロンプトは記録なし。マスターが「エステル」と呼ぶ ChatGPT 上の相談相手を通じて作った絵も含む(T2 c82fe072 09-08)。

**2026-09-20〜10-07 の作業**(T1、調査用の抜き出し: scratchpad `prov_late/main_chrome.tsv`, `main_user.tsv`, `refs.tsv`)
- Chrome の移動先は198回すべて chatgpt.com で、grok.com は一度もない(確認済み)。
- プロンプトは Claude が `execCommand('insertText')` で入力し、生成画像は Claude がページ上で `fetch` → ダウンロードして保存した。保存はタスクごとにマスターの許可を得た(例: 闘技場の背景 2026-10-06T09:31Z)。
- ChatGPT に添付した参照画像は、プロジェクト内の素材(`Art/UI`, `Art/WildBoss`, `Art/CaveBoss` など)か、それらを合成したもの(scratchpad の `sky/`, `selmain/`, `ld_art/`, `newchars/` など)だった。**例外は3件**で、どれもマスターが会話に添付した出どころ不明の画像: 二丁拳銃士の走り、双剣士の走り、竜騎士のデザイン画(1-B の各行を参照)。
- Chrome から送ったプロンプトに、既存作品・作家・ロゴの名前はなかった。画風の指示は「添付画像と同じ画風」「セミリアル寄りのアニメ調ダークファンタジー」のような一般的な言葉だけだった(確認済み)。

**プラン**: 2026-09-12 時点で ChatGPT アカウントのメニュー表示が「kzm ohs **Plus**」(T1 2026-09-12T15:44Z)= **その日は Plus プランだったことは確認済み**。それ以前と 2026-09-20 以降の記録にプランの記述はなく、Grok のプランも記録なし。

| 素材名 | ファイルパス | 作成・入手方法 | サービス・入手元 | 参考画像 | 確認根拠 | 利用条件・未確認事項 / ビルド |
|---|---|---|---|---|---|---|
| ビジュアル試作 Ver.1(黒剣士の参照図・各動作シート、ゴブリン・足場の参照図) | `Art/VisualStyleV1/**`(55) | **Claude が ChatGPT で生成**(英語プロンプト、参照画像の添付なし。例 "A young fantasy swordsman… dark navy/black… armor, torn red cape…")→ `Sheets/*.ps1` で白抜き・切り出し | ChatGPT(`ChatGPT Image 2026年8月30〜31日 *.png`) | なし。作風の手本はマスター添付の「Visual Style Ver.1」画像(出どころ不明, T0 08-30T13:45) | **H**(10件)、T0 08-30T14:13〜22:28 | フォルダ自体は**ほぼビルドに入らない**(7/55)。ここから作った `Player*_v1` が入る |
| 黒剣士 現行コマ(走り・ジャンプ・攻撃) | `Art/PlayerRun_v1`, `PlayerJump*_v1`, `PlayerLand_v1`, `PlayerAttack*_v1` | 上の Ver.1 シートからの切り出し | ChatGPT(Claude が生成) | — | T0 08-30T23:11(コピー記録)、SceneBuilder 3630〜3639行が読み込む | **ビルドに入る** |
| 黒剣士 上攻撃・下攻撃 | `PlayerUpAttackGround_v1`, `PlayerUpAttackAir_v1`, `PlayerDownAttack_v1`, `PlayerDownAttackLand_v1` | 経緯: 803dbee(マスター提供 `上攻撃.png` / `上攻撃空中.png` / `下攻撃.png`。コミット文は「専用手描き素材」)→ 1d7a9bc → 660d759 で戻す → **9ebdc3f(現行)**: 下攻撃はマスター添付のイラスト2枚、上攻撃は Claude が ChatGPT で生成(`PlayerRun_v1/run_00.png` とそのイラストを添付) | 803dbee の元画像は Z で chatgpt.com(09-08)。マスターの「エステルに上攻撃…を考えてもらう」発言の24分後に届いた(T2 c82fe072) → ChatGPT 生成で「手描き」ではない可能性が高い(推測)。9ebdc3f の下攻撃イラストは**作成経路未確認** | 自作の参照シート(下の行) | G、T1 09-08T12:56、09-09T14:10〜14:30 | **ビルドに入る**。下攻撃イラスト2枚は**ユーザー提供・作成経路未確認** |
| 黒剣士の参照シート/v1 コマ | リポジトリ直下 `OneMoreMile_Player_ReferenceSheet_2026-09-08.png`, `OneMoreMile_Player_v1_frames_2026-09-08.zip`(git 管理外) | **Claude が既存の `_v1` コマを Python(PIL)で並べて作ったもの**。マスターがエステルに渡すため | 自作 | — | T2 c82fe072 2026-09-08T12:31 | ビルドには入らない(Assets の外) |
| Game Feel 素材(影・埃・ヒット火花・撃破煙・雲・装飾) | `Art/Effects/{GroundShadow,RunDust,JumpPuff,LandingPuff,DoubleJumpRing,HitSpark,EnemyDeathSmoke,GrassDust,ForegroundCloudNew}.png`, `Art/Decoration/Decor*.png`, `Art/Ground/CloudPlatformNew.png` | マスター提供の素材シート | `Downloads/OneMoreMile_GameFeel/` 内の各 png は Z で chatgpt.com。zip 自体は Z なし | — | **H**(13件)、M 冒頭「ユーザー提供の Visual Feedback 用画像素材シート(ChatGPT生成)」 | ほぼ**ビルドに入る**(CloudPlatformNew は入らない) |
| 初期の雑魚敵(立ち絵) | `Art/Enemy/{Flying,Heavy,Irregular,Runner,Shooter}Enemy.png`, `RunnerRunSheet.png` | マスター提供の画像をそのまま使用 | マスターの ChatGPT(`* Enemy.png` はすべて Z で chatgpt.com, 09-03。`RunnerRunSheet` は `ChatGPT Image 2026年9月4日 19_48_10.png`) | 不明 | **H**、T0 09-03T11:07 / 09-04T10:50 | Heavy/Irregular/Runner/Shooter は**入る**。FlyingEnemy・RunnerRunSheet は入らない。プロンプトは記録なし |
| 初期の雑魚敵(走りコマ)・ゴブリン・鳥 | `Art/{GoblinRun,HeavyRun,IrregularRun,ShooterRun,WastelandBirdFlap}`(+`*Source`)、`Art/Enemy/enemy_v1.png`, `WastelandBird.png` | Claude が ChatGPT で生成。走りコマは**ChatGPT デスクトップアプリを画面操作**し、既存の立ち絵を参照画像として添付(「この画像と全く同じゴブリン…」) | ChatGPT | 自作/マスター提供の立ち絵(`enemy_v1`, `FlyingEnemy` など) | T1 09-15T09:29〜10:01、09-13T01:56、T0 08-30T14:34、G dd9a403, 3b3b102, 070ff33、M 1095〜1099行 | 走りコマ・`enemy_v1`・`WastelandBird` は**入る**。`*Source` は入らない |
| 背景(荒野・洞窟・夜の浮島) | `Art/Background/{WastelandBackground,CaveBackground,NightFloatingIsland}.png` | そのまま | ChatGPT | — | **H** | **入る** |
| 荒野の景色(昼夜サイクル) | `Resources/Scenery/Wasteland/scn_*.png`(11) | ChatGPT で同じ構図を生成 → `align2.py` で位置合わせ | ChatGPT(Downloads `scn_*_v1.png`) | 同じ構図の元画像 | **H**(10件)、M 1832行節 | **入る** |
| 荒野の地面断面・障害物・装飾 | `Art/VisualStyleV1/Ground/groundfill_wasteland.png`, `Art/Obstacles_v1`, `Art/Decoration/Wasteland_v1` | ChatGPT 生成 → 切り出し | ChatGPT(`obstacle_sheet*.png`, `decoration_sheet*.png` は Z で chatgpt.com) | — | groundfill は **H**、他は N。G 5319a24, 3ca08c9, b9dc34c | **入る** |
| 雲の足場 | `Art/Ground/CloudPlatform.png` | そのまま | ChatGPT(`雲.png`) | — | **H** | **入る** |
| タイトル・アプリアイコン・ホーム背景(マスター提供) | `Art/UI/TitleLogoV2.png`(←`新タイトル.png`)、`TopBackgroundHomeRoom.png`(←`新TOP画面.png`。後で Claude が ChatGPT でカーテンを消す編集)、`TitleLogo.png`(←`タイトル.png`)、`AppIcon.png`(←`ゲームアイコン.png`)、`TopCloud.png`(←`TOP画面の雲.png`)、`TopBackgroundNew.png`、`CardFrame.png` / `CardBack.png`(←`ChatGPT Image 2026年8月29日 12_17_43.png` から切り抜き)、`GachaMachine.png`(←`ガチャマシーン.png` の背景除去) | マスター提供の画像をそのまま、または加工 | マスターの ChatGPT(上記の元ファイルはすべて Z で chatgpt.com)。「共通デザインコンセプト」シートもマスターの ChatGPT(M 21行) | — | **H**(TitleLogoV2, TitleLogo, AppIcon, TopCloud, TopBackgroundNew)、T0 08-29T02:35 / 09-04T15:22、T1 09-18T06:11、G d78593d, b457ece | TitleLogoV2・AppIcon(ProjectSettings から参照)・TopCloud・CardFrame・CardBack・GachaMachine・TopBackgroundHomeRoom は**入る**。TitleLogo・TopBackgroundNew は**入らない**。プロンプトは記録なし |
| UI 素材(Claude が生成) | `Art/UI/OrnateFrame.png`(`CardFrame.png` を添付し9スライス用に生成)、`HomeCurtainStandalone.png`(部屋の絵の切り抜きを ChatGPT で背景除去)、`HomeCurtain.png`(不採用)、`PortraitAgingOverlay.png`、`HangerRack.png`、`PortraitBackdrop.png`、旅の地図の枠・持ち物アイコン(デスクトップアプリ) | Claude が ChatGPT で生成・編集 | ChatGPT | 自作/マスター提供の UI 画像 | **H**(PortraitAgingOverlay, HangerRack, PortraitBackdrop, HomeCurtain)、T0 09-01T03:35、T1 09-15T11:24〜11:35、09-17T03:41〜13:27、09-18T06:18 | OrnateFrame・HomeCurtainStandalone・PortraitAgingOverlay・PortraitBackdrop は**入る**。HomeCurtain・HangerRack・TravelAtlasFrame は**入らない**。`OrnateFrameSmall.png`・`AppIconForeground.png`(アイコンとして入る)の作り方は未追跡 |
| キャラの肖像(Home/旧セレクト) | `Art/UI/Characters/*_portrait.png` | マスター添付のキャラ選択画面の案(モックアップ)から切り抜き | モックアップの出どころは**不明** | — | T1 09-12T13:22、M 786行 | 黒剣士・お嬢様騎士・双剣士の分は**入る**。残り9人分(弓〜竜人)は**入らず**、作り方も未追跡 |
| 初期の剣の光(青い斬撃) | `Art/Effects/SlashArcBlue*`, `SlashUpBlue*`, `DiveTrailBlue`, `ImpactBurstBlue`, `SlashArcBlueFrames/`, `SlashUpBlueFrames/`、`SlashCrescentBlue.png` | Claude が ChatGPT で生成(SlashCrescentBlue と最初の DiveTrailBlue はマスター提供の `ChatGPT Image …`) | ChatGPT | — | T1 09-08T13:57、09-09T13:29〜14:39、09-10T00:08 / 03:56〜04:03、G 68ef619, 22fed7a | `Art/Effects` は 24/25 が**入る**(SlashCrescentBlue は入らない) |
| 肖像画の額 | `Art/UI/PortraitFrame.png` | マスター提供 | `portrait_frame.png`(Z なし, 09-16) | — | **H** | **ユーザー提供・作成経路未確認**。**ビルドに入る** |
| カード枠(★1〜5)・カード下地・タイトル板 | `Resources/CardFrames/CardFrameRarity1〜5.png`, `Art/UI/CardFrames/CardBase.png`, `CardTitlePlate.png` | そのまま | ChatGPT(`☆1〜5.png`, `カード下地.png`, `カードタイトル部分.png`) | `カード修正参考.png`(ChatGPT) | **H**、T 0a4c9a70 のコピー記録 | 入る(CardTitlePlate は入らない) |
| カードのカテゴリアイコン | `Resources/CardCategoryIcons/Icon_*.png`(7) | Claude が ChatGPT で生成 | ChatGPT | — | **H**(7件)、T1 09-17T13:56〜22:11 | **入る** |
| カードの能力アイコン(現行) | `Art/Icons/CardIcons/*.png`(91)、`CardIconSources` | Claude が ChatGPT で生成(09-15〜16 はデスクトップアプリ、09-17〜18 の9枚は Chrome)。G 51ca522 の「アイコン手描き化」は、**AI へのプロンプトで手描き風の質感を指定した**という意味で、人が描いたものではない | ChatGPT | — | **H**(9件)、T1 09-15T14:25〜15:29、09-17T22:23〜22:47、G 1aa98ce, f2fdc49, 51ca522 | `Art/Icons` 全体で 100/227 が**入る** |
| 初期の能力アイコン | `Art/Icons/Icon{JumpPower,JumpCount,Heal,AttackPower,SpeedDown,SpeedUp}` ほか、`IconGroundBreaker`〜`IconMomentum`(10) | マスター提供の画像・シートから切り出し | 元の `ジャンプ力.jpg` 等は Z で grok.com、作り直しに使った `アイコン (2).png` は chatgpt.com。10種のシートは出どころ不明 | — | T0 08-24T08:01、08-28T13:20、T1 09-11T10:14、G 4b68845 | スクリプトからは参照されない(推測: **入らない**)。`Icons/Generated/*` は C# の仮アイコン(予備) |
| 双剣士 | `Art/DualBlade*_v1` | 走り・ジャンプ・攻撃・空中・下攻撃は Claude が ChatGPT で生成(既存の `dual_blade_portrait.png` を参照)。走りは途中でマスターの6コマシート(`ChatGPT Image 2026年9月13日 17_22_35.png`)→ ChatGPT の4コマ → マスター提供 `run_01〜06.png`(352f725)→ 「提供7コマ」(bc1e716, 09-26)と変わった | ChatGPT。`run_01〜06.png` は Z で **grok-sandbox.com**(Grok の作業環境, 09-14)。現行7コマはマスターが会話に添付した画像(`images/11〜17.png`, T1 2026-09-26T06:04Z) | 既存の立ち絵 | T1 09-13T07:08〜11:57、G 534bbda, 3e8e5af, b81373a, ed9d78a, 352f725, bc1e716、Z | **入る**。現行の走り7コマは**ユーザー提供・作成経路未確認** |
| お嬢様騎士 | `Art/NobleLady*_v1` | 走り・ジャンプ・攻撃は Claude が ChatGPT で生成(マスターの元絵 `f5ac0db0-….png` と `PlayerRun_v1` を添付)。走りは Grok 動画 `generated_video (4).mp4` から ffmpeg でコマ抽出→ ChatGPT で左右反転の手直し(6c09518)→ 現行はマスター提供 `knight_run_frames_1〜3.zip`(1本の動画145コマ)から6コマを選んだもの(bf7dbbe) | ChatGPT / Grok(元絵は Z で chatgpt.com、`generated_video (4).mp4` は assets.grok.com、`knight_run_frames_*.zip` は grok.com) | マスターの元絵 | T1 09-12T16:11〜17:50、09-13T06:13〜06:29、M 864・995〜997行、G 2f42f76, 6c09518, bf7dbbe、Z | **入る** |
| 二丁拳銃士(走り以外) | `Art/Gunslinger*_v1`(Run を除く)、`Art/GunslingerBullet.png`、マズルフラッシュ | Claude が ChatGPT で生成(既存の絵を添付) | ChatGPT | 自作の絵 | G 7e63b63, d04f89c, bc1e716 | **入る** |
| **二丁拳銃士の走り8コマ** | `Art/GunslingerRun_v1/run_00〜07.png` | マスターが会話に画像8枚(`images/3〜9.png`, `10.webp`)を添付し「添付した画像の順番にして」と指示 → そのまま並べてサイズだけ揃えた | **ユーザー提供・作成経路未確認**。添付元の画像は残っていない | — | T1 2026-09-25T08:56Z、G 15f2ff5 | **入る**。**注意**: コミット 15f2ff5 の「原作/参考ゲームの走行GIF」、メモ M 1520行の「Grok生成と思われる、`grok_*.gif` を確認」は**Claude の推測で、裏付けがない**(記録に `grok_*.gif` は見当たらない)。既存ゲームから取った絵ではないか、**マスターに必ず確認** |
| 竜騎士 | `Art/Lancer*_v1` | Claude が ChatGPT で生成(シート → 切り出し) | ChatGPT(`lancer_*_sheet.png` 等, 09-26 Z) | **マスターが添付したデザイン画 `images/18.webp`**(T1 2026-09-26T07:08Z)を参照画像として ChatGPT に添付(07:11Z `lancer/ref.png`) | N、Z、G 65f9391, f0ef5a4 | **入る**。参照に使ったデザイン画は**ユーザー提供・作成経路未確認** → 生成した竜騎士の絵は、出どころ不明の参照画像をもとにしている |
| 弓/魔法/格闘/忍者、巫女/吸血鬼/竜人 | `Art/{Archer,Mage,Fighter,Ninja,Miko,Vampire,Dragonkin}*_v1` | Claude が ChatGPT で生成(シート → 切り出し)。既存キャラの立ち絵を画風の参考として添付 | ChatGPT(`omm_*_*.png` 09-27、`omm2_*_*.png`) | 自作の立ち絵(`noble_lady_portrait.png`、合成した `newchars/stand_swordsman_900.png` など) | N、Z、G 18dc55c, dcfe2ad | **入る**。`*Stand_v1` だけは入らない。`KitArt`(コードで描く技の見た目)は自作 |
| 開始/終了ポーズ | `*Start_v1`, `*Finish*_v1` | Claude が ChatGPT で生成 | ChatGPT | 各キャラの既存の絵 | G d503de4, 716c30d | **入る** |
| キャラセレクト立ち絵 v1 / v2 | v1: `ArtSource/CharacterSelectMain_v1/*`、v2: `ArtSource/CharacterSelectMain_v2/raw/selmain_*.png` → `cut.py` / `place.py` → ゲーム用 | Claude が ChatGPT で生成(1キャラ1チャット、参照画像を添付) | ChatGPT | `<id>_ref.png`(自作の現行立ち絵+カード画像) | **H**(12件)、**C**、R `ArtSource/CharacterSelectMain_v2/prompts.md` と scratchpad `selmain/prompts.md`(プロンプト全文)、G dcfe2ad, ecb221b | `ArtSource/` 自体は Assets 外なので**入らない**(加工後の画像が入る)。プロンプトに既存作品名なし |
| 荒野のボス11種 | `Art/WildBoss`(32) | Claude が ChatGPT で生成(既存のドラゴンや UI の絵を画風の参考に添付) | ChatGPT | 自作の絵(`Art/DragonIdle/idle_00.png` など) | T1 2026-09-20T14:38Z〜、G a21f6d2, b2e62ae | **入る** |
| 洞窟の地形 | `Art/Cave`(12)、`Art/Background/CaveBackground.png` | Claude が ChatGPT で生成 | ChatGPT(`cave_*_raw.png`) | 自作の絵 | **H**(CaveBackground)、G 0524211 | **入る** |
| 洞窟の雑魚5種 | `Art/Enemy/{CaveAnt,CaveBat,CaveHopper,BurrowWorm,SoldierAnt}.png`, `Cave*Run`, `BurrowWormRun`, `SoldierAntRun` | 最初は C# の仮絵(`Editor/CaveEnemyArtGenerator.cs`, G a4ba0a9)→ 2026-09-23 に Claude が ChatGPT で本番の絵に差し替え | ChatGPT | 自作の絵 | G a4ba0a9, 9cba83e | **入る** |
| 洞窟のボス11種 | `Art/CaveBoss`(33) | Claude が ChatGPT で生成 → クロマキー抽出 | ChatGPT(`pose_*.png` 09-26 Z) | 既存のボス絵 | G 14e8de8、Z | **入る** |
| 天空のボス・天空の雑魚 | `Art/SkyBoss`(27), `Art/SkyEnemy`(56) | Claude が ChatGPT で生成 | ChatGPT(`sky_*.png`, `*_sheet.png` 09-26 Z) | 自作の絵の合成(scratchpad `sky/`) | Z、G 435a3ca, 221966d | **入る**。`Editor/SkyEnemyPlaceholderArt.cs` の仮絵は予備 |
| ボーナス敵4種 | `Art/BonusEnemy`(20) | Claude が ChatGPT で生成 | ChatGPT(`golden_slime/mimic/treasure_goblin/card_fairy_sheet.png`) | 自作の絵 | G 179b22d | **入る**。mimic と treasure_goblin は、どのプロンプトで作ったかを個別に対応付けられていない |
| 死神三姉妹 | `Art/Reapers/**`, `Resources/Reapers` | Claude が ChatGPT で生成 → 切り出し | ChatGPT(`reaper_*_v1.png`) | 自作の絵(scratchpad `reaper_art/`) | **H**(元シート4件 = `Art/Reapers/Source/*`)、**C**、G bb3e299 | 加工後は**入る**。`Source/` は入らない |
| ラスダン / LAST CORRIDOR | `Art/LastCorridor/**`(26), `Resources/LastDungeon/Fx`(石板・ひび) | Claude が ChatGPT で生成 + Python で手続き生成(`Art/LastDungeon/Source/make_fx.py`, `make_cracked.py`) | ChatGPT(`ld_*.png`)/自作スクリプト | 自作の絵(scratchpad `ld_art/`) | 背景4件 **H**、R、G 69fe5f1 | **入る** |
| 荒野の景色(昼夜) | `Resources/Scenery/**` | (上の「荒野の景色」行と同じ) | ChatGPT | 同じ構図の元画像 | G b0139d8 | **入る** |
| 攻撃エフェクト・撃破粒子・銃弾・合成魔法陣 | `Resources/Effects/**`, `Art/Effects/**`(`FusionMagicCircle.png` を含む), `Art/EnemyAttack` | Claude が ChatGPT で生成 | ChatGPT(`fx_sheet_a/b.png`, `fusion_circle.png`, `gun_shot_sheet.png`, `muzzleflash_src.png`, `lancer_thrust_fx.png`) | 自作の絵 | N、Z、G f396e67, 7a14010, b2e62ae, bc1e716, 65f9391 | **入る**(`SlashCrescentBlue.png` は入らない) |
| 闘技場の背景・床 | `Resources/Arena/*` | Claude が ChatGPT で生成 → `seam2.py` で横につなぐ。保存はマスターの許可を得て実施(T1 2026-10-06T09:31Z) | ChatGPT(`arena_*_v*.png`、**C** あり) | 自作の絵(scratchpad `arenaart/`) | G 1cf7ed4、N | **入る**。`ArenaStage.cs` にはコードで描く予備の絵もある |

### 1-C. 自作(コード/スクリプトで描画)・その他

| 素材名 | ファイルパス | 作成・入手方法 | サービス・入手元 | 参考画像 | 確認根拠 | 利用条件・未確認事項 / ビルド |
|---|---|---|---|---|---|---|
| **エンドロールの巨大文字** | `Resources/LastDungeon/Glyphs/glyph_*.png`(60) | `make_glyphs.py` が **Windows 付属フォント Palatino Linotype Bold**(`C:/Windows/Fonts/palab.ttf`)で文字を描いて画像にした。YES/NO のひび割れ版は `make_cracked.py` | Windows 付属フォント(name 表: © Heidelberger Druckmaschinen AG / Linotype、Version 5.03) | — | R `Assets/Art/LastDungeon/Source/make_glyphs.py` 7行、M 1791行 | **入る**。**記録のみ: フォントを画像にしてゲームに入れてよいかの使用許諾は未確認**(変更はしていない) |
| 実行時に描く絵(光・幕・演出・仮の絵) | `HomeIdleFx.cs`, `AttackFlair.cs`, `SonicMoveFx.cs`, `UltimateFx.cs`(ULTIMATE の仮の演出), `KitArt`(新キャラの技の見た目), `CaveBossFx.cs`, `SkyBossFx.cs`, `UiBackdrop.cs`, `CardFaceArt.cs`, `ArenaStage.cs`(予備) ほか(`new Texture2D`) | コードで描く | Claude が書いたコード(G の Co-Authored-By) | — | R | 外部素材なし。カード `Icon_character_ultimate` もコードで描いたものと推測(未確認) |
| エディタ専用の仮絵生成・加工ツール | `Editor/CaveEnemyArtGenerator.cs`, `GunslingerArtGenerator.cs`, `SkyEnemyPlaceholderArt.cs`, `EnvironmentAssetProcessor.cs`, `VideoFrameExtractor.cs`, `Tools/FrameExtractorRunner.cs`, `VisualStyleV1/**/*.ps1` | 開発用 | 自作 | — | R | **入らない**(Editor 専用、または開発用) |
| `Art/square.png` | 単色の四角 | 推測: 自作 | — | — | — | 入る |

---

## 2. 画像以外(音・フォント・文章・外部コード/パッケージ)

| 素材・依存関係名 | 用途・ファイルパス | 使用・同梱状況 | 作成・入手方法 | サービス・配布元 | 確認根拠 | 利用条件 | 必要な対応・未確認事項 |
|---|---|---|---|---|---|---|---|
| タイトル曲 | `Assets/Audio/TitleBgm.wav`(HOME) | **入る**(Main.unity の AudioManager と AudioLibrary の homeBgm) | ChatGPT が作った MIDI(`ゲームTOPBGM.mid`、元の名前 `One_More_Mile_Dust_Runner_v1.mid`)を、マスターが **MuseScore Studio 4** の **MS Basic** 音源で WAV にした | MIDI: chatgpt.com(Z, 08-19)。WAV: ローカルで作成(Z なし) | **H**(`Downloads/ゲームTOPBGM.wav`)、T 0a4c9a70 2026-08-22T13:35Z「wavに変えてきたよ」、MuseScore の `recent_files.json` と `Downloads/ゲームTOPBGM.mp4/` 内の MuseScore 保存データ(元は mid、全トラック MS Basic) | ChatGPT の出力の扱いは OpenAI の規約による(当時のプランは記録なし)。MS Basic は MIT ライセンス(`C:/Program Files/MuseScore 4/sound/MS Basic_License.md`) | MS Basic の文書は「謝辞と著作権表示を派生物に含めること」を求めている → クレジット等への表記を検討 |
| 道中の曲(荒野・序盤) | `Assets/Audio/GameplayBgm.wav` | **入る** | ChatGPT の MIDI(`ゲームプレイBGM.mid`、元の名前 `One_More_Mile_Celtic_Road_Loop_v1.mid`)を WAV にした | 同上 | **H**、Z(08-21)。MuseScore で作ったことは **推測**(recent_files にあり、形式も同じだが保存データは残っていない) | 同上 | 同上 |
| HOME 曲の候補 | `Downloads/OneMoreMile_HOME_Celtic_Morning_v3_Flute_Organ.mid/.mp3` | **使っていない** | MIDI は ChatGPT(Z, 09-18)、mp3 は MuseScore で書き出し(推測) | — | Z | — | 使うかどうかをマスターに確認 |
| 効果音(攻撃1〜3・ジャンプ・二段ジャンプ・着地・カード5種・SE Pack 5種) | `Assets/Audio/SE/*.wav`(16) | **すべて入る**(Main.unity から参照)。`Docs/AudioRedesign_2026-10-06.md` 136〜141行には「4件はビルドに入らない」とあるが、参照があるので誤り | ChatGPT が作った WAV をマスターが添付 | chatgpt.com(Z, 08-24〜09-01)。SE Pack の README には「restrained SE pack」と書かれている | **H**(16件すべて: `OneMoreMile_SE_Subtle_Pack/*`, `2ndAction_*_v4/v5_quiet.wav`, `OMM_card_*.wav`, `着地.wav`)、T 0a4c9a70(08-26〜09-01 の添付) | 同上。ChatGPT がどう作ったか(推測: ChatGPT 内の Python 合成)とプロンプトは記録なし | — |
| 仮の音源(BGM / ジングル / 環境音 / 効果音) | `Assets/Audio/Placeholder/**`(249) | 170件が AudioLibrary から参照されて**入る**(Bgm 18/36、Ambience 20/33、Jingle 2/2、SE 130/178)。音量を揃える前の元ファイルなど **79件は入らない** | Python の数式合成 | 自作スクリプト `Tools/audio/gen_audio.py`(G f7e8a9a, 2026-09-29)、`gen_audio_v2.py`(G 43ad355, 2026-10-07)。Claude が作成 | R、T 972e48a6 2026-09-29T01:33Z、M 1719・2200行節 | 外部素材なし。ただし `Placeholder/SE/old_*_n.wav`(15件、うち9件が入る)は上の ChatGPT 効果音の音量を揃えたもの | 入らない79件は、ビルドに入らないことを確認済み |
| コードで作る音 | `AudioFactory.cs`(予備。実際は Main.unity に音が設定済みのため使われない)、`FusionSfx.cs`, `SkyBossFx.cs`, `BonusZone.cs`(`AudioClip.Create`) | 入る(コード) | C# で合成 | 自作 | R | 外部素材なし | — |
| 声・読み上げ(TTS) | — | **使用なし** | — | — | AudioManager の「声20」は同時に鳴らせる効果音の数(`class Voice`)。人の声はない | — | — |
| 動作確認用の録音 | `Unity/2ndAction/Builds/AudioCheck/*.wav` | 入らない(Assets の外) | ゲームの音を録音したもの | — | R | — | — |
| UI フォント | Unity 内蔵 `LegacyRuntime.ttf`(シーン内の文字1307件はすべてこの内蔵フォント。コードも `GetBuiltinResource<Font>("LegacyRuntime.ttf")`) | **入る** | Unity に同梱 | Unity | R `Editor/SceneBuilder.cs` 1540行ほか | Unity の規約の範囲内(推測)。書体名はローカルでは確認できなかった | 日本語は OS のフォントで代わりに表示される(端末で見た目が変わる)。ゲームにフォントファイルは同梱していない |
| エンドロール文字の元フォント | Windows 付属 Palatino Linotype Bold → `Resources/LastDungeon/Glyphs/*.png` | 画像として**入る** | 1-C 参照 | Windows 付属(Linotype) | R `make_glyphs.py` | **未確認** | 記録のみ。使用許諾の確認が必要(変更はしていない) |
| 外部から取り込んだ文章・翻訳 | — | **記録上なし** | クレジットの文言(`Scripts/LastDungeon/StaffCreditsData.cs`, `Resources/LastDungeon/StaffCredits.asset`: IRENE / ESTELLE / ALICIA / YOU)も自作 | — | R | — | クレジットに出す名前(実名/ペンネーム、AI の表記)を決める |
| ゲームのコード | `Assets/Scripts/**`, `Assets/Editor/**`, `Assets/Shaders/*.shader`(4) | Scripts・Shaders は入る、Editor は入らない | Claude Code(Claude Sonnet 5 / Claude Opus 5.5)が作成 | — | G(213件中209件に Co-Authored-By)。github / stackoverflow / MIT / Copyright / copied などで検索し、外から写したことを示すコメントはなし | — | — |
| 開発用スクリプト | `Tools/audio/*.py`, `ArtSource/**/*.py`, `Art/LastDungeon/Source/*.py`, `VisualStyleV1/**/*.ps1` | 入らない | 自作 | — | R | — | — |
| Netcode for GameObjects | `com.unity.netcode.gameobjects` 2.13.0 | 入る | Unity パッケージ | Unity | `Packages/manifest.json`, `packages-lock.json` | Library/PackageCache の LICENSE.md: Unity Companion License | — |
| Unity Transport | 6.5.0(依存) | 入る | 同上 | Unity | 同上 | Unity Companion License | — |
| uGUI | 2.5.0 | 入る | 同上 | Unity | 同上 | Unity Companion License | — |
| Collections / Mathematics | 6.5.0 / 1.4.0(依存) | 入る(推測) | 同上 | Unity | 同上 | Unity Companion License | — |
| Burst | 1.8.29(依存) | ビルド時に使われる(推測) | 同上 | Unity | 同上 | UCL+Unity Package Distribution License。Third Party Notices: LLVM(Apache 2.0 with LLVM Exceptions)、NCSA、Mono.Cecil(MIT)、Smash / xxHash(BSD-2)、musl | 製品に入る部分があるか確認し、必要なら第三者の表記を入れる |
| Multiplayer Center | 1.0.1 | エディタ用 | 同上 | Unity | 同上 | Unity Package Distribution License(第三者のソフトなし) | — |
| Test Framework / NUnit / Performance / Mono.Cecil | 1.7.0 / 2.1.0 / 3.5.0 / 1.11.6 | 開発用(推測: 製品に入らない) | 同上 | Unity | 同上 | UCL 等。中に NUnit(MIT)、Perfolizer(MIT)、Mono.Cecil(MIT) | — |
| Unity 本体 | 6000.5.6f1。起動ロゴ表示 ON | — | — | Unity | `ProjectSettings/ProjectVersion.txt`, `ProjectSettings.asset` | Unity の規約 | Unity のクラウドサービスはすべて無効 |
| Asset Store 素材・外部プラグイン | — | **なし** | — | — | `Assets/Plugins` なし、`.unitypackage` なし、dll / aar なし | — | — |

---

## 3. まとめ

**確認できた範囲**
- Downloads の全ファイルについてダウンロード元(Z)を記録した。そのうち124件はプロジェクトのファイルと中身が完全一致(H)。加工した素材は、会話記録・変換スクリプトに残っている「元ファイル → 出力フォルダ」の対応(N)で結び付けた。
- ビルドに入るかは、シーンと Resources からの参照をたどって判定した(ビルドに入る画像・音は計約1415件)。

**出どころ(確認済みの流れ)**
1. **Grok**(2026-08、マスターが自分で生成して添付): ドラゴン、魔人、砂煙・煙、初期の背景・機械竜。初期の黒剣士・斬撃・TOP背景も Grok 由来だが、これらは**現在ビルドに入らない**。
2. **ChatGPT**(2026-08-28 以降。マスターが自分で生成した分と、2026-08-30 以降 Claude がマスターの ChatGPT アカウントを操作して生成した分がある): 全12キャラ、敵・ボス、背景・景色、UI・カード、エフェクト、闘技場、ラスダン。2026-09-20 以降の新しい素材は、すべて Claude が ChatGPT で生成した(Grok の利用なし)。ダウンロードした元画像には OpenAI の C2PA(AI 生成の来歴情報)が入っている。
3. **音**: BGM 2曲は ChatGPT の MIDI を MuseScore 4(MS Basic 音源, MIT)で WAV にしたもの。効果音16件は ChatGPT の WAV。仮音源は Claude の Python スクリプトで合成。
4. **自作コード**: ゲームのコード、コードで描く演出・音、エンドロール文字の画像化。

**利用条件を記録上確認できたもの**
- Unity パッケージ各種(LICENSE.md)、MS Basic 音源(MIT、表示義務あり)。

**クレジット・ライセンス同梱などの対応が必要なもの**
- MS Basic(FluidR3 系)の謝辞・著作権表示。
- Burst などの Third Party Notices(製品に入る場合)。
- エンドロール文字(Palatino Linotype を画像化)の使用許諾の確認。

**利用条件や出どころが不明なもの**
- プラン: ChatGPT は **2026-09-12 時点で Plus**(アカウントメニューの表示, T1 09-12T15:44Z)。それより前の ChatGPT と、Grok のプランは**記録なし**。各サービスの当時の規約の写しもない。
- **既存作品の名前**: Claude が打った画像のプロンプトには、ゲーム・アニメ・作家・ブランドの名前は見つからなかった(2026-08-30〜10-07 の全期間を確認)。ただしマスターの企画メモでは、ゲームの発想として「チャリ走」(T0 2026-08-14T11:42)、レベルアップの選択画面の参考として「ヴァンサバ風選択UI」(Vampire Survivors、M 47・53・622行)が名前で出ている。仕組みの参考であり絵の参考ではないが、**選択画面の見た目が似すぎていないかは一度確認するとよい**。マスターが自分で Grok / ChatGPT に入れたプロンプトは記録がない。
- 2026-08 の Grok 素材と、マスターが自分で生成した ChatGPT 素材のプロンプト: 記録なし。
- **ユーザー提供・作成経路未確認**: `PortraitFrame.png`(元 `portrait_frame.png`、Z なし)、黒剣士の下攻撃イラスト2枚(9ebdc3f)、キャラ選択画面のモックアップ、`OneMoreMile_GameFeel.zip`(中の画像は chatgpt.com)、`Downloads/grokbot_run_preview/`、そして次の3件。
  - **二丁拳銃士の走り8コマ**(そのままビルドに入る): コミット 15f2ff5 とメモ M 1520 の「原作/参考ゲーム」「Grok生成と思われる」は裏付けのない推測。**既存ゲームから取った絵でないかを最優先で確認**。
  - 双剣士の現行の走り7コマ(そのままビルドに入る)。
  - 竜騎士のデザイン画(ChatGPT に参照として渡し、竜騎士の絵の元になった)。
- 2026-08 にマスターが作った Grok・ChatGPT 素材(Z でサービスは分かる)は、**どんなプロンプトで作ったかが記録にない**。
- お嬢様騎士の走り6コマ、各キャラのシートから切り出したコマ: 名前と日付での対応のみ(加工済みのため H なし)。
- ボーナス敵の mimic / treasure_goblin は、どのプロンプトで作ったかを個別に対応付けられていない(ChatGPT 由来であることは確認済み)。

**マスターへの質問**
1. ChatGPT は 2026-09-12 時点で Plus と記録されています。それより前(08月)と、Grok はどのプランでしたか。会話を残せるならそれも記録になります。
2. **二丁拳銃士の走り8コマ**(2026-09-25 に添付)は、どこで作った、または入手したものですか。既存のゲームから取ったものではありませんか。
3. 双剣士の走り7コマ(09-26)、竜騎士のデザイン画(09-26)、`portrait_frame.png`、下攻撃のイラスト2枚、キャラ選択画面の案、`grokbot_run_preview` はどこで作ったものですか。
4. 「手描き」とされた最初の上攻撃/下攻撃の絵(803dbee)は、エステル(ChatGPT)に描いてもらったものですか(記録上は chatgpt.com から保存)。
5. ゲームプレイ BGM も、タイトル曲と同じく MuseScore 4 の MS Basic で WAV にしましたか。
6. エンドロールの文字を、自由に使えるフォントで作り直すことを検討しますか(今回は記録のみで、変更はしていない)。

## 4. Steam AI 利用申告の説明文(下書き)

**日本語**
本作には、開発中に生成AIを使って作ったコンテンツが含まれます。プレイ中にAIが新しくコンテンツを生成することはありません。
- キャラクター・敵・ボス・背景・エフェクト・UI・カードなどの画像は、ChatGPT と Grok で生成した画像・動画をもとに、開発者が選別し、切り抜き・コマ分け・色調整などの加工をして作りました。
- BGM の一部は、ChatGPT で作った MIDI データを音楽ソフトで音声にしたものです。効果音の一部は ChatGPT で作った音声ファイルです。
- 仮の BGM・効果音の一部と、一部の画面演出は、AI アシスタントが書いたプログラムで合成しています。
- ゲームのプログラムは AI アシスタント(Claude)の支援を受けて書いています。

**English**
This game contains content created with generative AI during development. No content is generated by AI while the game is running.
- Images such as characters, enemies, bosses, backgrounds, effects, UI and cards are based on images and videos generated with ChatGPT and Grok. The developer selected them and edited them (cutting out, splitting into animation frames, color adjustment, and so on).
- Some background music was made by rendering MIDI data created with ChatGPT in music software. Some sound effects are audio files created with ChatGPT.
- Some placeholder music and sound effects, and some visual effects, are synthesized by programs written with an AI assistant.
- The game code was written with the help of an AI assistant (Claude).

(この下書きは記録で確認できた事実だけをもとにしている。審査に通ること、法的に問題がないことを保証するものではない。)
