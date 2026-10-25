# 布表現の見直し(2026-10-25〜)

目標: プレイヤーキャラ全員の「ボロボロの布を着ている印象」をなくし、「手入れされた、丈夫でしなやかな布」に整える。
顔・髪・体格・配色・模様・武器・装飾・ポーズ・向き・縮尺・画風は維持し、布とその傷みの表現だけを直す。

- 役割:
  - アイリーン: 素材の整理・修正作業・ゲーム内での確認
  - エステル(ChatGPT): 見た目の方針決め・生成指示の調整・修正前後のレビュー
- 相談の会話: ChatGPT プロジェクト OneMoreMile の「布表現方針相談」
- **ゲーム用の素材(Assets)はまだ書き換えていません。** 試作はこのフォルダだけにあります。

## 1. 対象の整理(12人)

`survey/all_main.jpg`(12人の立ち絵)、`survey/sheet_<id>.jpg`(立ち絵 + 走りの全コマ)を見て整理しました。

| キャラ | 布の部分 | 傷みの強さ | 方針(エステルと合意) |
|---|---|---|---|
| 黒剣士 | 赤いマント全体・腰の前垂れ | 強 | 仕立てた2〜3枚の大きな分割マント(**代表・試作済み**) |
| 双剣士 | 青い布の帯(細切れ) | 強 | 細い帯を幅広の布へ。流れる方向と面積は保つ |
| 弓使い | 緑の外套 | 強 | 柔らかい連続した面へ。躍動感は保つ |
| 忍者 | 赤いマフラー | 強 | 速度感(長くたなびく形)を保つ |
| 竜騎士 | 青緑の腰布 | 強 | 重さと厚みを出す |
| お嬢様騎士 | 青白の垂れ布の縁 | 中 | 高級感のある整った布端 |
| 拳銃士 | コートの裾 | 中 | 仕立てのよい裾へ |
| 格闘家 | 赤い帯の端 | 中 | しなやかさ重視 |
| 魔法使い | ローブの裾の重なり | 中 | 重なりを整理 |
| 吸血鬼 | 外套の穴と裾 | 中 | **コウモリの翼のような波形の縁は意匠として残す**。穴とほつれだけ |
| 巫女 | 袖・袴 | 弱 | 原則維持。房の飾りは装飾として残す |
| 竜人 | 腰の布 | 弱 | **翼(膜)・鱗・尾には触らない**。腰の布だけ確認 |

### 対象になる絵(キャラごと)

- 立ち絵: `Assets/Art/UI/Characters/<id>_main.png`(キャラ選択の大きな表示)
- カード: `<id>_card.png`(キャラ選択の左の一覧)。今の黒剣士のカードの絵もマントが裂けています。立ち絵の後で対象にします。
- アニメ(布が写るコマの数):

| キャラ | 合計 | 主な内訳 |
|---|---|---|
| 黒剣士 | 59 | 走り7、攻撃(小/中/大)15、上攻撃10、下攻撃3、ジャンプ5、着地2、被弾3、倒れる4、出発2、到着8 |
| 双剣士 | 42 | 走り7、攻撃12、被弾3、倒れる4、出発2、到着8 ほか |
| お嬢様騎士 | 43 | 走り6、攻撃4、空中/突き下ろし9、被弾3、倒れる4、出発2、到着8 ほか |
| 拳銃士 | 44 | 走り8、射撃15、被弾3、倒れる4、出発2、到着8 ほか |
| 竜騎士 | 39 | 走り8、突き/急降下 9、被弾3、倒れる4、出発4、到着8 ほか |
| 弓使い / 魔法使い / 格闘家 / 忍者 | 47 / 41 / 59 / 47 | 走り8、技のポーズ 15 / 9 / 27 / 15、被弾3、倒れる4、出発4、到着8 ほか |
| 巫女 / 吸血鬼 / 竜人 | 43 / 49 / 55 | 走り8、技のポーズ 12 / 18 / 24、被弾3、倒れる4、出発4、到着8 ほか |

- 黒剣士の走り: `Assets/Art/PlayerRun_v1/run_00〜06.png`(7コマ、1コマの絵は約220px、ゲーム中は高さ約62px)

## 2. 原因

- 最初の黒剣士(ビジュアル試作 Ver.1、2026-08-30)の生成指示に **"torn red cape"** が入っていました(`Docs/AssetProvenance_2026-10-07.md`)。
- 他のキャラもこの作風に合わせて描かれ、裂けた裾が全員に広がったと考えています。
- 今のリポジトリの生成指示(`ArtSource/CharacterSelectMain_v2/prompts.md` など)には「破れた・ぼろぼろ・擦り切れた」系の言葉は残っていません。
  - これから使う指示は、下の共通指示に置き換えます。
  - 「No holes, fraying…」のような**否定の文の中でだけ**使います。肯定の描写には使いません(エステルの提案)。

## 3. 共通の生成指示(OneMoreMile Cloth Refinement v1.0、エステルと確定)

