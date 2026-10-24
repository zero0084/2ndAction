# MIXGame ロゴ素材(C案・正式採用、2026-10-24)

デザイン: C案「三毛猫のシンボル + 虹色の MIXGame」(エステルと相談、マスター決定)。
表記は必ず **MIXGame**(M・I・X が大文字、スペースなし)。起動画面では下に小さく「by マジル旅人」。

## 素材(背景が透明の PNG)

| ファイル | 大きさ | 内容 |
|---|---|---|
| MIXGame_logo_full_transparent.png | 744x574 | 猫 + MIXGame + by マジル旅人(起動画面の形) |
| MIXGame_logo_noby_transparent.png | 744x505 | 猫 + MIXGame(作者名なし) |
| MIXGame_symbol_cat_transparent.png | 1005x689 | 猫のシンボルだけ(大きい版。アイコンなどに) |
| MIXGame_symbol_cat_small_transparent.png | 549x372 | 猫のシンボルだけ(C案の絵から切り出した、ロゴと同じ縮尺) |
| MIXGame_wordmark_transparent.png | 728x125 | 「MIXGame」の文字だけ(C案と同じ文字) |
| MIXGame_byline_transparent.png | 250x61 | 「by マジル旅人」だけ |
| MIXGame_wordmark_glossy_alt_transparent.png | 1564x341 | 参考: 大きく描き直した文字(つやと色が C案と少し違う。使う時はマスターに確認) |

## 起動画面用(ゲームに入れたもの)

| ファイル | 大きさ | 使い道 |
|---|---|---|
| MIXGame_SplashBg_Landscape.png | 1920x1080 | 横画面の起動画面(背景 #060810、ロゴは高さの5割) |
| MIXGame_SplashBg_Portrait.png | 1080x1920 | 縦画面の起動画面(背景 #060810、ロゴは高さの3割・幅の7割) |
| MIXGame_Splash_padded.png | 1353x1044 | 試した版(Unity の起動画面の「ロゴ」の欄用、透明の余白つき。今は使っていない) |

ゲームでは `Unity/2ndAction/Assets/Art/Brand/` の2枚(横/縦)を、Unity の起動画面の背景の絵として登録している
(`Assets/Editor/BrandSplashSetup.cs`、メニュー Tools/2ndAction/Apply Brand Splash)。

## 作り方

- 元の絵: `estelle_originals/`(エステルが生成。C_splash = 採用した C案の画面、C_symbol_cat = 猫だけを大きく、wordmark_glossy = 文字だけを大きく)
- 背景の透明化: `Tools/art/brand_cutout.py`(縁からつながる暗い背景だけを透明にし、内側の暗い所は残す。境目は背景色を差し引いて色を戻す)
- 文字は画像生成の C案の文字をそのまま使用(綴りが正しいことを確認済み)。フォントで組み直す必要はなかった
- 背景色: #060810(C案の絵の背景の色)
