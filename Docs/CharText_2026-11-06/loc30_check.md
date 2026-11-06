# キャラ選択 30 言語の確認(2026-11-07)

対象: ID 10617〜10642 の訳を入れた後の キャラ選択(30 言語 × 14 人 × 横 1920x1080 / 縦 608x1080)。開発版を `-memSave -qaCharSelect <dir> -csLangs <30言語>` で1回ずつ(横・縦)。
撮影: `C:\Users\0084k\AppData\Local\Temp\claude\1962f1340ffd9df2\972e48a6-cec8-40d5-8275-62f59fbcb3de\scratchpad\ct41\shots_all\{ls_1920x1080,pt_608x1080}`(各 420 枚、`qa_charselect.txt` に [txt] 行)。どちらも ALL CHECKS PASSED・例外 0。

## 直す前(訳を入れた直後)
- スクロール: 横 11 件(ビルマ語 5・タミル語 6)、縦 26 件(ビルマ語 10・タミル語 11・ms/fil/pt-BR/ru/sw 各 1)。
- 1語だけの行(訳の1行が幅を超えて、その行の最後の語だけが次の行へ落ちる): 縦を中心に 182 件(id/ms/fil/sw/ru/uk/de/fr/es/it/pt-BR/bn/ta/vi/cs/ro/tr/km)。
- 名前の欠け(縦、2行目が切れる): fil お嬢様/竜騎士/巫女、ta 竜騎士/格闘家/竜人、ru 竜騎士 の 7 件。

## 直したこと(コード側、Assets/Scripts/CharacterSelectUI.FlavorFit.cs 新規 + CharacterSelectUI.cs)
1. 紹介文: 書いた改行のまま収まる時はそのまま(日本語・英語・中国語・韓国語などは変化なし)。収まらない行がある時、単語の間に空白を入れる言語(右から左・タイ/ラオ/クメール/ビルマ/日本語/中国語を除く)は改行を空白にして1つの段落として流す。
2. それでも高さ(170px)を超える時だけ、紹介文の文字を小さくする(22 → 最小 18)。
3. 名前・一行: 幅に入らない時だけ文字を小さくする(名前 38 → 最小 26、一行 20 → 最小 16)。
4. 向きが変わった時も測り直す(ApplyOrientationLayout の最後)。

## 直した後(最終)

表の見方: つないだ / 行の折り返し(つながない言語) / スクロール / 一行・型ラベルのはみ出し(OVER) / 小さくした紹介文 / 段落の最後が1語 / 紹介文の最大の高さ(枠 170px)

