# キャラクター選択画面の文言 監査(2026-11-06)

対象: 14キャラの (1) 名前の下の一行(subtitle) (2) 英語の型ラベル(role) (3) 紹介文(flavorText)。
読むだけの監査(ゲームのファイルは変更していない・Unity 未実行)。行番号は 2026-11-06 時点。

---

## 0. まとめ(主な問題)

1. **日本語と他言語で中身が食い違っている(7人)**: 紹介文の翻訳キーは英語の文そのもの。日本語(ja.json の v)だけが新しい仕様に書き直されていて、英語の元の文と他 28 言語は古い仕様のまま。
   対象: 拳銃士 / 弓使い / 格闘家 / お嬢様騎士 / 巫女 / 吸血鬼 / 竜人。日本語以外では古い説明が出ている(例: 弓使いは英語だと「長く引くほど強い」=長押しの溜めに読める)。
2. **吸血鬼の一行「長引くほど、強くなる / The Longer, The Stronger」が誤り**: 時間では溜まらない。敵・ボスに攻撃を当てると溜まり、当てないと 2.5 秒後から減る。地上で被弾すると 30 まで落ちる。
3. **忍者「倒れる前に姿を消す」**: 自動で死を避ける仕組みは無い。あるのは前の瞬身の 0.16 秒の無敵だけで、0.8 秒に 1 回まで。日本語だと「自分が倒れる前に消える」とも読める。
4. **魔法使い「一撃でも受ければ深手」**: 魔法使いだけ被ダメージが増える仕組みは無い(1 発のダメージは全キャラ共通)。実際の根拠は、開始 HP 20(他は 30〜50)と被弾時の押し戻し ×1.4 だけ。「必ず浮いている」は正しい(穴の上も浮いたまま越えられる)。
5. **拳銃士「近接攻撃が無く、接近戦は苦手」**: 近接の技は無いが、弾は構えなしで銃口(体の 0.62m 前)から出るので、目の前の敵にも当たる。本当の弱みは、弾が細い(0.55×0.16)ことと、1 発が 1 体にしか当たらないこと(貫通はカードだけ)。
6. **弓使い**: 強くなるのは「次の**前**射撃」だけ。基準は「最後に撃った矢(どの方向でも)からの時間」で、被弾するとやり直しになる。英語の "The longer he draws" は長押しの溜めと誤読される。
7. **竜騎士 BREAKER**: 実際の強みは、長い突きで並んだ敵をまとめて貫くこと・基礎攻撃力 2 倍・ノックバック ×1.8・押し戻されにくいこと。**ボスの BREAK(崩し)が溜まりやすい効果は無い**(崩しは攻撃の種類だけで決まる)。ゲーム内の「BREAK」と混同されやすい。
8. **双剣士 GROUND COMBO**: 地上専用の強みは無い。5 段の連撃は空中でも同じ処理で出る。実際の特徴は、最大 5 段の連撃・攻撃速度 ×0.75(全キャラ最速)・走る速さ ×1.15(最速)・1 発の威力 10(半分)。
9. **お嬢様騎士**: 強くなる基準は時間でも Lv でもなく、**このランで到達した距離**(GameManager.MaxDistance)。20km から 80km にかけて、攻撃力 ×1→×1.6・間合い ×1→×1.27 になる。疾走出発でも到着した距離がそのまま入るので、話は合っている。ただし日本語の「長く走るほど」は時間と読める。鎧(振っている間は止まらない)の説明も無い。
10. **巫女**: 式神は後ろへ飛ばす攻撃で、「進路を制する」ものではない。御札は 2 枚目を当てるとその場で爆発するので、「即効性に欠ける」は言い過ぎ。結界の減速は雑魚だけで、ボスには効かない。
11. 黒剣士の紹介文は物語だけで、戦い方が伝わらない(任意の改善)。竜人・ルカ・宇宙人は大きな誤りなし。

参考(文言とは別の、実装上の気付き。未検証): お嬢様の鎧の「受けるダメージ ×0.75」(PlayerController.Noble.cs:46-47)は、通常 3 マップでは DifficultyTuning.Adjust(Balance/DifficultyTuning.cs:229-258)がダメージをハートの表で置き換えるので、通常の被弾では効いていないように見える。逆に、ボスの重い一撃(20→15)は「普通のボスの一撃」に分類し直されて軽くなる。文案ではダメージ軽減に触れていない。

---

## 1. 表示の仕組み・キー・文字数

- データ: `Assets/Resources/Characters/<id>.asset` の `subtitle` / `role` / `flavorText`(英語)。`Assets/Editor/CharacterDatabaseBuilder.cs:189-830` の Spec は**既にあるアセットを上書きしない**(同ファイル 5-9 行のコメント)。文を変える時は**アセットを直接直し、Builder の Spec も同じ文に**そろえる。
- 表示: `CharacterSelectUI.cs:412-415`(`Loc.Auto(def.subtitle / def.flavorText / def.role)`)、カルーセルの札の role は `CharacterSelectUI.Carousel.cs:118`、闘技場は `Arena/ArenaController.cs:408-411`(同じキー)。
- 翻訳: `Loc.Auto` は**英語の文そのものがキー**(`Resources/Localization/<lang>.json` の k)。キャラ文言は「以前から英語だった UI ラベル」の扱いで、ja.json でも k は英語・v が日本語。英語を変えるとキーが変わるので、**30 言語すべてに新しい k を足す**必要がある。訳が無い言語は英語が出る(`Loc.cs:209-216` Raw)。
- 型ラベル(role)は ja.json でも英語のまま(v = 英語)。他の言語は訳している(例: fr "COMBO AU SOL")。
- 現在の文字数(目安)
  - subtitle: 枠 480×32px・20pt・1 行(`Editor/SceneBuilder.cs:1838-1848`)。英語の最長 27 字(Swift Steel, Endless Motion)、日本語の最長 16 字(猫パンチ、飛びかかり、大ジャンプ)。**日本語は 16 字以内、英語は 28 字以内**を推奨。
  - role: バッジ 320×46px・22pt 太字(`SceneBuilder.cs:1852-1869`)。カルーセルの札はさらに小さい字(`Carousel.cs:99`)。最長 12 字(GROUND COMBO / NOBLE KNIGHT)。**12 字以内**を推奨。
  - flavorText: 480×170px・22pt・はみ出すとスクロール(`SceneBuilder.cs:1889-1898`、`CharacterSelectUI.cs:413`)。日本語は 1 行がだいたい 21 字で折り返す。今の日本語の最長は 1 行 25 字・4〜5 行。英語は全体で 120〜175 字。**日本語は 4 行・1 行 24 字以内、英語は 180 字以内**を推奨。

