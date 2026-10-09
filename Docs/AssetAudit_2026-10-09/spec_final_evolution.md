# FINAL EVOLUTION 表示・オーラ・VFX 制作仕様

> 2026-10-09 素材監査(調査のみ)。コード・素材は変更していない。
> 根拠: `Assets/Scripts/Cards/FinalEvolution.cs`(見た目 494〜540行、発動 228〜250行、ATTACK RANGE UP の斬撃波 447〜466行)、`Assets/Scripts/Cards/FinalEvolutionTuning.cs`(オーラ色 61〜81行、代表9枚 91〜99行)、`Assets/Scripts/RunBuildHud.cs` 500〜563行(HUD の枠)、`Assets/Scripts/RewardCardUI.cs` 196〜320行(LEVEL UP の3択のカード)、`Assets/Scripts/Bosses/BossBattle.cs` 100〜170行(名前の帯)、`Docs/FinalEvolution_2026-10-04.md`。
> `Resources/FinalEvolution/FinalEvolutionTuning.asset` は**存在しない**(コードの既定値で動く)。

## 0. 現状の一覧

| 場面 | 今の見た目(描いている所) | 判定 |
|---|---|---|
| READY(RUN BUILD HUD のカード枠) | 幅 6% の**1px 白テクスチャの線4本**を金色で脈動(RunBuildHud.cs 521〜534行) + 右上に**文字の★**(AWAKENED は ✦)(536〜547行) | 仮 |
| ACTIVE(同上) | 同じ線4本を橙で速く脈動 + カードの上に**黒の角丸 + 残り「8s / 1.2km」の文字**(548〜562行) | 仮 |
| USED / 再使用待ち | 右上に**文字の◆** | 仮(要確認: 第2段階で再使用になったため USED が出る場面はほぼ無い。`usesPerRun=0`) |
| LEVEL UP の3択に出る FINAL EVOLUTION のカード | 元のカードの絵 + ★5の枠 + 背後に**手続き生成の光**(`CardFaceArt.SoftGlow`、橙)+ 上端に**角丸の板と「FINAL EVOLUTION」の文字**(RewardCardUI.cs 201〜229、276〜289行)、周りを回る**手続き生成の粒4個**(`SparkDot`、234〜249行) | 仮(枠・カード絵は正式) |
| 発動の名前表示 | `BossBattleHud.Banner("FINAL EVOLUTION  <カード名>")`= **黒の半透明の板 + 文字**(BossBattle.cs 160〜168行)1.5 秒 | 仮 |
| 発動の画面の縁の光 | IMGUI で**白テクスチャの帯を画面の4辺に6段重ね**てオーラ色で 0.7 秒(FinalEvolution.cs 528〜546行) | 仮 |
| オーラ(ACTIVE 中ずっと) | プレイヤーの後ろに**手続き生成のぼかし丸**(`OneShotSpriteEffect.SoftDotSprite()`)をオーラ色で脈動(496〜525行)。分類ごとの違いは色だけ | 仮 |
| 終わり | 通常は**何も出ない**。AWAKENED のみぼかし丸が一瞬広がる(268行)。**SE も無い** | 仮(欠落) |
| 代表9枚の専用の見た目 | 下の 4 章。ATTACK RANGE UP の斬撃波は `KitArt.WhiteSprite()`(**白い四角**)を水色に塗って飛ばす | 仮 |
| SE | READY = `fe_ready`、発動 = `fe_activate`(どちらも `Audio/Placeholder/SE`、Python 生成)。終わりの SE は無い | 仮 |

## 1. デザイン基準(FINAL EVOLUTION 共通)

- 意味: 「Lv9 MAX の能力が**一時的に限界を突破**する」。ULTIMATE(キャラの必殺技)とは別物なので、**色と形で区別**する:
  - ULTIMATE = キャラ色・円形のボタン
  - FINAL EVOLUTION = **金〜橙の炎のような縁取り + 上向きに立ち上る光**(「限界の天井を突き抜ける」)。AWAKENED は**白金寄り**。
