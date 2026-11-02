# 空中で出せる技と出る絵の監査(2026-11-02、コードの読み取りのみ)

PlayerAnimator は攻撃の状態で「空中かどうか」を見ていない(地上の攻撃の絵がそのまま空中に出る)。魔法使いは常に地上扱い。
JumpStart / DoubleJump は 18fps(1枚の絵は 0.056 秒で消える)→ 今回 0.28 秒保持に修正。

## 地上の踏ん張りの絵が空中に出る技(空中用のコマが必要)
| キャラ | 技(空中での入力) | 今の絵 |
|---|---|---|
| 黒剣士 | 前/後の1〜3段目、地上からの↑(上昇中) | PlayerAttackSmall/Attack/Large_v1、PlayerUpAttackGround_v1 → **空中用を作成済み** |
| 双剣士 | 前/後の1〜3段目 | DualBladeAttackSmall/Mid/Large_v1 |
| 拳銃士 | 前/後の射撃 | GunslingerAttackSmall/Attack/Large_v1 |
| 竜騎士 | 前の突き/後ろの石突き | LancerThrust_v1 / LancerBack_v1 |
| 弓使い | 前/後の引き絞り・放つ | draw / release |
| 格闘家 | jab/straight/kick/heavy、backstep | 各 p0/p1 |
| 忍者 | dashslash/slashend(前)、throw(後) | 地面の線までの踏み込み |
| 巫女 | throw(前)/shiki(後)/place(下: 空中で膝つき) | place_p1 が最も目立つ |
| 吸血鬼 | claw1/claw2/bloodslash | 各 p0/p1 |
| 竜人 | claw1〜3、tail | 各 p0/p1 |
| ルカ | paw1/paw2、tail | 3本の脚で接地 |
| 宇宙人 | stretch1/stretch2/pokapoka/bump、upreach(毎ジャンプ) | 足を地面につけた絵 |

## 攻撃の絵が出ない(棒立ち)
- 黒剣士/双剣士/お嬢様: 急降下を↑で止めた時 → **AirUpAttackShown で二段目の上攻撃の絵を出すよう修正**
- 双剣士の上攻撃(1/2回目)・お嬢様の2回目: 1枚の絵が 0.056 秒 → **0.28 秒保持に修正**

## 逆の不一致
- 格闘家 divekick の着地(p1): 空中の飛び蹴りの絵が地上で出る
- 弓/拳銃士のホバー射撃の後は着地の専用絵なし

(詳細な表はサブエージェントの調査結果。お嬢様は air/plunge の専用絵があり問題なし。魔法使いは浮遊のため対象外)