---

## 2. キャラ別

凡例: 「現在」は ja(日本語の表示)/ en(英語のキー=元の文)。文中の \n は改行。
「再翻訳」= その文を変えた時に新しい訳が要る言語。

### 2-1. 黒剣士 swordsman

| 項目 | 現在の文 | 実装で確認した事実 | 修正文案 | 理由 |
|---|---|---|---|---|
| subtitle | ja 歩みを止めない者 / en The One Who Keeps Moving | 物語の二つ名で、性能の主張は無い | 変更なし | 雰囲気の一行として成立 |
| role | BALANCED | 全倍率 1.0・HP 30/50・3 段(swordsman.asset:36-45)。他キャラの基準 | 変更なし | 正しい |
| flavor | ja 己の道を歩む孤高の剣士。\n何度倒れても、また立ち上がる。\nまだその先に、\nたどり着くべき場所があるから。<br>en A lone swordsman who walks his own path.\nNo matter how many times he falls,\nhe rises again - because there is still\na further place to reach. | 前: 踏み込む 3 段斬り(PlayerController.cs:2932-3030, 3280)/ 上: ジャンプ+斬り上げ(打ち上げ、空中の敵を引き寄せる)(3099-3160)/ 下: 急降下斬り+着地の衝撃(3179, 3233) | ja 己の道を歩む孤高の剣士。\n踏み込む3連斬り、打ち上げる斬り上げ、\n急降下斬りと、基本の技がそろう。\n何度倒れても、また立ち上がる。<br>en A lone swordsman who walks his own path.\nA stepping three-hit combo, a launching upslash\nand a diving strike - every basic is his.\nNo matter how many times he falls, he rises again. | 任意。戦い方が一言も無いので、基本形のキャラだと分かる一行を足す |

キー: subtitle `The One Who Keeps Moving` / role `BALANCED` / flavor `A lone swordsman who walks his own path.\nNo matter how many times he falls,\nhe rises again - because there is still\na further place to reach.`
再翻訳: flavor を変える場合は 30 言語すべて。

### 2-2. 双剣士 dual_blade

| 項目 | 現在の文 | 実装で確認した事実 | 修正文案 | 理由 |
|---|---|---|---|---|
| subtitle | ja 迅き刃、止まらぬ舞 / en Swift Steel, Endless Motion | — | 変更なし | 雰囲気として正しい |
| role | GROUND COMBO | 地上専用の処理は無い。前/後の攻撃は地上でも空中でも同じ DoAttack(PlayerController.cs:2879-2919 に接地の条件なし)。連撃の上限 5(dual_blade.asset:36)は空中でも同じ(2997)。キャラ専用のコードも無い(grep で AttackFlair の色だけ) | **SWIFT COMBO**(ja も英語表記のまま) | 「地上だけ強い」と誤解される。速さ+連撃が本当の特徴。SWIFT COMBO はカード名と重ならない(en.json で確認) |
| flavor | ja 一度も足を緩めることなく\n連撃をつなぎ続ける俊足の双剣使い。\n速さと勢いこそが最大の武器。<br>en A swift dual-wielder who chains strikes\ntogether without ever slowing down.\nSpeed and momentum are her greatest weapons. | 連撃 5 段・攻撃速度 ×0.75(全キャラ最速)・走る速さ ×1.15(最速)・攻撃力 10(他の多くは 20)・間合い ×0.85・敵を飛ばす力 ×0.6(敵が離れにくい)・HP 50/50(満タンで開始)(dual_blade.asset:33-42) | ja 足を緩めず走り抜ける、俊足の双剣使い。\n一撃は軽いが、地上でも空中でも\n最大5段の速い連撃をつなぎ続ける。\n速さと勢いこそが最大の武器。<br>en A swift dual-wielder who never slows her stride.\nEach strike is light, but on the ground or in the air\nshe chains up to five quick slashes.\nSpeed and momentum are her greatest weapons. | 1 発が軽いこと・5 段・空中でもつながることを足す。大筋は今の文のまま |

キー: subtitle `Swift Steel, Endless Motion` / role `GROUND COMBO` / flavor `A swift dual-wielder who chains strikes\ntogether without ever slowing down.\nSpeed and momentum are her greatest weapons.`
再翻訳: role は ja 以外の 29 言語(ja は英語表記)。flavor は 30 言語。

### 2-3. お嬢様騎士 noble_lady