| 言語 | 横: つないだ / 行の折り返し / スクロール / 一行・型ラベルのはみ出し / 小さい字 / 最後の1語 / 最大の高さ | 縦: 同じ | 件数 |
|---|---|---|---|
| ja | 0 / 0 / 0 / 0 / 0 / 0 / 150px | 0 / 0 / 0 / 0 / 0 / 0 / 149px | 0 |
| en | 0 / 0 / 0 / 0 / 0 / 0 / 125px | 0 / 0 / 0 / 0 / 0 / 0 / 124px | 0 |
| zh-Hans | 0 / 0 / 0 / 0 / 0 / 0 / 125px | 0 / 0 / 0 / 0 / 0 / 0 / 124px | 0 |
| zh-Hant | 0 / 0 / 0 / 0 / 0 / 0 / 125px | 0 / 0 / 0 / 0 / 0 / 0 / 124px | 0 |
| ko | 0 / 0 / 0 / 0 / 0 / 0 / 125px | 0 / 0 / 0 / 0 / 0 / 0 / 124px | 0 |
| id | 1 / 0 / 0 / 0 / 0 / 1 / 125px | 8 / 0 / 0 / 0 / 0 / 2 / 124px | 3 |
| ms | 2 / 0 / 0 / 1 / 0 / 2 / 125px | 11 / 0 / 0 / 1 / 0 / 2 / 124px | 6 |
| vi | 0 / 0 / 0 / 0 / 0 / 0 / 125px | 3 / 0 / 0 / 0 / 0 / 0 / 124px | 0 |
| th | 0 / 0 / 0 / 0 / 0 / 0 / 125px | 0 / 0 / 0 / 0 / 0 / 0 / 124px | 0 |
| fil | 1 / 0 / 0 / 1 / 0 / 0 / 125px | 13 / 0 / 0 / 1 / 0 / 4 / 149px | 9 |
| my | 0 / 1 / 0 / 0 / 5 / 0 / 160px | 0 / 6 / 0 / 0 / 10 / 0 / 162px | 15 |
| km | 0 / 0 / 0 / 0 / 0 / 0 / 140px | 0 / 2 / 0 / 0 / 0 / 0 / 137px | 1 |
| lo | 0 / 0 / 0 / 0 / 0 / 0 / 140px | 0 / 0 / 0 / 0 / 0 / 0 / 137px | 0 |
| hi | 0 / 0 / 0 / 0 / 0 / 0 / 125px | 0 / 0 / 0 / 0 / 0 / 0 / 124px | 0 |
| bn | 0 / 0 / 0 / 0 / 0 / 0 / 125px | 5 / 0 / 0 / 0 / 0 / 1 / 124px | 1 |
| ta | 14 / 0 / 0 / 1 / 0 / 2 / 150px | 14 / 0 / 0 / 1 / 1 / 3 / 149px | 11 |
| fr | 0 / 0 / 0 / 0 / 0 / 0 / 125px | 5 / 0 / 0 / 0 / 0 / 1 / 124px | 1 |
| de | 0 / 0 / 0 / 0 / 0 / 0 / 125px | 4 / 0 / 0 / 0 / 0 / 2 / 124px | 2 |
| es | 0 / 0 / 0 / 0 / 0 / 0 / 125px | 3 / 0 / 0 / 0 / 0 / 2 / 124px | 2 |
| pt-BR | 0 / 0 / 0 / 0 / 0 / 0 / 125px | 4 / 0 / 0 / 0 / 0 / 2 / 124px | 2 |
| it | 0 / 0 / 0 / 0 / 0 / 0 / 125px | 5 / 0 / 0 / 0 / 0 / 0 / 124px | 0 |
| pl | 0 / 0 / 0 / 1 / 0 / 0 / 125px | 2 / 0 / 0 / 1 / 0 / 0 / 124px | 2 |
| uk | 0 / 0 / 0 / 0 / 0 / 0 / 125px | 6 / 0 / 0 / 0 / 0 / 2 / 124px | 2 |
| ru | 0 / 0 / 0 / 1 / 0 / 0 / 125px | 8 / 0 / 0 / 1 / 0 / 3 / 149px | 6 |
| tr | 1 / 0 / 0 / 0 / 0 / 0 / 125px | 1 / 0 / 0 / 0 / 0 / 0 / 124px | 0 |
| cs | 0 / 0 / 0 / 0 / 0 / 0 / 125px | 1 / 0 / 0 / 0 / 0 / 0 / 124px | 0 |
| ro | 0 / 0 / 0 / 0 / 0 / 0 / 125px | 1 / 0 / 0 / 0 / 0 / 0 / 124px | 0 |
| ar | 0 / 0 / 0 / 0 / 0 / 0 / 125px | 0 / 0 / 0 / 0 / 0 / 0 / 124px | 0 |
| fa | 0 / 0 / 0 / 0 / 0 / 0 / 125px | 0 / 0 / 0 / 0 / 0 / 0 / 124px | 0 |
| sw | 1 / 0 / 0 / 0 / 0 / 0 / 125px | 11 / 0 / 0 / 0 / 0 / 5 / 149px | 5 |

