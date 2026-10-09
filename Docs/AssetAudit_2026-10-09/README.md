# 仮素材の総点検と本番素材への差し替え計画(2026-10-09)

> **調査と計画のみ**。コード・素材・Prefab・シーン・データは一切変更していない(画像の生成・ビルド・コミットもしていない)。
> 対象: ホーム〜リザルトまでの通常プレイの全画面(DEBUG を除く)、ステージ、敵/ボスのアニメ、VFX、SE、BGM。
> 根拠: コード(`Unity/2ndAction/Assets/Scripts`, `Assets/Editor`)、`Scenes/Main.unity` と `Resources/**` からの GUID 参照の追跡、画像の目視、既存の資料(`Docs/AssetProvenance_2026-10-07.md`, `Docs/Ultimate100_2026-10-04.md`, `Docs/FinalEvolution_2026-10-04.md`, `Docs/AudioRedesign_2026-10-06.md` ほか)。

## ファイル

| ファイル | 内容 |
|---|---|
| `asset_ledger.csv` | 素材の台帳(UTF-8 BOM、283行)。1行 = 1つの素材 / 素材の組 / 仮の使用箇所。列: ID・領域・名前・ファイルパス/コード位置・使われる場所・描画/参照箇所・今の見た目・判定・出荷版に出るか・優先度・推奨デザイン・サイズ/比率・透過・アニメ・既存素材の流用・新規制作・難易度・差し替え方法・参照方式・備考 |
| `spec_ultimate_icons.md` | ULTIMATE のアイコン13枚(共通カード1 + キャラ別発動12)の制作仕様。各キャラの技名と演出はコードから。そのまま画像生成のプロンプトに使える文つき |
| `spec_combo_icons.md` | COMBO アイコン15枚の制作仕様(名前・構成カード・効果・モチーフ・色・構図・サイズ・禁止)+ 通知の帯 |
| `spec_final_evolution.md` | FINAL EVOLUTION の HUD 枠(READY/ACTIVE)・残り表示・3択のカードの帯・名前の帯・オーラ(分類ごと)・発動/終わりの VFX・代表9枚の VFX・SE |
| `spec_other.md` | 属性カード8枚のアイコン、ULTIMATE のボタン、COMBO の VFX、バトル HUD、ホーム/メニュー UI、バトル VFX、キャラ/敵のアニメ、ステージ、**SE/BGM の不足と生成音の一覧** |

## 1. 集計

### 台帳の行数(283行)

| 判定 | 行数 | うち出荷版に出る | 意味 |
|---|---|---|---|
| **正式** | 44 | 43 | 本番の絵/音として採用済み。**保護**(差し替えない)。大量にあるため**フォルダ/組ごとに1行**にまとめた |
| **仮** | 177 | 173 | コードで描いた図形・文字だけの表示・生成音など、仮と分かるもの |
| **要確認** | 62 | 55 | 仮かどうか判断が要るもの(機能的な表示、デザインとして成立しているかもしれないもの、使い回し、記録の食い違い) |

仮/要確認(239行)の優先度: **S 36 / A 54 / B 70 / C 67 / 対応不要 12**。

### ファイル単位で見ると
- **画像**: ビルドに入る画像ファイルは 994枚(`Main.unity` と `Resources/**` からの GUID 参照で集計)。そのうち**仮の画像ファイルは 11枚だけ**(カードアイコンの仮 9枚 `Art/Icons/Generated/*`、エンドロールの石板 `slab.png`、白い四角 `square.png`)。キャラ・敵・ボス・背景・地形・障害物・カード枠・攻撃エフェクトの**絵はほぼ全部が本番の絵**。
- **仮の大半は「画像ファイル」ではなく「コードで描いている見た目」**: ULTIMATE の全演出、FINAL EVOLUTION の全表示、COMBO の HUD、属性の状態、新キャラの飛び道具、白い四角の飛ぶ斬撃、ボスの危険範囲、IMGUI の板・ボタン・記号(♥ II ★ ◆ ▼ ✓ 歯車)など。→ **差し替えには追加の実装が必要なものが多い**。
- **音**: ビルドに入る音 188本のうち **161本が Python の数式合成(仮)**、正式は 27本(うち `Audio/SE` の15本はシーンの予備で鳴らない)。**通常プレイで鳴る正式の音は SE 10種程度 + BGM 2曲**。SeId は 89種すべてに音があり無音は無いが、**COMBO の発生音・FE の終わり・属性の発生音・リングの通過音は存在しない/呼ばれていない**。