| 項目 | 現在の文 | 実装で確認した事実 | 修正文案 | 理由 |
|---|---|---|---|---|
| subtitle | ja まだ目覚めぬ英雄 / en A Hero Not Yet Awakened | 距離で目覚める仕組みがある(下記) | 変更なし | 仕様と合っている |
| role | NOBLE KNIGHT | — | 変更なし | 性能を主張していない |
| flavor | ja 銀と金の鎧をまとう騎士。\n竜をも倒すと伝わる大剣を携えるが、\nその力はまだ眠っている。\nランで長く走るほど、真の力が目覚めていく。<br>en Clad in silver and gold, she wields a\ngreatsword said to fell dragons.\nHer legend, however, has yet to catch\nup with her armor. | 覚醒: **このランで到達した距離**(GameManager.MaxDistance)で、20km→80km の間に攻撃力 ×1→×1.6・間合い ×1→×1.27(PlayerController.Noble.cs:15-39、Balance/BalanceTuning.cs:51-53)。時間や Lv は関係ない。疾走出発は到着時に MaxDistance=到着距離(Sprint/GameManager.Sprint.cs:206)なので、到着した距離の分だけ最初から目覚めている=整合。<br>大剣: 間合い 0.75→1.0・威力 ×1.6(BalanceTuning.cs:75-76、Noble.cs:22, 42)。1 段攻撃・攻撃速度 ×1.35(遅い)・攻撃力 10・HP 30/30(最大 HP が全キャラ最低)(noble_lady.asset:35-44)。<br>鎧: 振っている間に被弾しても攻撃が止まらず、押し戻されない(Noble.cs:50-61、BalanceTuning.cs:54)。<br>2 段ジャンプ・空中の薙ぎ払い・振り下ろし急降下+衝撃波(PlayerController.cs:540-543、Noble.cs:64-184) | ja 銀と金の鎧をまとう騎士。\n大剣の一撃は重いが、その力はまだ眠っている。\n20kmを越えて進むほど、力と間合いが目覚める。\n振っている間は、攻撃を受けても止まらない。<br>en Clad in silver and gold, she swings a heavy greatsword\nwhose true power still sleeps.\nPast 20 km, the farther she goes, the more its power and reach awaken.\nMid-swing, taking a hit won't stop her blade. | 「長く走る」は時間とも読めるので「進む(距離)」に直す。何 km からかを書く。鎧の特徴を足す。英語(=他言語の元)は仕組みに一言も触れていない |

キー: subtitle `A Hero Not Yet Awakened` / role `NOBLE KNIGHT` / flavor `Clad in silver and gold, she wields a\ngreatsword said to fell dragons.\nHer legend, however, has yet to catch\nup with her armor.`
再翻訳: flavor は 30 言語(今の時点でも ja 以外は古い文の訳)。

### 2-4. 二丁拳銃士 gunslinger

| 項目 | 現在の文 | 実装で確認した事実 | 修正文案 | 理由 |
|---|---|---|---|---|
| subtitle | ja 一発に、ひとつの距離 / en One Shot, One Distance | — | 変更なし | 雰囲気 |
| role | RANGED | 全方向が飛び道具(PlayerController.cs:2941-2945, 3284-3383) | 変更なし | 正しい |
| flavor | ja 二丁拳銃で敵を撃ち抜く遠距離の射手。\n前後どちらにも射撃でき、\n敵が近づく前に倒すのが得意。\nただし近接攻撃が無く、接近戦は苦手。<br>en Twin pistols, a single sharp line of fire.\nShe wins the fight before it ever\ncloses in - but let an enemy through,\nand she has no answer for it. | 前/後: 構えなしの水平射撃、3 連までつながる(3298-3352、gunslinger.asset:39)。上: ジャンプ+斜め上(3355-3363)。下: 空中で斜め下へ撃ち、0.22 秒だけ落下をゆるめる。1 回の滞空で 3 回まで(3371-3383、PlayerController.cs:857)。<br>弾: 銃口は体の 0.62m 前(PlayerController.cs:870)、当たり判定 0.55×0.16 の細長い形(PlayerBullet.cs:100)、最初の 1 体で消える(PlayerBullet.cs:52-68。貫通はカードだけ: 21-24)、15m/秒×1.6 秒(gunslinger.asset:134-135)。<br>近い敵: 弾は即座に銃口から出るので、目の前の敵にも当たる。近いから当たらない・弱いという処理は無い | ja 二丁拳銃で敵を撃ち抜く遠距離の射手。\n前後・斜め上・斜め下の4方向へ撃てる。\n空中の下撃ちは、落下を一瞬ゆるめる。\n弾は細い一直線で、当たるのは1発に1体だけ。<br>en A gunslinger who picks foes off with twin pistols.\nShe fires forward, backward, and diagonally up and down;\nshooting down in midair briefly slows her fall.\nEach thin shot hits only one enemy. | 「近接の技が無い」と「接近戦に弱い」を混同しない。本当の制約(細い・1 体だけ)を書く。英語は「近寄られたら打つ手が無い」で事実と違う |

キー: subtitle `One Shot, One Distance` / role `RANGED` / flavor `Twin pistols, a single sharp line of fire.\nShe wins the fight before it ever\ncloses in - but let an enemy through,\nand she has no answer for it.`
再翻訳: flavor は 30 言語。

### 2-5. 竜騎士 dragon_lancer

