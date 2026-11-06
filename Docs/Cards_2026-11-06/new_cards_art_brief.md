# 新規カード 18 枚 絵の依頼書(エステルと相談して制作する用、2026-11-06)

今は手続き生成の仮のアイコン(MILE = 金の丸 / EXP = 水色の矢印 / リスク = 赤 / 複合 = 紫)で動いています。本番の絵ができたら下の場所へ置くだけで差し替わります。

## 置き場所と手順(今までのカードの絵と同じ)

1. エステルに **1024〜1254px・真っ黒の背景・文字なし** で描いてもらう(既存の 112 枚と同じ条件)。
2. `Tools/art/iconprep.py <生成.png> Unity/2ndAction/Assets/Art/Icons/CardIcons/<id>.png --preview <確認.png>` で 392×589・透明・既存と同じ配置へ(`--cy` は既存 0.41〜0.56)。
3. Unity の `Tools/OneMoreMile/Build Card Database`(batchmode: `-executeMethod CardDatabaseBuilder.Build`)。`CardIcons/<id>.png` があれば自動でその絵に切り替わります(コードの変更は不要)。
4. 40px / 64px の並び(`--preview`)で、既存の似た絵と見分けられるかを確認。

| id | 置く絵のファイル |
|---|---|
| combo_pay | `Assets/Art/Icons/CardIcons/combo_pay.png` |
| hunt_pay | `Assets/Art/Icons/CardIcons/hunt_pay.png` |
| boss_pay | `Assets/Art/Icons/CardIcons/boss_pay.png` |
| road_pay | `Assets/Art/Icons/CardIcons/road_pay.png` |
| survival_pay | `Assets/Art/Icons/CardIcons/survival_pay.png` |
| combo_training | `Assets/Art/Icons/CardIcons/combo_training.png` |
| hunt_training | `Assets/Art/Icons/CardIcons/hunt_training.png` |
| boss_training | `Assets/Art/Icons/CardIcons/boss_training.png` |
| road_training | `Assets/Art/Icons/CardIcons/road_training.png` |
| survival_training | `Assets/Art/Icons/CardIcons/survival_training.png` |
| danger_bounty | `Assets/Art/Icons/CardIcons/danger_bounty.png` |
| sky_siege | `Assets/Art/Icons/CardIcons/sky_siege.png` |
| crossroads | `Assets/Art/Icons/CardIcons/crossroads.png` |
| high_stakes | `Assets/Art/Icons/CardIcons/high_stakes.png` |
| chain_bounty | `Assets/Art/Icons/CardIcons/chain_bounty.png` |
| iron_marathon | `Assets/Art/Icons/CardIcons/iron_marathon.png` |
| trophy_hunt | `Assets/Art/Icons/CardIcons/trophy_hunt.png` |
| horizon | `Assets/Art/Icons/CardIcons/horizon.png` |

(仮のアイコンは `Assets/Art/Icons/Generated/Icon_<id>.png`。本番の絵を置いた後も残しておいて構いません)

## 全体の方針(エステルへの相談の起点)

- **2つの家族を色で分ける**: MILE の5枚(〜 PAY)= 金の硬貨が主役 / EXP の5枚(〜 TRAINING)= 水色の光る星(EXP の星)が主役。同じ条件どうし(COMBO PAY と COMBO TRAINING など)は**同じ形の小物**を使い、主役の色だけ違う = 「条件の形 + 報酬の色」で読めるように。
- **リスク ★3** は既存のリスクのカード(GREED / HELL MODE 等)と同じく赤黒の危険の光 + 報酬(金/水色)を小さく添える。
- **複合 ★4** は金と水色の両方(MILE と EXP の両方がもらえる)+ 紫がかった縁の光で ★4 らしく。
- 既存で硬貨を使う絵: EXECUTIONER(斧 + 弾ける硬貨)/ TREASURE HUNTER / ONE MORE MILE / MOB KILLER など。**剣・斧は使わない**(攻撃のカードに見えるため。10-31 の見直しで「剣が多く見分けにくい」とされた)。
- 既存で道を使う絵: LONG HAUL / THE LONG ROAD(金の道)。GREED は翼の靴。**道・翼の靴は新カードでは避ける**。
- 文字・数字は入れない(30 言語のため)。「10」などの数は、点や刻みの数で表す。