### 正式(保護)として確認したもの(一部)
キャラ12人の全コマと立ち絵(`Art/*_v1`, `Art/UI/Characters/<id>_main.png`)、カード能力アイコン91枚(`Art/Icons/CardIcons`、画風の基準)、カテゴリアイコン7種、レア度枠5種、カードの下地/枠/裏、ホームの部屋・ロゴ・カーテン・ガチャマシン・肖像の額、装飾枠(大/小)、荒野/洞窟/天空/LAST CORRIDOR/闘技場の背景・地形・障害物・装飾、荒野の昼夜の景色11枚、雑魚・ボス(荒野10/洞窟11/天空9、ドラゴン、魔人)・ボーナス敵・死神三姉妹の絵、攻撃エフェクト17種(`Resources/Effects`)、剣の斬撃のコマ、ヒット火花・埃・煙、カード UI の装飾部品(`CardFaceArt`、最終デザインとして採用済み)、BGM 2曲、正式 SE。
**メモにあった「洞窟の素材は仮」は過去の話**で、洞窟の雑魚5種・ボス11種・攻撃エフェクトは本番の絵に差し替え済みであることを実際の参照で確認した。`CaveBossFx` / `SkyBossFx.Placeholder` / 単色ブロックの予備は、全ボスに絵があるため**通常は出ない**(開発用の保険)。

## 2. 最優先(S)と次点(A)

**S(出荷版で目立つ・素材を作れば効果が大きい)**
1. **#100 ULTIMATE のカードアイコン**(ULT-01): 駒の形の記号のまま。**素材の差し替えだけ**。
2. **ULTIMATE のキャラ別発動アイコン12枚**(ULT-02〜13): 今は存在しない(キャラ色の円と文字だけ)。追加の実装が必要。
3. **COMBO アイコン15枚**(CMB-01〜15): 今は「色の角丸 + 2文字(BE/WF…)」。追加の実装が必要。
4. **属性カード8枚のアイコン**(ICN-01〜08: BURNING SOUL, INFERNO, ICE PRISON, ABSOLUTE ZERO, CHAIN LIGHTNING, THUNDER LORD, GALE, TORNADO): 丸/滴/線/×/吹き出しの記号のまま、カードの全画面に出る。**素材の差し替えだけ**。いずれも COMBO の構成カードなので COMBO と同時に作ると揃う。

**A(主なもの)**
- FINAL EVOLUTION: READY/ACTIVE の枠、3択の帯、名前の帯、オーラ、発動の VFX、白い四角の斬撃波(FE-01/02/06/09/11/13/15)
- ULTIMATE: ボタンの円/輪/光、技名のカットイン帯(ULT-14/15)、COMBO の成立の通知(CMB-16)
- バトル HUD: HP のハート(文字の♥)、一時停止「II」、ボスの HP バー(白い四角)、ボス戦の帯/BREAK、ボス撃破の光(UI-130/150/152/153/160, VFX-25)
- バトル VFX: 属性の状態(色の丸)、落雷の線、白い四角の飛ぶ斬撃(風刃/SONIC BLADE)、新キャラの飛び道具(手裏剣/御札/式神/コウモリ)、ボスの叩きつけ/魔法の床(単色の四角)、天空のボスの光線/触手(VFX-10〜13/20/21, CHR-15, BOS-13)
- メニュー: NEW RUN(30px の文字だけ)、リザルトのパネルと「Tap to Retry」、ガチャ ★4/★5 の光、入力欄(Unity 既定の灰色)、**マルチの画面一式(Unity 既定の灰色の IMGUI のまま出荷版に出る)**、デッキの分類タブ、疾走出発のパネル(装飾枠なし・絵文字🔒)、キャラ選択の3人の古いカード絵(UI-20/40/51/63/70/100/101/110/112)
- ステージ: 疾走出発の画面一式(サムネイルを並べた背景・平らな地面・手続き生成のリング)(STG-30, UI-144)
- 音: ULTIMATE/COMBO/FE/BREAK/FINISH/攻撃/UI/LEVEL UP の SE、ステージ11曲・ボス5曲・ジングル(SE-01〜04/06/07/09〜11/16/17/21/25, BGM-03〜07/10)

## 3. 段階的な差し替え計画