| 項目 | 現在の文 | 実装で確認した事実 | 修正文案 | 理由 |
|---|---|---|---|---|
| subtitle | ja 突き破れ、決して屈するな / en Break Through, Never Yield | — | 変更なし | 「突破」の意味で合っている |
| role | BREAKER | 強み: 3 段突き。判定は体の 0.45m 先から 2.45m×(0.9/1.05/1.3)の細長い帯で、帯の中の敵全員に当たる(PlayerController.Lancer.cs:9-12, 302-352、dragon_lancer.asset:140-141, 172-176)。基礎攻撃力 40(多くは 20)・ノックバック ×1.8・攻撃速度 ×1.35(遅い)・被弾の押し戻し ×0.45・HP 40/50(asset:38-47, 85)。速く走るほど突進とノックバックが最大 +60%(Lancer.cs:84-89)。障害物へのダメージも攻撃力で決まる(ObstacleController.cs:111)ので壊しやすい。<br>**ボスの BREAK(崩し)は攻撃の種類だけで決まり、竜騎士の補正は無い**(Bosses/BossBattleTuning.cs:57-61, 141-145) | 案A **PIERCER** / 案B LONG REACH / 案C BREAKER のまま(紹介文で補う) | ゲーム内の「BREAK」(ボスの崩し)と同じ語で、崩しが得意と誤解されやすい。実際は「並んだ敵をまとめて貫く長い突き」。PIERCER はカード名 PIERCING BLADE と少し似るので、気になる場合は案B |
| flavor | ja 竜の牙から鍛えた槍を携える\n若き竜騎士。\nどんな陣も正面から打ち破るが、\n懐に入り込まれると\n対処が難しい。<br>en A young dragon knight bearing a lance\nforged from a dragon's fang.\nHe breaks every line head-on -\nbut anything that slips inside his reach\nis hard for him to answer. | 懐(体から 0.45m 以内)には突きの判定が無い(Lancer.cs:12)=「懐が苦手」は正しい。急降下突き+着地の衝撃波 半径 1.8(asset:198-203) | ja 竜の牙から鍛えた槍を携える若き竜騎士。\n長い突きは前に並ぶ敵をまとめて貫き、\n一撃が重く、大きく吹き飛ばす。\nだが槍の届かない懐は苦手。<br>en A young dragon knight with a lance forged from a dragon's fang.\nHis long thrusts pierce every foe in a line,\nhitting hard and sending them flying -\nbut he can't strike foes pressed right against him. | 「どんな陣も打ち破る」の具体的な中身(貫通・重さ・吹き飛ばし)を書く。懐の弱点はそのまま |

キー: subtitle `Break Through, Never Yield` / role `BREAKER` / flavor `A young dragon knight bearing a lance\nforged from a dragon's fang.\nHe breaks every line head-on -\nbut anything that slips inside his reach\nis hard for him to answer.`
再翻訳: role は ja 以外の 29 言語(fr BRISEUR / de BRECHER など今の訳も「壊す」の意味)。flavor は 30 言語。

### 2-6. 弓使い archer(表示名 RANGER)

| 項目 | 現在の文 | 実装で確認した事実 | 修正文案 | 理由 |
|---|---|---|---|---|
| subtitle | ja 引き、狙い、貫く / en Draw, Aim, Pierce | 最大段階の矢は貫通する(PlayerController.Archer.cs:101) | 変更なし | 正しい |
| role | SNIPER | 前の矢は最大 3.2 倍・射程が長い | 変更なし | 正しい |
| flavor | ja 深い森に暮らす長弓の使い手。\n前の射撃から間を空けるほど、\n次の矢が強くなる。\n遠距離では強いが、接近戦と連射は苦手。<br>en A ranger of the deep woods\nwith a longbow taller than himself.\nThe longer he draws, the harder it hits -\nbut up close, he has little to answer with. | 強化されるのは**前の射撃だけ**(ArcherForward が段階を読む: Archer.cs:57-87)。段階の基準は「最後に矢を撃ってからの時間」で、前・後・上・空中の下・地上の低い矢の**どれを撃っても**数え直し(Archer.cs:120-125, 152, 175, 205, 245)。0.55 秒で 2 倍、1.2 秒で 3.2 倍+矢が速く+**敵を貫通**(制限なし)(CharacterKitParams.cs:27-39)。**被弾すると溜め直し**(PlayerController.Kit.cs:189)、ランの開始時は溜まった状態(Kit.cs:128)。長押しの操作は無い(Archer.cs:5-7)。<br>近い敵: 矢は体の 0.45m 前から出るので当たる。連打すると弱い矢しか出ないのが本当の弱み。走る速さ ×0.93(archer.asset:45) | ja 深い森に暮らす長弓の使い手。\n最後に矢を放ってから間を空けるほど、\n次に前へ放つ矢が強くなり、最大で敵を貫く。\n連射すると弱い矢しか出ず、被弾で溜め直し。<br>en A ranger of the deep woods with a towering longbow.\nThe longer since his last arrow, the stronger\nhis next forward shot - fully drawn, it pierces.\nRapid fire looses only weak arrows, and a hit resets the draw. | 対象(前の矢)と条件(どの矢でも数え直し・被弾で戻る)を正確に書く。英語の "The longer he draws" は長押しの溜めに読める。「接近戦が苦手」は連射の弱さとして言い直す |

キー: subtitle `Draw, Aim, Pierce` / role `SNIPER` / flavor `A ranger of the deep woods\nwith a longbow taller than himself.\nThe longer he draws, the harder it hits -\nbut up close, he has little to answer with.`
再翻訳: flavor は 30 言語(今の時点でも ja 以外は古い文の訳)。

### 2-7. 魔法使い mage(表示名 SORCERESS)