- スクロール: 0(全言語・両方の向き)。名前・一行の欠け: 0(fil/ta/ru の 7 件は 35〜37pt に縮小)。
- 残り(問題の一覧):
  1. **km(クメール語)ルカ・縦**: 4行目「ហើយលោតខ្ពស់ដោយគ្មានអាវុធឬពាសដែក។」が空白の無い長い行で、語の途中「ពាសដែក」で切れて最後の行が「ដក។」だけになる。案(要レビュー): 語の区切りに空白を入れる/少し短くする 例「ហើយលោតខ្ពស់ គ្មានអាវុធ ឬពាសដែក។」(行数 4 のまま)。
  2. **my(ビルマ語)**: 字が高いので紹介文を 19〜21pt に縮小(横 5 人・縦 10 人)。読めるが他言語より小さい。気になるなら各行を短く(4 行以内・1 行 25 字程度)。
  3. **型ラベル NOBLE KNIGHT の訳がカードの小さな字の枠(186px)より少し広い**: ms「KESATRIA BANGSAWAN」191 / fil「MARANGAL NA KABALYERO」221〜224 / ta 188〜189 / pl「SZLACHETNA RYCERKA」193〜195 / ru「БЛАГОРОДНЫЙ РЫЦАРЬ」199〜200。撮影ではカードの板の中に収まって見える(欠けていない)。今回変えていない既存の訳。幅の差が大きいのは fil(+38px)で、短い訳にするなら要レビュー。
  4. **段落の最後が1語だけ**(つないだ段落の自然な終わり。欠けやスクロールではない): 下の一覧の WIDOW 行(id/ms/fil/bn/ta/fr/de/es/pt-BR/uk/ru/sw、計 35 件)。気になる物は訳を 1〜2 語短くすれば消える。
- 既存の文字の描き方の限界(今回の文に限らない、ゲーム全体): クメール語は下に付く子音(្)と前に書く母音の一部(ើ ឿ ៀ)が形にならない/ヒンディー語・ベンガル語は結合文字の一部で ्/্ が見える(IndicShaper の冒頭に記載の既知の限界)。アラビア語/ペルシャ語は行の順・字形は正しいが、段落は左寄せ(ゲーム全体と同じ)。タイ語・ラオス語・タミル語は問題なし。

## 一覧(最終)