- バトル HUD は**読みやすさ最優先・重い装飾は避ける**。カード枠の外側に 6〜10% はみ出す程度まで。数字(残り時間)の上には何も重ねない。
- UI 色は既存の紺(#0B1024)/ 金(#FFD040 前後。コードの FeGold = #FFD140)/ シアン。ACTIVE の橙はコードの FeActive = #FF8C2E。
- 画風: ポップ6割 + ファンタジー4割、手描きの筆の質感、太い黒フチなし、透過 PNG。
- **白で描いてコードで着色できる素材**を基本にする(1枚で分類ごとの色に対応できる)。色付きの完成品が必要な物は個別に記載。

## 2. HUD のカード枠(RUN BUILD)

| 素材 | 用途 | サイズ / 比率 | 透過 | アニメ | 推奨デザイン |
|---|---|---|---|---|---|
| FE_ReadyFrame | READY の枠(今の線4本の置き換え) | 256×256、9-slice(縁 48px)。カード枠(角丸 正方形)より外側に 6% はみ出す前提 | 要 | 不要(脈動はコードの不透明度/拡大) | 金の細い二重線の角丸の枠。四隅に小さな**上向きの炎の飾り**(角の外へ少しはみ出す)。内側は完全に透明 |
| FE_ActiveFrame | ACTIVE の枠 | 同上 | 要 | 任意(4コマの炎の揺れ。無ければ静止画 + コードの脈動) | READY より太く明るい橙金。辺の上側から**炎が立ち上る**形。白で描いて着色にすると AWAKENED(白金)にも使える |
| FE_ReadyMark | 右上の★の置き換え | 96×96 | 要 | 不要 | 金の小さな星章(上向きの矢じりと星を合わせた形)。AWAKENED 版(白金・光の粒付き)を別に1枚 |
| FE_TimerPlate | 残り時間「8s / 1.2km」の下敷き | 192×64、9-slice(横に伸びる) | 要 | 不要 | 紺の半透明 + 橙金の細い縁。角丸。文字は描かない(コードで描く) |
| FE_UsedMark | ◆の置き換え(要確認: 出る場面がほぼ無い) | 96×96 | 要 | 不要 | 落ち着いた金の小さな菱形の紋章 |

**生成プロンプト(FE_ActiveFrame)**
```
ゲームの画面のカード枠に重ねる、正方形の角丸の枠を1枚描いてください。カードの能力が限界を突破している状態を表します。枠の内側は完全に透明。枠の線は明るいオレンジがかった金色で、上の辺から短い炎のような光がいくつか立ち上り、四隅に小さな上向きの炎の飾りが付きます。ポップ6割・ファンタジー4割、手描きの筆のタッチが残る塗り、太い黒い輪郭線は使わない、派手すぎず、小さく表示しても形が分かる。文字・数字・背景は描かない。背景は単色のマゼンタ(#FF00FF)一色。正方形 1:1。
```

## 3. LEVEL UP の3択のカード / 発動の表示

| 素材 | 用途 | サイズ / 比率 | 透過 | アニメ | 推奨デザイン |
|---|---|---|---|---|---|
| FE_ChoiceBand | 3択のカード上端の「FINAL EVOLUTION」の帯(今は角丸の板) | 1024×160(カード幅の 46% に入る横長)、9-slice 可 | 要 | 不要 | 橙金の帯、左右の端に炎の飾り、中央は文字用に平ら。AWAKENED 版(白金)を別に1枚 |
| FE_ChoiceGlow | 3択のカードの背後の光(今は SoftGlow) | 1024×1536(カードと同じ 2:3、外へ 24% はみ出す) | 要 | 不要(呼吸はコード) | カードの形に沿って**上へ強く立ち上る**炎の光。白〜橙のグラデーション。白で描いて着色でも可 |
| FE_Spark | カードの周りを回る粒(今は SparkDot) | 64×64 ×3種 | 要 | 不要 | 小さな火の粉 / 星 / 光の菱形 |
| FE_CutInBand | 発動の名前表示(今は BossBattleHud の黒い板) | 2048×256(画面幅いっぱいの横長) | 要 | 任意(左から右へ光が走る 6コマ) | 紺の半透明の帯 + 上下に橙金の細い線 + 左端に上向きの炎の紋章。文字は描かない。**BossBattleHud.Banner は他の表示(BREAK・段階移行・OVERDRIVE)とも共用**なので、FE 専用の帯にするなら呼び出しを分ける実装が必要 |
| FE_ScreenEdge | 画面の縁の光(今は IMGUI の帯6段) | 512×512、9-slice(縁 160px)、内側は透明 | 要 | 不要(0.7秒で消えるのはコード) | 画面の四辺から内側へ柔らかく消える光。四隅に向かって少し炎の筋。白で描いてオーラ色で着色 |

## 4. オーラ(ACTIVE 中、プレイヤーの後ろ)と 分類ごとの色

今は全分類が同じ「ぼかし丸」の色違い。**共通のオーラ1種(白で描く)+ 分類ごとの小さな飾り(任意)**で、数を増やさずに差を出す。

| 素材 | サイズ | 透過 | アニメ | 内容 |
|---|---|---|---|---|
| FE_Aura_Base | 512×640(縦長、キャラの全身を包む。足元が少し広い) | 要 | **要: 6〜8コマのループ**(炎が上へ揺らめく)。コマが無い時は静止画 + 今の脈動 | 白〜薄い灰の、上へ立ち上る炎状のオーラ。中心は透明寄り(キャラを隠さない)。加算合成(`Resources/Effects/SpriteGlow.mat`)を想定 |
| FE_Aura_Awakened | 同上 | 要 | 同上 | 上記 + 金の光の粒と細い光の輪。または Base を金寄りで着色 + 粒だけ別素材 |

分類(Module)とオーラ色(`FinalEvolutionTuning.AuraOf`、16進に換算)と、任意の飾りのモチーフ:

| 分類 | 色 | 飾り(任意、128×128、オーラの中を上へ流れる粒) | 代表カード |
|---|---|---|---|
| Fire | #FF591A | 火の粉 | FLAME BLADE(炎獄)、BURNING SOUL、INFERNO |
| Ice | #8CD9FF | 雪の結晶 | FROST EDGE、ICE PRISON |
| Lightning | #FFF266 | 小さな稲妻 | THUNDER STRIKE(雷神状態)、HIGH VOLTAGE |
| Wind | #99FFBF | 風の三日月 | WIND CUTTER、GALE |
| Blood / Lifesteal | #D91A33 | 血の滴の光 | VAMPIRE(血の飢え) |
| Shield / Guard / HighHp | #99BFFF | 小さな六角の盾片 | SHIELD 等 |
| Heal / Revive | #FF9926 | 羽根 | PHOENIX(不死鳥状態) |
| Movement | #73D9FF | 速度線 | SPEED UP(超高速状態)、MOMENTUM |
| Exp | #8CFF99 | 光の玉 | EXP UP(経験値覚醒) |
| Mile | #FFD933 | 金貨 | GREED(黄金暴走) |
| Enemy / BossChallenge / LowHp | #BF33F2 | 小さな角(つの)の光 | BOSS KILLER 等 |
| Air | #B3E6FF | 小さな雲 | AIR ATTACK UP 等 |
| Ground | #D9A659 | 小石 | GROUND FIGHTER 等 |
| その他(DamageBurst 等) | #FF8C33 | 光の菱形 | ATTACK UP(攻撃限界突破) は個別色 #FF7326 |

## 5. 発動 / 終わりの VFX

| 素材 | 今 | サイズ | 透過 | アニメ | 推奨デザイン |
|---|---|---|---|---|---|
| FE_ActivateBurst(発動の瞬間) | 無し(縁の光 + 名前 + 0.08秒のヒットストップのみ) | 512×512 | 要 | **要: 6コマ**(0.4秒) | キャラの足元から上へ突き抜ける光の柱 + 外へ広がる輪。白〜金。白で描いてオーラ色で着色 |
| FE_EndFade(終わり) | 無し(AWAKENED のみぼかし丸) | 512×512 | 要 | 要: 4コマ | オーラがほどけて光の粒になって消える。終わりが分かる程度に控えめ |

## 6. 代表9枚の専用の見た目

| カード / FE名 | 今の見た目(コード) | 欲しい素材 | サイズ | アニメ |
|---|---|---|---|---|
| ATTACK UP / 攻撃限界突破(最終ダメージ×1.5) | 専用の見た目なし(オーラのみ) | 命中時の大きめのヒット火花(橙金)。既存 `Art/Effects/HitSpark.png` を色替えで流用も可 | 256×256 | 3コマ |
| SPEED UP / 超高速状態(加速・自動の小攻撃・接触から守る・カメラ 8% 引き) | 残像を最大、自動の小攻撃は**見た目なし**(`ElementSystem.DealQuiet`) | 体の前の風よけの光の膜(円錐)+ 自動の小攻撃の小さな斬撃 | 512×256 / 128×128 | 4コマループ / 3コマ |
| ATTACK RANGE UP / 画面を切り裂く射程(命中時に斬撃波) | **`KitArt.WhiteSprite()` の白い四角を水色に塗って飛ばす**(FinalEvolution.cs 458行) | 三日月形の斬撃波(水色白) | 512×256(横長) | 3コマ |
| VAMPIRE / 血の飢え(溢れた分は Blood Shield) | 吸収時にぼかし丸(赤)(394行) | Blood Shield の盾のアイコン(プレイヤーの横に小さく浮かぶ)+ 吸収の赤い光の筋 | 128×128 / 256×64 | 不要 / 3コマ |
| PHOENIX / 不死鳥状態(1回だけ緊急復活) | 復活時に `Resources/Effects/burst.png` を橙で(`CardProcs.PhoenixBurst`) + 帯「FINAL EVOLUTION: REBIRTH」 | 不死鳥の翼が開く復活の演出 + 待機中の小さな羽の印 | 768×512 / 96×96 | 6コマ / 不要 |
| FLAME BLADE / 炎獄(延焼) | 延焼時にぼかし丸(橙)(439行) | 敵から敵へ飛び移る炎の弧 | 256×128 | 4コマ |
| THUNDER STRIKE / 雷神状態(連鎖+1) | 既存の落雷の見た目のまま | 連鎖が増えたことが分かる太い稲妻の分岐(既存 `Resources/Effects/skybolt.png` の流用で可) | 既存に合わせる | 既存に合わせる |
| EXP UP / 経験値覚醒(次の2,000m) | オーラのみ | EXP バーの横に小さな印(緑の光の玉) | 96×96 | 不要 |
| GREED / 黄金暴走(MILE×1.5、被ダメ×1.3) | オーラのみ | MILE 取得時の金貨の弾け + 被ダメ増のリスクの印(赤い縁の金貨) | 128×128 ×2 | 3コマ / 不要 |

## 7. SE(FINAL EVOLUTION)

| 場面 | 今 | 必要 |
|---|---|---|
| READY | `Audio/Placeholder/SE/fe_ready.wav`(生成) | 正式音(短い上昇のチャイム、0.5〜0.8秒) |
| 発動 | `fe_activate.wav`(生成) | 正式音(光の柱が立ち上る音 + 低い響き、1.0〜1.5秒) |
| 終わり | **無し** | 新規(控えめな下降音 0.4秒)+ `SeId` の追加と呼び出しの実装(`FinalEvolution.End`、253行) |
| PHOENIX の緊急復活 | 専用なし | 新規(羽ばたき + 炎) |

## 8. 組み込み(差し替えだけで済むか)

**ほぼすべて追加の実装が必要**(今は全部コードで描いており、画像の参照先が無い):
- HUD の枠・印・残り時間の下敷き: `RunBuildHud.DrawSlotOverlay`(521〜562行)の線4本/文字を `GUI.DrawTexture` へ。画像は `Resources/FinalEvolution/UI/*.png` から `Resources.Load` で読むのが簡単(シーンの作り直し不要)。
- 3択のカードの帯/光/粒: `RewardCardUI.EnsureMasteryArt`(201〜229行)で `CardFaceArt.RoundedRect()/SoftGlow()/SparkDot()` の代わりに読み込んだ Sprite を使う。**AWAKENED の表示と共用の部品**なので、FE の時だけ替える分岐が要る(`ApplyMasteryVisual` 276〜289行)。
- 名前のカットイン帯: `BossBattleHud.Banner` は BREAK/段階/OVERDRIVE と共用 → FE 専用の表示関数を足すか、Banner に帯の種類の引数を足す。
- オーラ: `FinalEvolution.UpdateAura`(497〜525行)の `SoftDotSprite()` を読み込んだ Sprite(コマ送りなら簡単なループ処理を追加)へ。`SoftDotSprite()` 自体は他の多くの演出(COMBO の Blood Aegis、ULTIMATE 以外の各種の光)でも共用しているので、**関数の中身を置き換えるのではなく呼び出し側で替える**こと。
- 発動/終わりの VFX、代表9枚の VFX: 呼び出しの場所はあるので、`OneShotSpriteEffect.CreateTweened` の引数の Sprite を差し替える形で入る(コマ送りの物は既存の `OneShotSpriteEffect.CreateAnimated`(72行)を使える。ただし CreateAnimated には着色の引数が無いので、白で描いて分類色に塗る素材をコマ送りにする場合は小さな追加実装が要る)。
- 終わりの SE: `SeId` の追加(`Scripts/Audio/AudioLibrary.cs` 158〜185行)+ `Resources/Audio/AudioLibrary.asset` への登録 + `End()` からの呼び出し。