順番は依頼の案を基本に、調査結果で2点を変えた: **(a) 属性カード8枚のアイコンを第2段に入れた**(COMBO の構成カードで、カード画面すべてに出る仮のため)、**(b) 新キャラの飛び道具を第6段の先頭に置いた**(ULTIMATE の巫女/吸血鬼/忍者の VFX と素材を共用できる)。

| 段 | 内容 | 主な ID | 素材だけ / 追加の実装 | 参照のしかた・注意 |
|---|---|---|---|---|
| **1** | **ULTIMATE のアイコン**: 共通カード1 + キャラ別12 | ULT-01〜13 | カード1枚は**素材だけ**。キャラ別12枚は**追加の実装**(`UltimateArt.OnGUI` 646〜697行で `Resources.Load<Texture2D>("Ultimate/Icons/ult_<id>")` を描く) | カード: `Art/Icons/CardIcons/character_ultimate.png` に置き `Tools/OneMoreMile/Build Card Database`(`Resources/Cards/*.asset` の icon、Texture2D)。シーン再ビルド不要 |
| 1+ | ULTIMATE のボタンの円/輪/光、カットイン帯 | ULT-14, 15 | 追加の実装(`UltimateFx.HudDisc` の置き換え、`UltimateFxLabel` に帯) | Resources.Load(IMGUI、シーン再ビルド不要) |
| **2** | **COMBO アイコン15枚** + **属性カード8枚のアイコン** | CMB-01〜15, ICN-01〜08 | COMBO は**追加の実装**(`RunBuildHud.DrawCombos` 338〜344行、画像が無ければ今の2文字に戻す)。属性カード8枚は**素材だけ** | COMBO: `Resources/Combos/Icons/combo_<id>.png` を提案(`ComboTuning.asset` は無いので Resources から読むのが簡単)。カード: CardIcons の命名規則 |
| 2+ | COMBO の成立の通知の帯、ENHANCED の枠、AWAKENED の★ | CMB-16〜18 | 追加の実装(`ComboSystem.OnGUI` 140〜169行) | IMGUI |
| **3** | **FINAL EVOLUTION** の表示・アイコン: READY/ACTIVE の枠、印、残り表示の下敷き、3択の帯と光、名前の帯、画面の縁の光、オーラ、発動/終わりの VFX、斬撃波、代表9枚の VFX、終わりの SE | FE-01〜16, SE-08 | **ほぼ全部が追加の実装**(今はすべてコードで描画) | `Resources/FinalEvolution/UI/*` を提案。`BossBattleHud.Banner` と `CardFaceArt.SoftGlow` / `SoftDotSprite` は他の演出と**共用**なので、関数の中身でなく呼び出し側で替えること |
| **4** | **バトル HUD の仮アイコン**: HP のハート/封印/盾、一時停止、ボスの HP バー、ボス戦の帯/BREAK、BOSS APPROACHING、撃破の光、属性の状態の印、ヒット数 | UI-130, 150〜153, 160, 162, VFX-10, 25 | 追加の実装(GameManager の IMGUI、`DragonHealthBar` の SpriteRenderer、`BossBattle.cs`) | HUD は**読みやすさ優先・装飾は控えめ** |
| **5** | **ホーム/メニューの仮 UI**: ボタン一式(主/副/危険)・入力欄・タブ・スライダーの共通素材 → NEW RUN、リザルト、ガチャの光、疾走出発のパネル、歯車/機能アイコン、NEW、ランキングのメダル、マルチの画面の作り直し、キャラ/ステージ選択の背景、3人のカード絵、矢印、天空回廊のサムネイル、CONVERT の魔法陣 | UI-20〜144 | 共通素材を作り、IMGUI は実装の修正、uGUI は **SceneBuilder の修正 + シーン再ビルド**(`Tools/2ndAction/Build Prototype Scene`) | キャラ/ステージ選択・デッキ編集・LEVEL UP は uGUI で**シーンに焼き込み**。CONVERT の魔法陣(UI-72)は既存の `FusionMagicCircle.png` を指すだけで済む |
| **6** | **バトル VFX**: 新キャラの飛び道具(手裏剣/御札/式神/コウモリ/結界)、白い四角の飛ぶ斬撃(風刃/SONIC BLADE/FE)、落雷の線、属性の重ね、ボスの危険範囲・叩きつけ・予告、天空ボスの光線/触手、BREAK の破片、竜巻/Blood Aegis、障害物の破片/ひび、**ULTIMATE の技の VFX 12人**、COMBO の VFX(任意) | CHR-15, VFX-10〜31, BOS-13/14, ULT-16〜27, CMB-20 | 追加の実装(コード生成の Sprite を `Resources.Load<Sprite>("Effects/...")` 等へ)。上ルートの雑魚の火花(VFX-26)は**既存素材の配線だけ** | 既存の `Resources/Effects` と同じく Resources 方式にすると、シーン再ビルド不要 |
| **7** | **キャラ/敵のアニメ**: 4人の被弾/死亡、死神三姉妹の攻撃/被弾、新キャラの二段ジャンプ等、敵の攻撃ポーズ・精鋭の専用絵、立ち直り、ULTIMATE の専用ポーズ | CHR-10〜18, BOS-10〜12, ENM-10〜13, ULT-28 | 多くは**素材 + データの配線**(`Resources/Characters` / `Resources/Enemies` / `Resources/Reapers` の asset)。黒剣士の既定のコマは SceneBuilder 焼き込み。ULTIMATE のポーズは追加の実装 | 死神三姉妹は `ReaperSisterData` の空の配列に入れるだけで使える |
| **8** | **ステージの仮**: 疾走出発の画面一式、闘技場の床の上端、エンドロールの石板、Q の文字、洞窟の障害物の種類、(任意)奥行きの層・朝もや | STG-20〜30 | 石板は**素材だけ**(同名で上書き)。Q はスクリプト修正 + 作り直し。他は追加の実装 / SceneBuilder + シーン再ビルド | 疾走出発は画面全体が「試作」(SprintRunner.cs ヘッダー) |
| **9** | **SE/BGM の不足と生成音**: 欠落(COMBO の発生/FE の終わり/属性/リングの通過)の追加、生成音 161本の正式化(A: ULTIMATE/COMBO/FE/BREAK/FINISH/攻撃/UI/LEVEL UP、ステージ11曲・ボス5曲・ジングル)、コード合成(天空ボス/BONUS ZONE/合成)の置き換え | SE-01〜36, BGM-01〜13 | 生成音の正式化は**素材だけ**(`Resources/Audio/AudioLibrary.asset` のスロット差し替え、または同名で上書き)。欠落とコード合成の置き換えは**追加の実装**(SeId の追加・呼び出し) | AudioLibrary は Resources(シーン再ビルド不要)。シーン側の `Audio/SE` は予備で鳴らない |