| 項目 | 現在の文 | 実装で確認した事実 | 修正文案 | 理由 |
|---|---|---|---|---|
| subtitle | ja 宙に浮き、下を撃つ / en Float Above, Strike Below | 下への魔法がある(PlayerController.Mage.cs:226-244) | 変更なし | 雰囲気として可 |
| role | FLIGHT MAGE | — | 変更なし | 正しい |
| flavor | ja 決して地に足をつけない魔法使い。\n自在に浮き沈みしながら、\n眼下のすべてに魔法を降らせる。\nだが一撃でも受ければ深手となる。<br>en A sorceress who never touches the ground.\nShe rises and sinks at will,\nraining spells on whatever lies below -\nbut a single blow cuts deep. | **常に浮いている**: ジャンプ/重力の代わりに MageFlightMove を使い、地面から最低 0.4m の高さを保つ(Mage.cs:70-107、CharacterKitParams.cs:72)。高さは 4 段(1 段 1.2m)。上/下のフリックで 1 段ずつ変わり、同時に上=斜め上の雷撃、下=真下の魔法を撃つ(Mage.cs:124-133)。前=小爆発する魔法弾、後=弱い魔法弾(151-204)。穴の上では直前の地面の高さを保つので**穴に落ちない**(73-78)。洞窟の天井で頭打ち、天井の針には当たる(90-100)。<br>「深手」の根拠: 魔法使いだけ被ダメージが増える処理は無い(TakeDamage: PlayerController.cs:1869-1925。キャラ別の補正はお嬢様の NobleDamage だけ: 1919)。根拠は開始 HP 20(多くは 30)・最大 HP 40・被弾の押し戻し ×1.4(mage.asset:36-37, 86)だけ | ja 決して地に足をつけない魔法使い。\n上下で高さを変えるたびに魔法を放ち、\n穴の上も浮いたまま越えていく。\nだがHPが低く、被弾で大きく押し戻される。<br>en A sorceress who never touches the ground.\nEach rise or fall in altitude looses a spell,\nand she floats right over pits.\nBut her HP is low, and hits knock her far back. | 「一撃で深手」は被ダメージが大きいと読めるが、そういう処理は無い。HP が低いことと押し戻しに直す。高さの操作と穴を越えられることは大きな特徴なので書く |

キー: subtitle `Float Above, Strike Below` / role `FLIGHT MAGE` / flavor `A sorceress who never touches the ground.\nShe rises and sinks at will,\nraining spells on whatever lies below -\nbut a single blow cuts deep.`
再翻訳: flavor は 30 言語。

### 2-8. 格闘家 fighter(表示名 BRAWLER)

| 項目 | 現在の文 | 実装で確認した事実 | 修正文案 | 理由 |
|---|---|---|---|---|
| subtitle | ja 打ち返す拳 / en Fists That Answer Back | カウンターがある | 変更なし | 正しい |
| role | COUNTER | 同上 | 変更なし | 正しい |
| flavor | ja 鉄の籠手で戦う格闘家。\n射程は短いが、重い連撃が得意。\n後ろ攻撃で構え、その間に攻撃を受けると\n無傷で強烈な反撃を放つ。<br>en A martial artist who fights\nwith nothing but iron gauntlets.\nHe must get close - and when you strike first,\nhe strikes back harder. | 連撃: 4 段(ジャブ/ストレート/蹴り/締め)。1〜3 段目は 0.8/0.8/1.1 倍と軽く、4 段目だけ 3.0 倍で大きく吹き飛ばす(PlayerController.Fighter.cs:66-148、BalanceTuning.cs:66-71)。全キャラで最も射程が短い(Fighter.cs:5)。<br>**カウンターの条件**: 後ろフリックで 0.16 秒の後退+0.32 秒の構え=**フリックから約 0.48 秒の間**(Fighter.cs:159-160)。この間に敵/ボスの攻撃・接触・弾を受けると、ダメージ 0 で周囲(半径 1.35)へ 3 倍の反撃+0.3 秒の無敵(Kit.cs:324-334、Fighter.cs:205-242、CharacterKitParams.cs:120-131)。攻撃中/硬直中は構えられない(Fighter.cs:51)。何も来なければ後ろへ肘打ち(181-193)。<br>**防げないもの**: 穴への落下(TakeDamage の isFall)、洞窟の天井の針(PlayerController.cs:1818)、死神に捕まる(ソロは GameManager.ReapPlayer が直接 HP を減らす: GameManager.cs:3747-3756)、ダメージの無い減速(ApplyMoveSlow: PlayerController.cs:2317-2323) | ja 鉄の籠手で戦う、射程の短い格闘家。\n4段目で大きく吹き飛ばす連撃が得意。\n後ろ攻撃の構えの一瞬に敵の攻撃を受けると、\n無傷で周囲へ強烈な反撃。穴や地形は防げない。<br>en A martial artist with iron gauntlets and the shortest reach.\nHis four-hit combo ends in a blow that sends foes flying.\nTake an enemy hit in the brief stance of his back attack,\nand he takes no damage and strikes back all around - but not against pits or terrain. | 今の日本語はおおむね正しい。構えが一瞬(約 0.5 秒)であること、防げない物があることを足す。「重い連撃」は 4 段目だけ重いので言い直す。英語は「先に殴ればもっと強く返す」だけで条件が分からない |

キー: subtitle `Fists That Answer Back` / role `COUNTER` / flavor `A martial artist who fights\nwith nothing but iron gauntlets.\nHe must get close - and when you strike first,\nhe strikes back harder.`
再翻訳: flavor は 30 言語(今の時点でも ja 以外は古い文の訳)。

### 2-9. 忍者 ninja(表示名 SHINOBI)