id ls gunslinger WIDOW last line "musuh." (joined paragraph, 4 lines)
id pt swordsman WIDOW last line "atas." (joined paragraph, 5 lines)
id pt miko WIDOW last line "belakang." (joined paragraph, 5 lines)
ms ls noble_lady cardrole OVER w 191/186 fs 16 OVER "KESATRIA BANGSAWAN"
ms ls gunslinger WIDOW last line "musuh." (joined paragraph, 4 lines)
ms ls archer WIDOW last line "cas." (joined paragraph, 5 lines)
ms pt noble_lady cardrole OVER w 194/186 fs 16 OVER "KESATRIA BANGSAWAN"
ms pt mage WIDOW last line "belakang." (joined paragraph, 5 lines)
ms pt miko WIDOW last line "belakang." (joined paragraph, 5 lines)
fil ls noble_lady cardrole OVER w 221/186 fs 16 OVER "MARANGAL NA KABALYERO"
fil pt noble_lady name shrunk to 35pt: w 403/415 fs 35 "MARANGAL NA BINIBINI"
fil pt noble_lady cardrole OVER w 224/186 fs 16 OVER "MARANGAL NA KABALYERO"
fil pt gunslinger WIDOW last line "tama." (joined paragraph, 4 lines)
fil pt dragon_lancer name shrunk to 35pt: w 414/415 fs 35 "MANINIBAT NG DRAGON"
fil pt mage WIDOW last line "malayo." (joined paragraph, 5 lines)
fil pt ninja WIDOW last line "pagkakamali." (joined paragraph, 5 lines)
fil pt miko name shrunk to 37pt: w 403/415 fs 37 "DALAGA NG DAMBANA"
fil pt pentatuttulu WIDOW last line "nilalang." (joined paragraph, 5 lines)
my ls swordsman font 21pt (4 lines, h 139/170)
my ls dual_blade font 19pt (4 lines, h 125/170)
my ls archer font 21pt (4 lines, h 139/170)
my ls miko font 21pt (4 lines, h 139/170)
my ls vampire font 19pt (5 lines, h 160/170)
my pt swordsman font 21pt (5 lines, h 162/170)
my pt dual_blade font 21pt (5 lines, h 162/170)
my pt noble_lady font 21pt (4 lines, h 126/170)
my pt archer font 21pt (5 lines, h 162/170)
my pt fighter font 19pt (4 lines, h 119/170)
my pt miko font 21pt (5 lines, h 162/170)
my pt vampire font 21pt (5 lines, h 162/170)
my pt dragonkin font 21pt (4 lines, h 126/170)
my pt luka font 21pt (5 lines, h 162/170)
my pt pentatuttulu font 21pt (4 lines, h 126/170)
km pt luka ORPHAN? line 5/5 "ដក។" (wrapped 4->5)
bn pt vampire WIDOW last line "কেম।" (joined paragraph, 5 lines)
ta ls dual_blade WIDOW last line "ஈடுெசய்யும்." (joined paragraph, 5 lines)
ta ls noble_lady cardrole OVER w 189/186 fs 16 OVER "உயர்குல வீராங்கைன"
ta ls archer WIDOW last line "நீக்கும்." (joined paragraph, 5 lines)
ta pt noble_lady cardrole OVER w 188/186 fs 16 OVER "உயர்குல வீராங்கைன"
ta pt dragon_lancer name shrunk to 37pt: w 400/415 fs 37 "டிராகன் ஈட்டிவீரன்"
ta pt fighter name shrunk to 37pt: w 400/415 fs 37 "ைகச்சண்ைட வீரன்"
ta pt fighter WIDOW last line "தடுக்காது." (joined paragraph, 6 lines)
ta pt vampire font 21pt (6 lines, h 137/170)
ta pt dragonkin name shrunk to 35pt: w 398/415 fs 35 "டிராகன் குலத்ேதான்"
ta pt dragonkin WIDOW last line "தாங்குவாள்." (joined paragraph, 6 lines)
ta pt pentatuttulu WIDOW last line "ெதரியாது." (joined paragraph, 6 lines)
fr pt gunslinger WIDOW last line "ennemi." (joined paragraph, 4 lines)
de pt gunslinger WIDOW last line "einen." (joined paragraph, 4 lines)
de pt archer WIDOW last line "neu." (joined paragraph, 5 lines)
es pt swordsman WIDOW last line "arriba." (joined paragraph, 5 lines)
es pt noble_lady WIDOW last line "ataques." (joined paragraph, 4 lines)
pt-BR pt noble_lady WIDOW last line "ataques." (joined paragraph, 4 lines)
pt-BR pt gunslinger WIDOW last line "alvo." (joined paragraph, 4 lines)
pl ls noble_lady cardrole OVER w 193/186 fs 16 OVER "SZLACHETNA RYCERKA"
pl pt noble_lady cardrole OVER w 195/186 fs 16 OVER "SZLACHETNA RYCERKA"
uk pt gunslinger WIDOW last line "ворога." (joined paragraph, 4 lines)
uk pt dragonkin WIDOW last line "поштовхів." (joined paragraph, 5 lines)
ru ls noble_lady cardrole OVER w 200/186 fs 16 OVER "БЛАГОРОДНЫЙ РЫЦАРЬ"
ru pt noble_lady cardrole OVER w 199/186 fs 16 OVER "БЛАГОРОДНЫЙ РЫЦАРЬ"
ru pt dragon_lancer name shrunk to 37pt: w 412/415 fs 37 "ДРАКОНИЙ КОПЕЙЩИК"
ru pt archer WIDOW last line "заряд." (joined paragraph, 5 lines)
ru pt miko WIDOW last line "сзади." (joined paragraph, 5 lines)
ru pt luka WIDOW last line "брони." (joined paragraph, 5 lines)
sw pt swordsman WIDOW last line "mbizi." (joined paragraph, 5 lines)
sw pt gunslinger WIDOW last line "mmoja." (joined paragraph, 4 lines)
sw pt archer WIDOW last line "chaji." (joined paragraph, 5 lines)
sw pt ninja WIDOW last line "kubwa." (joined paragraph, 5 lines)
sw pt vampire WIDOW last line "ardhini." (joined paragraph, 6 lines)