## 1枚ずつ

| # | カード | レア度 | 効果 | モチーフ案 | アイコンの形(40px で読む所) | 色 | 構図 | 既存との見分け |
|---|---|---|---|---|---|---|---|---|
| 1 | **COMBO PAY** / コンボ手当 | ★1 | 連続ヒット10回ごとに +1 MILE(Lv で増加) | 連続ヒットの火花の列が、最後に金貨へ変わる | 10 個の小さな光の点が弧を描き、終点に大きな金貨1枚 | 金・白 | 左下→右上へ弧、右上に金貨 | 剣を描かない。COMBO EDGE(斬撃の線が増える)と違い、線ではなく「点の列 + 硬貨」 |
| 2 | **HUNT PAY** / 討伐手当 | ★1 | 雑魚10体撃破ごとに +1 MILE | 倒した印(刻み)の束と金貨 | 5本の刻み × 2 束(=10)+ 金貨 | 金・灰 | 刻みの板を左、金貨を右手前 | MOB KILLER(雑魚への攻撃)と違い武器なし。「数える」印 |
| 3 | **BOSS PAY** / ボス手当 | ★1 | ボス撃破の MILE +12%/Lv | 小さな王冠の上に積まれた金貨 | 王冠 + 金貨の山 | 金・深紅 | 中央に王冠、上に硬貨 | BOSS RUSH/BOSS CHALLENGE(炎の王冠・2つの王冠)より小さく穏やか、炎なし |
| 4 | **ROAD PAY** / 走行手当 | ★1 | 1kmごとに +2 MILE | 道のりの標(キロポスト)と金貨 | 立った石の標(刻み1本)+ 横に金貨 | 金・石の灰色 | 標を中央、足元に金貨 | 道そのものは描かない(LONG HAUL/THE LONG ROAD と区別) |
| 5 | **SURVIVAL PAY** / 生存手当 | ★1 | 30秒ごとに +1 MILE | 砂時計の砂が金貨になって落ちる | 砂時計 + 下に金貨 | 金・琥珀 | 縦長の砂時計が中央 | LAST CHANCE/CLOSE CALL(無敵)の絵と砂時計が重ならないかエステルと確認 |
| 6 | **COMBO TRAINING** / コンボ修行 | ★1 | 連続ヒット10回ごとに EXP | #1 と同じ光の点の弧、終点に水色の星 | 10 個の点 + 水色の星 | 水色・白 | #1 と同じ構図 | #1 と並べて「同じ形・違う色」 |
| 7 | **HUNT TRAINING** / 討伐修行 | ★1 | 雑魚10体撃破ごとに EXP | #2 の刻みの束 + 水色の星 | 刻み 10 本 + 星 | 水色・灰 | #2 と同じ | EXPERIENCE BURST(撃破の EXP)は爆発の光 → こちらは静かな「数える」印 |
| 8 | **BOSS TRAINING** / ボス修行 | ★1 | ボス撃破の EXP +15%/Lv | 王冠の上に水色の星 | 王冠 + 星 | 水色・深紅 | #3 と同じ | #3 と色違い |
| 9 | **ROAD TRAINING** / 走行修行 | ★1 | 1kmごとに EXP | 石の標に水色の星が灯る | 標 + 星 | 水色・石の灰色 | #4 と同じ | PATHFINDER(距離の EXP、コンパス等)と形を変える(標) |
| 10 | **SURVIVAL TRAINING** / 生存修行 | ★1 | 30秒ごとに EXP | 砂時計の砂が水色の星の粒 | 砂時計 + 星の粒 | 水色・琥珀 | #5 と同じ | #5 と色違い |
| 11 | **DANGER BOUNTY** / 危険報酬 | ★3 | 雑魚が高火力敵(赤紫のオーラ)になる代わりに MILE/EXP | 赤紫のオーラの大きな拳(ゲームの高火力敵の印と同じ色)と、指の間からこぼれる金貨 | 赤紫に光る拳 + 金貨 | 赤紫・金 | 拳が中央手前、金貨が下へ | ゲーム内の高火力敵のオーラの色(赤紫)と同じにして「あの敵が増える」と分かるように。剣なし |
| 12 | **SKY SIEGE** / 空襲 | ★3 | 空中雑魚が増える代わりに EXP/MILE | 上空から急降下してくる鳥の群れの影と、赤い予告の線 | 3 羽の影 + 斜めの赤い線 | 赤・空色・水色の星 | 右上から左下へ斜め | AIR STRIKE(照準の輪)/ SKY MASTER(翼)と違い「群れが降ってくる」 |
| 13 | **CROSSROADS** / 分かれ道 | ★3 | 判断イベントが早く・多くなる代わりに MILE/EXP | 上下に分かれる矢印の標識(上 = 空の帯、下 = 地上の帯)、片方が赤く光る | Y 字の分岐矢印、片側赤 | 赤・白・金/水色 | 中央に Y 字 | ゲームの判断イベントの帯(赤い帯 + 矢印)の見た目に寄せる。道の絵にはしない |
| 14 | **HIGH STAKES** / 大勝負 | ★3 | ボスから受けるダメージ↑の代わりにボスの MILE/EXP | 割れかけたハートの前に積まれた大きな賭け金(金貨の塔)、背後に巨大な爪の影 | ひびの入ったハート + 金貨の塔 | 深紅・金 | ハート左、金貨右、爪の影は背景 | BOSS CHALLENGE(炎の王冠)と違い「自分が痛い」= ひびのハート |
| 15 | **CHAIN BOUNTY** / 連撃の賞金 | ★4 | 10HIT 以上の連続ヒット中の撃破で MILE/EXP | 光の鎖の輪が連なり、輪の中に金貨と水色の星 | 鎖 + 硬貨 + 星 | 金・水色・紫の縁光 | 鎖が S 字に画面を横切る | CHAIN EXPLOSION(爆発の連鎖)/ CHAIN LIGHTNING(雷)と違い、鎖そのもの + 報酬 |
| 16 | **IRON MARATHON** / 鉄人マラソン | ★4 | 被弾せずに 1kmごとに MILE/EXP | 傷ひとつない鉄の勲章(リボン)、周りに連続する刻み | 鉄の勲章 + 金と水色のリボン | 鋼・金・水色 | 勲章が中央に大きく | 靴・翼は使わない(GREED と区別)。HEAVY ARMOR/FORTRESS(鎧)と違い勲章 |
| 17 | **TROPHY HUNT** / 戦利品狩り | ★4 | ボス撃破で、前のボスからの撃破数に応じて MILE/EXP | 小さな敵の印(どくろ型の札)で満ちた優勝杯 | 杯 + 小さな札がたくさん | 金・水色・紫の縁光 | 杯が中央、札があふれる | TREASURE HUNTER(宝箱)と違い杯。BOSS PAY(王冠)と違い「数がたまる」 |
| 18 | **HORIZON** / 地平線 | ★4 | 10kmごとに MILE と EXP が伸びる | 地平線から昇る、半分が金・半分が水色の大きな太陽 | 水平線 + 2色の太陽 | 金・水色・夜明けの紫 | 下 1/3 に水平線、中央に太陽 | 道を描かない(LONG HAUL/THE LONG ROAD と区別)。ONE MORE MILE とも違う形 |

## エステルに確かめてほしいこと

1. 金(MILE)と水色(EXP)の 2 色の家族分けで、40px でも読めるか。
2. 同じ条件の 2 枚(例: COMBO PAY / COMBO TRAINING)が「同じ形・違う色」で並んで分かるか。
3. 既存の 112 枚(特に EXECUTIONER / TREASURE HUNTER / ONE MORE MILE / LONG HAUL / THE LONG ROAD / LAST CHANCE / GREED / BOSS 系)とぶつからないか。
4. リスク ★3 の赤と、複合 ★4 の紫の縁光でレア度の違いが伝わるか。