段1〜4 を先に終えると、**ラン中に目に入る仮(ULTIMATE・COMBO・FE・カード・HUD)がほぼ消える**。段5 はメニュー、段6〜8 は演出の質、段9 は音。音は画像と並行して進められる。

## 4. 参照のしかた(差し替えの仕組み)

| 方式 | 対象 | 差し替えの手順 | シーン再ビルド |
|---|---|---|---|
| **カードの ScriptableObject**(`Resources/Cards/*.asset` の `icon`、Texture2D) | カードアイコン100枚 | `Art/Icons/CardIcons/<cardId>.png` に置く → `Tools/OneMoreMile/Build Card Database`(命名規則で自動採用、`Editor/CardDatabaseBuilder.cs` 22〜40行)。または asset の icon を手で替える | 不要 |
| **Resources.Load(パス指定)** | `CardFrames/*`, `CardCategoryIcons/*`, `Effects/<name>`(攻撃エフェクト17種), `Arena/*`, `Scenery/<stage>_scenery`, `LastDungeon/Glyphs/*`, `LastDungeon/Fx/slab`, `Reapers/*`, `Audio/AudioLibrary` | 同じパス・同じ名前で上書きすれば反映 | 不要 |
| **データ定義(Resources.LoadAll)** | `Resources/Characters`(キャラのコマ・立ち絵)、`Resources/Enemies`(雑魚のコマ)、`Resources/Stages`(サムネイル) | asset の配列/参照を替える(`CharacterDatabaseBuilder` 等で再生成) | キャラ選択のカルーセルとステージのサムネイルは**シーンに焼き込まれる**ので要 |
| **SceneBuilder の焼き込み**(`Editor/SceneBuilder.cs` → `Scenes/Main.unity`) | ホーム/ガチャ/肖像の絵、装飾枠、キャラ/ステージ選択・デッキ編集・合成・LEVEL UP の uGUI、地形・背景・障害物・装飾・雲、ボスの絵、黒剣士の既定のコマ、ヒット火花など | 画像の上書き(同じ GUID)なら反映されるが、**参照先を変える/新しい部品を足す時は SceneBuilder を直して `Tools/2ndAction/Build Prototype Scene`** | **要** |
| **実行時にコードで生成**(`new Texture2D` + SetPixel、`Sprite.Create`、1×1 の白、`KitArt.WhiteSprite`, `OneShotSpriteEffect.SoftDotSprite`, `UltimateFx.Make`, `SkyBossFx`, `BossFx.Block`, `UiKit.Fill/DrawGear`, `UiBackdrop`) | ULTIMATE・FE・COMBO の表示、属性、飛び道具、ボスの危険範囲、IMGUI の UI | **画像を置くだけでは変わらない**。読み込み(Resources.Load 推奨)と描画の差し替えの実装が必要 | 不要(Resources 方式なら) |
| **SpriteAtlas / Animator / AnimationClip** | **使っていない**(コマは `Sprite[]` の配列をコードで送る。Animator Controller・.anim・SpriteAtlas はプロジェクトに無い) | — | — |
| **音** | `Resources/Audio/AudioLibrary.asset`(SeId 89種・BGM・環境音・ジングル・武器系統の振り音) | スロットの AudioClip を替える。新しい SeId は `Scripts/Audio/AudioLibrary.cs` 158〜185行に追加 + asset に登録 + 呼び出し | 不要 |