```
Refine only the existing fabric while preserving the character's original identity, silhouette, pose, scale, position, colors, and art style.
Create well-maintained, durable, finely woven matte fabric with broad, continuous folds shaped by gravity, wind, and natural fabric tension.
Use soft, low sheen only along the ridges of large folds (matte wool-blend, not glossy, not satin).
Fabric panels must have coherent attachment points, natural overlaps, visible thickness, and smooth, intact, finished hems.
Preserve the original direction, overall area, and motion of the cloth.
Avoid holes, fraying, shredded strips, ragged edges, granular or mottled highlights, scratches, and flame-like fabric tips.
Preserve all non-fabric materials and original character details.
Keep all fabric within the original character's silhouette and canvas bounds.
For animation, maintain consistent panel count, attachment points, volume, length, and movement continuity across frames.
```

日本語の補足(依頼文に一緒に書く):

- 元画像の編集として頼みます。描き直しにはしません。
- 大きさ・背景・位置・縮尺・ポーズは元と同じにします。背景はマゼンタ #FF00FF の一色です。
- 新しい色(裏地の別色など)は足しません。裏側は元の色の暗い影色だけにします。
- 滑らかにしすぎず、元の絵になじむ筆致と立体感を残します。

展開の時の追加ルール(エステル):

1. **布の構造を先に決める**: どこに固定され、どこへ流れ、何枚のパネルか。
2. **シルエットとキャラ固有の意匠を優先**: 全員を黒剣士と同じマントの形にはしません。
3. **布以外の画素は元画像を優先**: 顔・髪・鎧・武器などは、生成結果が良く見えても元の画素に戻します。

アニメへの展開(エステル):

- 各コマを独立した新しい絵として生成しません。今の走りのコマを元に、布だけを直します。
- 次の点を全コマでそろえます:
  - 布の固定位置・枚数・色・長さ
  - 布の面積(コマごとに急に増減しない)
  - 走る方向と風の向き(マントが急に逆へ跳ねない)
- 細かなしわや縁取りは、62px でちらつかない太さにします。

## 4. 作業の流れ(黒剣士で確立)

1. エステルが元画像(raw、1024x1536、マゼンタ)を編集する。
2. `Tools/art/clothcmp.py`: 修正版を元の raw に位置合わせし、今の `_main.png` と同じ置き方で 700x1100 へ置く。
   - 位置合わせは、赤い布以外(顔・鎧・剣)の形で行う。
3. `Tools/art/clothfit.py`: 布が元の範囲より外へ出た時だけ、体の無い外側の帯を横に寄せて元の範囲に収める。
   - 黒剣士は右の帯を約13%、左を数px 寄せた。
4. `Tools/art/clothrestore.py`: 布以外(首から上の顔・髪・剣・手)を元の画素に戻す。
   - 境目は 1.5px ぼかす。小さな塊(裂けた布の影など)は戻さない。
5. `Tools/art/clothboard.py`: 比較画像を作る。
   - 全身の前後と拡大
   - 等倍(440 / 200 / 62px)
6. エステルが前後をレビューする。

## 5. 黒剣士の試作の経過

| 版 | 内容 | 結果 |
|---|---|---|
| v1 | マントを仕立てた分割マントへ | 裂け・穴は消えた。まだら模様と前垂れの尖りが残る |
| v2 | まだら模様と前垂れを修正 | 前垂れが幅広の1枚になり、元の鋭さが弱い。マントが元より右へ約30px はみ出す |
| v3 | 前垂れを2枚に戻し、肩のムラを軽減 | エステル承認。ただし右へのはみ出しは残った(x=1008、元は 979) |
| **v3 確定案** | v3 + 範囲合わせ(clothfit) + 顔・髪・剣を元の画素へ(clothrestore) | エステル最終承認。表示枠の中に収まる(x 56〜687) |

## 6. ファイルの対応(元画像は変更なし)

| 役割 | ファイル |
|---|---|
| 元画像(raw、変更なし) | `Unity/2ndAction/ArtSource/CharacterSelectMain_v2/raw/selmain_swordsman.png` |
| 今のゲーム用の立ち絵(変更なし) | `Unity/2ndAction/Assets/Art/UI/Characters/swordsman_main.png` |
| エステルの編集版 | `proto/raw/swordsman_v1_raw.png` / `v2` / `v3` |
| 範囲合わせ後 | `proto/raw/swordsman_v3fit_raw.png` |
| 確定案(元画像の座標、透過) | `proto/swordsman_v3face_aligned_raw.png` |
| **確定案(700x1100、ゲーム用と同じ置き方)** | `proto/swordsman_v3final_main.png` |
| 比較 | `proto/cmp_swordsman_final.jpg`(全身+拡大A/B)、`_zoom2.jpg`(拡大C/D/E)、`_small.jpg`(等倍)、`_charselect.jpg`(キャラ選択画面に重ねた見本) |
| 相談に使った資料 | `consult/` |

## 7. 次

1. マスターが見た目を確認する。
2. 黒剣士の走り7コマへ展開する。
   - 今のコマを元に布だけ直す。
   - 色・裾・しわの連続性とちらつきを確認する。
   - 足元・位置・縮尺・画像の大きさ・ピボットは今のまま。
3. その後、双剣士(細い布の基準)→ 弓使い・忍者・竜騎士 → 残りの順で広げる(エステルの推奨)。
