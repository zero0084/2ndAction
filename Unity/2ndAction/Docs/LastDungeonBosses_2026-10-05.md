# ラストダンジョンのボス構成(2026-10-05)

調整値: `Assets/Resources/Bosses/LastDungeonBossTuning.asset`(`Bosses/LastDungeonBossTuning.cs`、Inspector で編集)。
処理: `BossManager.LastDungeon.cs`(遭遇の管理/節目の抽選/ボスラッシュ)。

## 0〜89km: 節目のボス(過去マップのボスを格ごとに再登場)
| 格 | 関門 | プール |
|---|---|---|
| 1,000m 格 | 5 の倍数でも 10 の倍数でもない km | 荒野 Wolf / 洞窟 Centipede / 天空 Dragon |
| 5,000m 格 | 5 の倍数(10 の倍数を除く) | 荒野 GoblinRider / 洞窟 Scorpion / 天空 Majin |
| 10,000m 格 | 10 の倍数 | 3 マップの 10km ごとの専用ボス 27 体 |

- 抽選は戦闘の開始時(HOST/シングル)。直近 `recentExclude`(3)体を外す。候補が少ないプール(3 体)は「候補−2」まで緩める(=直前の 1 体だけ外す)。
- 製品版は「会ったことのあるボス」(`ProgressStats.BossSeen`、保存キー `BossSeenV1`)だけから選ぶ。会ったボスが `minSeenCandidates` 未満の格は全部から。開発版は全部。大きなセーブの仕組みは不要(カンマ区切り 1 キー、DEBUG RUN では保存しない)。
- 強さは関門の距離に合わせる(再戦と同じ式: 初登場距離 → 今の距離の節目 HP の比 × 再戦の段階)+ラスダンの倍率。
- ラン再開あり(`milestoneRunResume`)。ラン再開したボスが残ったままボスラッシュの入口(90km の `rushEntranceHoldMeters`=300m 手前)まで来たら再び足止め(ボスラッシュの関門を飛ばさない/保留にしない)。

## 90〜98km: ボスラッシュ(1,000m ごと、99km までに終わる)
各関門に 2 つの構成があり、どちらかを抽選。出し方: 同時(Start)/時間で増援(Time 秒)/HP で増援(出ているボスの合計 HP の割合)/撃破で次(OnKill)。誰も居なくなったら条件を待たずに次を出す(棒立ちにしない)。

| km | 体数 | 構成 A | 構成 B |
|---|---|---|---|
| 90 | 2 | Serpent + Fenrir(地上+空中 同時) | Troll → Griffin(撃破で次) |
| 91 | 2 | Cyclops +(8s) Bat | Behemoth +(HP60%) Basilisk |
| 92 | 3 / 2 | Mole + Griffin → Titan | Spider +(7s) Jellyfish |
| 93 | 3 | Golem +(HP55%) CrystalGolem → SkyGolem | Worm + Fenrir +(10s) Hydra |
| 94 | 3 | ScorpionKing + Demon +(9s) Phoenix | Leviathan + Troll → Cyclops |
| 95 | 4 / 3 | Hydra + Drake +(HP60%) Jellyfish → Serpent | SkySerpent +(6s) Basilisk → Golem |
| 96 | 4 | SkySerpent + Spider +(8s) Demon → Bat | Titan + Mole +(HP50%) Griffin → CrystalGolem |
| 97 | 4 | AncientDemon + Guardian +(HP55%) Demon → Fenrir | Drake + Leviathan +(9s) Hydra → ScorpionKing |
| 98 | 5 | BlackKnight + Phoenix +(8s) Basilisk +(HP50%) Guardian → AncientDemon | Guardian + Griffin +(HP60%) AncientDemon +(12s) BlackKnight → SkySerpent |

- 同時に戦うのは最大 `maxSimultaneous`(3)体(処理落ち対策)。
- 無理な組み合わせを作らない: タグ GROUND / AIR / LARGE / PROJECTILE / CHASER / AREA_ATTACK(+ NoRush)で同時の上限(大型 2 / 空中 2 / 範囲攻撃 1 / 飛び道具 2 / 追跡 2)。超える時は先のボスが倒れるまで待たせる。構成表も手で安全な組み合わせにしてある。
- 竜(荒野/天空)と魔人は別の仕組みのボス(撃破の数え方が違う)なのでボスラッシュに入れない(NoRush)。節目のボスには出る。
- ボスラッシュの 1 体の HP = 距離に合わせた HP × `hpMul` × `rushHpMulPerBoss`(0.3。0.55 では1関門 69〜158 秒と長すぎた)。被弾量は `rushDamageMul`(1.4)。
- 90〜99km は雑魚なし(従来どおり)。99〜100km は静寂、100km は三姉妹(変更なし)。

## ラストダンジョンの倍率(全関門)
| 項目 | 既定 | 掛かる所 |
|---|---|---|
| HP `hpMul` | 1.25 | 生成時の HP |
| 攻撃の頻度 `attackFrequencyMul` | 1.2 | 特殊攻撃/必殺技の間隔を割る |
| 攻撃の間隔 `attackIntervalMul` | 0.85 | 攻撃と攻撃の間の待ち時間(予告=構えの時間は変えない) |
| 移動速度 `moveSpeedMul` | 1.1 | 接近/間合いの移動/突進 |
| 被弾量 `damageMul` | 1.0 | 再戦の段階の倍率に掛ける |

(竜/魔人は HP だけ。攻撃間隔/移動速度は WildBossBase 系のボスに掛かる。)

## 遭遇の管理(1 体前提をやめた)
`BossManager.CurrentBossEncounter`(すべての関門で作る): `activeBosses`(出ているボス)、`waiting`(まだ出ていない増援)、`total/spawned/defeated`、`RemainingBossCount`。
撃破は `OnWildBossDefeated(this)` でどのボスかを受け取る。待っている増援も残り数(aliveWild)に入れるので、増援を出す前に関門が終わらない。

- 「ボスが残っている扱いで次が出ない」の対策: 撃破の処理を通らずに消えたボス(破棄)は見張りが残り数から外す(`watchdogFixes`)。
- ラン再開との整合: ボスラッシュはラン再開しない(倒すまで距離が止まる → 関門を飛ばさない/重ならない)。節目のボスはラン再開+保留 1 つ(従来どおり)+ボスラッシュの入口で足止め。ワープ/CONTINUE では遭遇を持ち越さない(`RestoreNextBossDistance`)。
- NEXT BOSS(開発用、DEBUG RUN): 遭遇のボスを全員(待っている増援も)一度で片付けて関門を終わらせ、次の関門の手前へ。

## 確認
`LastDungeonQa -ldQa <dir> -ldQaMode bosses`(A プール / B 抽選 / C 節目の関門 / D ラン再開と入口の足止め / E 90〜98km 各関門 / F NEXT BOSS / G 見張り / H 98km 後は静寂)、`-ldQaMode flow`(従来のボスラッシュ→静寂→三姉妹の通し)。