| 項目 | 現在の文 | 実装で確認した事実 | 修正文案 | 理由 |
|---|---|---|---|---|
| subtitle | ja 消え、斬り、また消える / en Vanish, Cut, Vanish | 瞬身の残像・急降下の煙の演出 | 変更なし | 雰囲気の一行として可 |
| role | MOBILITY | 位置を素早く変えるキャラ(Ninja.cs:4-5) | 変更なし | 正しい |
| flavor | ja 斬ったはずの場所には、もういない忍。\n一太刀で敵の間をすり抜け、\n倒れる前に姿を消す。\nだが打たれ弱い。<br>en A shinobi who is never where you strike.\nShe blinks through enemies with a single cut\nand is gone before they fall -\nbut she cannot take many hits. | 前: 瞬身。0.13 秒で 3m 踏み込み、通り抜けた敵を斬る(PlayerController.Ninja.cs:48-103、ninja.asset の ninja:dashDistance 3)。**無敵は 0.16 秒だけで、0.8 秒に 1 回まで**(連打しても続かない)(Ninja.cs:60-66、CharacterKitParams.cs:161-162)。後: 手裏剣 2 枚+後ろへ小跳び(105-143)。上: 斜め上の跳躍斬り(146-177)。下: 空中=急降下斬り+着地の斬撃 / 地上=滑り斬り(179-250)。<br>**自動で倒れずに済む仕組みは無い**(TakeDamage での特別扱いは瞬身の無敵だけ: Kit.cs:324-327)。HP 30/40(ninja.asset:36-37) | ja 斬ったはずの場所には、もういない忍。\n前への瞬身で敵の間を斬り抜け、\n踏み込んだ一瞬だけ攻撃をすり抜ける。\nだが最大HPは低く、打たれ弱い。<br>en A shinobi who is never where you strike.\nHer forward blink cuts through enemies,\nslipping past attacks for a split second.\nBut her max HP is low, and she can't take many hits. | 「倒れる前に姿を消す」は、自分が倒れる前に消える(=死を避ける)とも読めるので削る。無敵がごく短いことを書く |

キー: subtitle `Vanish, Cut, Vanish` / role `MOBILITY` / flavor `A shinobi who is never where you strike.\nShe blinks through enemies with a single cut\nand is gone before they fall -\nbut she cannot take many hits.`
再翻訳: flavor は 30 言語。

### 2-10. 巫女 miko(表示名 SHRINE MAIDEN)

| 項目 | 現在の文 | 実装で確認した事実 | 修正文案 | 理由 |
|---|---|---|---|---|
| subtitle | ja 行く手を封じよ / en Seal the Path Ahead | 前方の結界・御札の封印 | 変更なし | 正しい |
| role | SEALER | 同上 | 変更なし | 正しい |
| flavor | ja 山の社に仕える巫女。\n御札・式神・結界で敵の進路を制する。\n攻撃は即効性に欠けるが、\n仕掛けを重ねて敵を追い詰める。<br>en An exorcist of a mountain shrine.\nShe lays ofuda and barriers ahead,\nand the path itself turns against her foes -\nbut she is slow to strike first. | 前: 御札(当たり 0.6 倍)。敵/ボスに貼り付き、**1.3 秒後に爆発**(1.6 倍)。貼られた敵に 2 枚目を当てると**その場で大爆発**(2.2 倍)(PlayerController.Miko.cs:53-106、KitZones.cs:207-211、CharacterKitParams.cs:197-206)。<br>後: **式神=紙の鳥を後ろへ放つ**。ゆらゆら飛んで最初に触れた敵に 1 倍(Miko.cs:108-140)。進路を制する技ではない。<br>上: ジャンプ+御札を扇状に 3 枚(各 0.5 倍、貼り付かない)(143-149)。<br>下: 結界。前方 3.4m の地面に幅 4.2m・2.6 秒。0.45 秒ごとに 0.35 倍のダメージ。中の敵の横移動を 30% に遅くするが、**遅くなるのは雑魚(EnemyController)だけ**(KitZones.cs:168-191)。同時に 1 つで、張り直すと古い方は消える(KitZones.cs:35) | ja 山の社に仕える巫女。\n御札は敵に貼り付き、時間差で爆ぜる。\n2枚目を当てれば、その場で大爆発。\n前に張る結界は雑魚を鈍らせ、後ろへは式神を放つ。<br>en A shrine maiden of a mountain sanctuary.\nHer ofuda stick to foes and burst a moment later;\na second ofuda sets off a bigger blast at once.\nHer barrier ahead slows lesser foes, and her shikigami fly behind her. | 式神は後ろへの攻撃なので「進路を制する」に入れない。2 枚目ですぐ爆発するので「即効性に欠ける」は言い過ぎ。結界の減速はボスに効かない |

キー: subtitle `Seal the Path Ahead` / role `SEALER` / flavor `An exorcist of a mountain shrine.\nShe lays ofuda and barriers ahead,\nand the path itself turns against her foes -\nbut she is slow to strike first.`
再翻訳: flavor は 30 言語(今の時点でも ja 以外は古い文の訳)。

### 2-11. 吸血鬼 vampire