**注意点(過去の落とし穴と今回の発見)**
- NPOT の画像は 2のべき乗へ伸ばされる(`nPOTScale: 1`)。カードアイコンは既存と同じ 392×589 で作り、インポート設定を揃える(RunBuildHud は `icon.width/height` で縦横比を計算する)。
- スクリプトで Sprite 化する時は `spriteImportMode` も設定する。
- Editor の仮絵生成ツール(`Editor/SkyEnemyPlaceholderArt.cs`, `GunslingerArtGenerator.cs`, `CaveEnemyArtGenerator.cs`)は**ファイルが無い時だけ影絵を書く**。天空/ボーナスの敵のコマ数やポーズを増やすと、次のビルドで**黙って仮の影絵が作られる**ので、素材を先に置いてからビルドする。
- `CardFrames/CardTitlePlate.png` は読み込まれているが使われていない。`Art/UI` の旧版(TitleLogo, TopBackground*, HomeCurtain, HangerRack, TravelAtlasFrame)も未使用。
- 不具合の発見(記録のみ・未修正): エンドロールの `glyph_Q.png` の中身が「?」(STG-27)。

## 5. 正式/仮の判断の要点(要確認の扱い)

- **正式として保護**: 採用済みのキャラの立ち絵・カード枠・背景・敵のデザイン・カード能力アイコン91枚・カード UI の装飾部品(`CardFaceArt`、コード生成だが最終デザインとして採用済み)。
- **仮**: コードの図形で「絵の代わり」をしているもの、文字/記号だけのアイコン、Unity 既定の見た目、Python の生成音、コメントやツールチップ自身が「仮」「最終演出ではない」「試作」「正式アイコンは後で」と書いているもの。
- **要確認**(62行): 機能的な表示として成立しているもの(パッドのフォーカス枠、画面遷移、HUD の板、FINISH の粒)、1枚絵の使い回し(新キャラのジャンプ系、洞窟の雑魚の走り、ゴブリン精鋭の色違い)、判断が要る記録(ビルド番号の表示、LAST CORRIDOR の仮名、テスト用の Resources、エンドロールの名前とフォントの使用許諾)など。マスターの判断で正式/仮に振り分ける。
- **開発用(出荷版に出ない)**: 未使用の仮アイコン78枚、未使用の生成音79本、`AudioFactory`、ボスの予備の絵、機械竜(旧日程)、持ち物アイコンの色の予備、Editor の仮絵ツール、`*Source` / 旧版のコマ。

## 6. 画風の基準(全仕様書の共通)

カードアイコンの現行の方向性: **ポップ6割 + ファンタジー4割**、手描きの筆の質感、鮮やかだが派手すぎない、**小さく表示しても読めるシルエット**、立体感はほどほど、**太すぎる黒い輪郭線は避ける**、**主役1つ + 補助のエフェクト**、**透過 PNG** が基本。
UI は既存の**紺 / 金 / シアン**。バトル HUD は**読みやすさ優先**で重い装飾を避ける。