| 項目 | 現在の文 | 実装で確認した事実 | 修正文案 | 理由 |
|---|---|---|---|---|
| subtitle | ja 長引くほど、強くなる / en The Longer, The Stronger | **時間では溜まらない**。敵/ボスに攻撃を当てた時だけ増え(PlayerController.Vampire.cs:41-59, 167-171。障害物は数えない: ObstacleController.cs:111 は通知なし)、最後に当ててから 2.5 秒たつと毎秒 9 減る(Vampire.cs:87-90、CharacterKitParams.cs:235-236) | ja **当て続けるほど、強くなる** / en **Keep Hitting, Grow Stronger** | 「戦いが長引けば強くなる」と読める。実際は当て続けないと減る |
| role | BLOOD | — | 変更なし | 正しい |
| flavor | ja 刃を持たず、爪と血の力で戦う夜の貴族。\n攻撃を当てるほど血のゲージが溜まり、強く速くなる。\n当て続けないとゲージは減っていき、\nランの始めは空の状態から戦う。<br>en A noble of the night who needs no blade.\nEvery wound he deals feeds his blood,\nand a long fight only makes him faster -\nbut he starts every battle hungry. | ゲージ 0〜100。爪/霧/低い薙ぎの 1 回の命中で +7、コウモリ +3.5、急降下吸血 +35(CharacterKitParams.cs:233-234、Vampire.cs:268, 354)。50 以上で威力 ×1.2・技の時間 ×0.88(26-27、params:237-239)。100 で 5 秒間強化: 威力 ×1.3・技の時間 ×0.7・走る速さ ×1.12(params:241-244)。強化中は命中ごとに回復が少し貯まり(1 回 0.06 ハート)、ハート 1 つ貯まると回復。ただし 12 秒に 1 回まで(Vampire.cs:46-49, 62-75、params:245, 250)。終わるとゲージ 30(85)。ゲージ 50 以上の急降下吸血の命中でも回復が貯まる(349-357)。<br>**地上での被弾や落下で、強化が終わりゲージは 30 まで下がる**(OnKitRespawn→VampireReset(false): Kit.cs:208-217、Vampire.cs:31-38、PlayerController.cs:2193-2194, 2405)。ランの開始時は 0(Kit.cs:97, 203) | ja 爪と血の力で戦う夜の貴族。\n敵に当てるほど血のゲージが溜まり、強く速くなる。\n満タンで5秒間さらに強まり、当て続ければ回復も。\n当てないと減っていき、地上で被弾すると大きく減る。<br>en A noble of the night who fights with claws and blood.\nEvery hit on a foe fills his blood gauge, making him stronger and faster.\nAt full, he surges for 5 seconds, and steady hits slowly heal him.\nThe gauge drains if he stops hitting, and drops sharply if he's hit on the ground. | 今の日本語はおおむね正しいが、満タン時の強化と回復、被弾で減ることが無い。英語は "a long fight makes him faster"(長い戦い=時間)で、一行と同じ誤り |

キー: subtitle `The Longer, The Stronger` / role `BLOOD` / flavor `A noble of the night who needs no blade.\nEvery wound he deals feeds his blood,\nand a long fight only makes him faster -\nbut he starts every battle hungry.`
再翻訳: subtitle・flavor とも 30 言語(今の訳は全言語で「長引くほど」の意味。例: de "Je länger, desto stärker")。

### 2-12. 竜人 dragonkin

| 項目 | 現在の文 | 実装で確認した事実 | 修正文案 | 理由 |
|---|---|---|---|---|
| subtitle | ja 爪、尾、翼、炎 / en Claw, Tail, Wing, Flame | 4 方向の技そのまま(PlayerController.Dragonkin.cs:4-12) | 変更なし | 正しい |
| role | BRUTE | 攻撃力 30・HP 50/60(最大)・押し戻し ×0.5(dragonkin.asset:36-38, 86) | 変更なし | 正しい |
| flavor | ja 竜の血を引く戦士。\n爪・尾・炎を武器に、敵を薙ぎ払う。\n大柄で攻撃を受けやすいが、\n高いHPを誇り、押し戻されにくい。<br>en A warrior with the blood of dragons.\nShe needs no weapon - claws, tail and fire\nsweep everything aside.\nHer size makes her easy to hit, but hard to stop. | 体の当たり判定 ×1.15(Kit.cs:103、params:318)。上: 羽ばたいて普通より高く跳ぶ(×1.18)。1 回のジャンプにつき 1 回、落下を 0.45 秒ゆるめる滑空(Dragonkin.cs:9-11、params:300-305)。下: 炎のブレス+燃える地面 | ja 竜の血を引く戦士。\n爪・尾・炎で敵を薙ぎ払い、翼で高く跳んで滑空する。\n大柄で攻撃を受けやすいが、\n高いHPを誇り、押し戻されにくい。<br>en A warrior with the blood of dragons.\nClaws, tail and fire sweep foes aside; her wings lift her high and let her glide.\nHer size makes her easy to hit,\nbut her HP is high and she is hard to push back. | 任意。日本語は正しい。翼(高いジャンプ・滑空)が抜けている。英語は HP の高さが無い(今の時点でも ja と他言語で中身が違う) |

キー: subtitle `Claw, Tail, Wing, Flame` / role `BRUTE` / flavor `A warrior with the blood of dragons.\nShe needs no weapon - claws, tail and fire\nsweep everything aside.\nHer size makes her easy to hit, but hard to stop.`
再翻訳: flavor を変える場合は 30 言語。変えない場合も、ja と合わせるなら 29 言語。

### 2-13. ルカ luka

| 項目 | 現在の文 | 実装で確認した事実 | 修正文案 | 理由 |
|---|---|---|---|---|
| subtitle | ja 猫パンチ、飛びかかり、大ジャンプ / en Paw, Pounce, Leap | 前 3 段目が飛びかかり・上が飛びつき(PlayerController.Cat.cs:7-9) | 変更なし | 正しい |
| role | AGILE | 走る速さ ×1.08・ジャンプ ×1.05・被弾の硬直 0.12 秒(既定 0.25)(luka.asset:45, 82) | 変更なし | 正しい |
| flavor | ja 元気いっぱいの、小さく勇敢なキジトラ猫。\n武器も鎧もなし。あるのは素早い前足と\n怖いもの知らずの飛びかかり。\n小さな体で危険をすり抜け、軽やかに高く跳ぶ。<br>en A small, brave tabby cat full of energy.\nNo weapon, no armor - just quick paws\nand a fearless pounce.\nSmall enough to slip past danger, light enough to leap high. | 体の当たり判定が小さく低い(0.95×0.6)(Kit.cs:104、params:363)。無敵の技は無い(Cat.cs:6)。HP 30/40 | 変更なし(任意: 「被弾してもすぐ立て直す」を足せる) | 誤りなし |

キー: subtitle `Paw, Pounce, Leap` / role `AGILE` / flavor `A small, brave tabby cat full of energy.\nNo weapon, no armor - just quick paws\nand a fearless pounce.\nSmall enough to slip past danger, light enough to leap high.`
再翻訳: 不要。

### 2-14. ペンタトゥットゥルー星人 pentatuttulu

| 項目 | 現在の文 | 実装で確認した事実 | 修正文案 | 理由 |
|---|---|---|---|---|
| subtitle | ja 伸びて、弾んで、ぽよん / en Stretch, Bounce, Poyon | — | 変更なし | 正しい |
| role | STRETCH | 前 1・2 段目の届き 2.5(params:375) | 変更なし | 正しい |
| flavor | ja 遠いどこかから来た、雲のように白くふわふわな訪問者。\n手足は思った以上にぐーんと伸びて、\n踏みつけた相手からぽよんと跳ね上がる。\nどこから来たのか、誰も知らない。<br>en A fluffy, cloud-white visitor from somewhere far away.\nIts arms and legs stretch farther than you think,\nand it bounces off what it stomps on.\nNobody knows where it came from. | 空中の下=スタンプ。敵に当たった時だけ跳ね返る。1 回のジャンプにつき 1 回(PlayerController.Cloud.cs:8-9、params:403) | 変更なし | 誤りなし |

キー: subtitle `Stretch, Bounce, Poyon` / role `STRETCH` / flavor `A fluffy, cloud-white visitor from somewhere far away.\nIts arms and legs stretch farther than you think,\nand it bounces off what it stomps on.\nNobody knows where it came from.`
再翻訳: 不要。

---

## 3. 反映する時の手順メモ(今回は未実施)

1. `Assets/Resources/Characters/<id>.asset` の subtitle/role/flavorText を新しい英語に直す(Builder は既にあるアセットを上書きしないため)。`CharacterDatabaseBuilder.cs` の Spec も同じ文に。
2. 30 言語の `Localization/<lang>.json` に、新しい英語の k を足す(ja は v=新しい日本語、en は v=k、他は訳)。古いキーは参照されなくなるだけなので、残しても動く。
3. 訳の手順(メモリの方針): 新しい文は 30 言語のレビュー TSV にして、エステルのレビューを通してから反映。
4. 訳が無い言語では英語が出る(Loc.cs:209-216)。訳が全部そろうまで前の文を出したい場合は、`Loc.Reviewed(new, old)`(Loc.cs:177-178)で包む手もある(今の CharacterSelectUI は Loc.Auto の直呼び)。
5. 影響する画面: キャラ選択(CharacterSelectUI.cs:412-415、Carousel.cs:118)、闘技場のキャラ欄(Arena/ArenaController.cs:408-411)。

### 再翻訳が必要な文の一覧(文案を全部採用した場合)

| キャラ | subtitle | role | flavor |
|---|---|---|---|
| 黒剣士 | - | - | 30 言語(任意) |
| 双剣士 | - | 29 言語(ja は英語のまま) | 30 言語 |
| お嬢様騎士 | - | - | 30 言語 |
| 拳銃士 | - | - | 30 言語 |
| 竜騎士 | - | 29 言語(案A/B の場合) | 30 言語 |
| 弓使い | - | - | 30 言語 |
| 魔法使い | - | - | 30 言語 |
| 格闘家 | - | - | 30 言語 |
| 忍者 | - | - | 30 言語 |
| 巫女 | - | - | 30 言語 |
| 吸血鬼 | 30 言語 | - | 30 言語 |
| 竜人 | - | - | 30 言語(任意) |
| ルカ / 宇宙人 | - | - | - |

---

## 4. 反映(2026-11-07、エステル最終案 estelle_reply_1.txt を全部採用)

- 型ラベル: 双剣士 GROUND COMBO → SWIFT COMBO、竜騎士 BREAKER → PIERCER。吸血鬼の一行 → 当て続けるほど、強くなる / Keep Hitting, Grow Stronger。黒剣士・竜人の任意の紹介文も採用。
- 英語の代名詞: 今の英語に合わせて直した(双剣士・拳銃士・忍者・竜人 = she、宇宙人 = it)。ルカは今の英語に代名詞が無く、他のデータにも性別が無いので、エステル案の he のまま。
- 日本語の改行: 撮影で測った紹介文の幅は横 468px(全角 21 字)・縦 403px(全角 18 字)。21〜24 字の行は「。」「、」だけが次の行へ落ちたので、1 行 18 字以内に付け直した(4〜6 行。6 行までは枠 170px に収まる)。英語は同じ行数に(merge_add.py の改行数の検査)。
- 新しい ID: 10617〜10642(Tools/loc/keys_extra41.tsv、Tools/loc/est41/src41_for_estelle.txt)。ja/en は反映済み。他の 28 言語は新しい英語のキーを持たないので、訳が入るまで新しい英語が出る(古い訳は出ない)。
- 2026-11-07 追記: 英語の紹介文 14 件を短い版(en_intro_short.tsv、3〜5 行・150 字前後)へ差し替え(同じ ID 10618〜10642、日本語はそのまま)。英語の行数は日本語の行数と一致しない(日本語は 18 字の幅に合わせた 4〜6 行)。ja.json の値は直接入れてあるので実行時は問題ないが、ja を merge_add.py で取り込み直すと改行数の検査で弾かれる。
