# マルチプレイ 最大8人へ向けた構造メモ(2026-10-02)

当面の開発・確認は2人で続ける。ただしネットワーク関連のコードは「2人専用」にせず、2→4→8人へ増やせる形で書く。
今回はAuthority構造(HOST権威)は変えていない。

## 1. 人数の設定

- `NetSession.MaxPlayers`: 部屋の上限。既定は2(`DefaultMaxPlayers`)。
- `NetSession.PlannedMaxPlayers` = 8: 将来の上限。
- 開発版(Editor/Development Build)だけ、起動引数 `-netMaxPlayers N`(2〜8)で上限を変えられる。4人/8人の試験に使う。
- 番号(P1〜P8)は HOST が空いている一番小さいスロットから割り当てる(`NetPlayer.Slot`、`PlayerNumber = Slot + 1`)。

## 2. 人数に依存しない作りになっているもの(調査で確認済み)

| 項目 | 実装 |
|---|---|
| Ready / カウントダウン | `ConnectedClientsIds` 全員の Ready を待つ(30秒で打ち切り) |
| ALIVE / DOWN / ELIMINATED | `NetMatch.recs`(番号→Rec の辞書)。全員が非ALIVE で RunOver |
| 敵のターゲット | `NetTargets` / `EnemyTargetSelector`。活動中の全プレイヤーから選ぶ |
| 撤去 / 諦め判定 | `NetCombat.RangeSceneX` / `RearmostAlivePlayerX`(全員の範囲) |
| LastHit / 報酬 | `LastHitPlayer` は番号(1〜8)。VERSUS は LastHit 本人だけがビルド報酬 |
| カード / EXP / ビルド | 各端末のローカル(人ごと) |
| CO-OP 復活 | `CanRevive(down, donor)` は任意の2人の組み合わせ。`RequestRevive(pn)` で誰を助けるかを指定する(HUD は倒れている人ごとにボタン) |
| VERSUS 結果 | `FinalDistance` を距離順に何人でも並べる |
| 相手の表示 | 相手の NetPlayer ごとに `RemotePlayerAvatar` を1つ作る |

## 3. 今回直したもの(2人のプレイは変わらない)

1. **`PlayerNumberOfClient` が不明な時に 2 を返していた。**
   - 3人目以降の、まだ番号が届いていない接続が P2 として扱われる恐れがあった。
   - 今は 0(不明)を返し、呼び出し側(命中申告・HP申告・障害物)はそのメッセージを捨てる。
   - JOIN の hello は、HOST の表に自分が載るまで 1.5 秒ごとに送り直す。
2. **雑魚の出現の基準が HOST 自身の位置だった。**
   - HOST が DOWN / 脱落すると、全員の前に敵が出なくなっていた(2人でも起きる不具合)。
   - 今は活動中のプレイヤーの最前を基準にする(`NetCombat.ForemostPlayerX`、障害物と同じ方式)。
3. **誰かが抜けると、HOST に毎回「接続が切れました」の全画面が出ていた。**
   - 他に参加者が残っている時は「P3 が切断しました(残り2人)」の通知だけにした。
   - 誰も残っていない時(2人の時)は従来どおりの全画面。
4. **ラン中に途中参加できてしまっていた。** 名簿に無い人は Ready を送れない。また、空いたスロットに前の人の記録(Out)が残る。マルチのランのシーン中は `RUN IN PROGRESS` で断るようにした。
5. **攻撃の状態のまとめ送り(`NetAttackSync.SendStates`)**
   - これまでは8件固定で 1300B の枠に書いていた。見た目が多い攻撃だと枠を超えて例外が出得た(2人でも起き得る)。
   - 今は大きさを見積もって 1200B 程度で区切る。枠も伸びるようにした。
6. **CO-OP の HUD**
   - REVIVE ボタンは倒れている人ごとに出す(3列で折り返し)。枠の高さは人数で伸びる。
   - 文言を「HP 2+」から `PlayerHit+1`(11+)にした。
   - 復活できるかどうかの記録は、3人以上では変化した時だけ出す。毎秒×組み合わせ数でログが増えないようにした。
7. **文言・表示**
   - 「2台を接続」を「最大N人」にした。
   - ラン中の上部表示は相手ごとに1行。
   - 「相手プレイヤーがまだ走っています」を「まだ走っているプレイヤーがいます」にした。
8. **相手同士の重なり**: 番号の小さい人が手前に来るよう、z を少しずらした(毎フレームの入れ替わりで、ちらつかない)。
9. **自動テスト**
   - `-netAutoPlayers N` で N 人の接続を待ってから開始する。
   - 3人以上の時は、全員分の行(`remotes t=`)をログに出す。

## 4. 計測(4人/8人の試験で比べるもの)

`NetStats`(Scripts/Net/NetStats.cs)が、自前の送受信を種類別に数える。対象は全ての NamedMessage(`NetStats.SendNamed` / `NetStats.Counted` を通す)と、位置スナップショットの RPC(概算 101B / 件)。1秒ごとの値を出す。

- 送信 / 受信 KB/s・件/s(種類別)
  - `OMM.*` の各メッセージ
  - `snapshot`
  - `snapshotRelay`: HOST が JOIN の位置を他の JOIN へ中継する分
- フレーム時間(直近1秒の平均 ms)
- 同期中の雑魚 / ボス / 飛び道具・攻撃の数、表示中の相手の人数

見る場所:
- DebugMode のラン中、左下の `NET COMBAT` パネル。
- NetAutoTest のログ。毎秒の `load t=...` 行。

新しく通信を足す時は、必ず `NetStats.SendNamed` で送り、`NetStats.Counted` で受ける(計測から漏れないように)。

## 5. 8人にする時に大きな作り直しが要りそうな所(今回は直していない)

| 箇所 | 今の前提 | 8人での問題 | 方針案 |
|---|---|---|---|
| 位置スナップショット(`NetPlayer.SnapshotRpc`、30Hz、NotOwner) | JOIN→HOST→他の JOIN へ中継 | 中継が (N-1)(N-2) 件/回。8人で HOST の送信は約 1.2Mbit/s | 送信間隔を人数で下げる / 遠い人は間引く / まとめ送り |
| ~~ボス関門の距離~~ | → **3.1で対応**(WorldFront) | | |
| ~~天空系の「画面内にいるか」(`SkyOnScreen`)~~ | → **3.1で対応**(参加中の誰かの画面) | | |
| ワームなどの先回り / 天空ボスの飛び道具の流れ | HOST の速さ | 速さの違う人がいると位置がずれる | 天空ボスの2か所は3.1で狙いの相手の速さへ。残りは個別に確認 |
| HOST のボス戦中のレベルアップ | HOST はボス戦の間ずっと後回し(JOINにはボスフェーズが無いので開く) | 人によってボス戦中のカード選択の有無が違う | ボス戦の状態をJOINへも配るか、ルールを揃える(ゲームルールの判断が要る) |
| 天空ボスの一部の攻撃(風で押す等) | HOST 自身だけに効く処理がある | JOINには効かない | 被弾申告と同じ形でJOINへも届ける |
| 100,000m の三姉妹/ラストダンジョンの流れ | HOST の `gm.MaxDistance` | 先頭がJOINだと始まりが遅れる | 台本の流れが大きいので別途(3.1では触っていない) |
| JOIN の地形生成 | 自分の周りだけ作る | 遠くの人の所の敵・攻撃が地形の無い場所に見える | 表示だけの軽い地形を作るか、遠い人は簡易表示にする |
| `LocalPlayerNumber` | スロットが届く前は 1 | 接続直後の一瞬、P1 と名乗る可能性 | 0(不明)を経由させる |
| HOST の負荷 | 敵 AI・命中・HP を全部 HOST で計算 | 8人で敵数と命中の申告が増える | まず計測(4章)で4人の値を見てから判断 |
| カメラ / 観戦 | 自分(DOWN 中は相手1人) | 観戦先を選ぶ UI が無い | 観戦先の切り替えを付ける |

## 6. 試験

- 2人(従来どおり): CO-OP の被弾・ダウン・復活・戦闘、VERSUS の脱落・結果、ボスの再戦プール。
- 3人(開発用、`-netMaxPlayers 3 -netAutoPlayers 3`): HOST + JOIN×2 の CO-OP で、接続・開始・被弾・復活・送受信量を見た。

---

# Phase 3.1 World Streaming(2026-10-02)

目的: 「HOSTがどこにいるか」ではなく「走っているプレイヤー全体がどこまで世界を進めているか」で世界を動かす。
Authority は今まで通り HOST(全員の位置を HOST が把握 → HOST が判定 → HOST が敵/ボスを出す → 同期)。最前の JOIN が敵を出す形にはしていない。

## WorldRange(Scripts/Net/WorldRange.cs)

| 名前 | 内容 |
|---|---|
| `GetActivePlayers()` | このRunに参加中の人(ALIVE + DOWN)。脱落/退出/切断/Run終了済み/分身がまだ無い人(シーン読み込み前)は含まない |
| `GetAlivePlayers()` | ALIVE の人。カード選択中の人も含む(その場に止まっているだけ) |
| `WorldFrontDistance` / `WorldFrontSceneX` | ALIVE の最前。雑魚の出現・ボスの関門・ボスの登場位置の基準 |
| `WorldBackDistance` / `WorldBackSceneX` | ALIVE の最後尾。取り残された敵の片付け・出現記録の掃除の基準 |
| `FrontRunning()` | 最前の「走っている」人(カード選択中を除く)。ボスの登場位置 |
| `VisibleToAnyone(x)` | 参加中の誰かの画面に映っているか(全員同じ構図とみなす) |

- 距離は「HOST の距離の物差し」= HOST の距離 + (その人の X − HOST の X)。人によって距離の数え方が違っても(ボス戦中の除外など)、HOST が出す物の距離の帯・関門とずれない。
- ALIVE が誰もいない時(全員 DOWN 直後など)は、参加中 → 自分 の順に代わりを使う。
- シングルでは自分1人なので、`WorldFrontDistance` = 自分の距離(従来と同じ)。
- 人数は任意。P1/P2 を名指しせず、一覧から求める。同じ X なら番号の小さい方(全端末で同じ結果)。

## 雑魚の出現(EncounterDirector)

- 基準は `WorldRange.Front`。HOST 自身は、自分が最前の時だけ基準になる。
- 最前が相手の時の補正:
  - 分身の表示は通信と補間のぶん遅れて見えるので、その人の速さ × 0.3 秒だけ先を基準にする。
  - 先読み距離は、その人の走行速度から求めた速度倍率で伸ばす(従来の `spawnAheadDistance × 速度` の考え方は維持)。
  - 画面の見える範囲は、画面の幅いっぱいを上限として見込む。
- 距離の帯・分岐・BONUS・`SuppressAt`・開始直後の安全区間は、すべて `RunDistanceAt`(HOST の物差し)で判定する。
- **二重生成の防止**:
  - 基準点 `nextAnchor` は前へしか進まない。
  - それに加えて、決めた出現位置(1m 単位)を HOST の `plannedAnchors` で一意に管理し、同じ位置は二度と決めない。防いだ回数は `DuplicateAnchorsBlocked`。
  - 分岐は従来どおり `lastBranchForkLogical` で一度だけ。
- 記録(自動テスト/デバッグ用): 出現を決めた時の最前の人(`SpawnsByFront`)、最前の人から何 m 先に出したか(`MinSpawnLead` / `LastSpawnLead`)。

## 敵の片付け(従来の考え方を維持し、一覧から計算)

- `NetCombat.CleanupPassedEnemies`: ALIVE 全員の最後尾より `BehindDespawnDistance` 後ろの雑魚だけ消す。
  - 先頭が通過しただけでは消えない。
  - DOWN / 脱落の人は数えないので、倒れた人が片付けを永久に止めることはない。
- CO-OP で先頭が DOWN した場合: DOWN 地点の周りの敵は、後ろの ALIVE の人より前にあるので消えない(救出に向かえる)。
  - DOWN の人の足場は従来どおり残す(`RearmostPlayerX` は ALIVE + DOWN)。
- ボス戦の開始時の雑魚の片付け:
  - シングルは全部(従来どおり)。
  - マルチは「ボスが出る所」(最前で走っている人の 30m 後ろから先)だけ。後ろの人がこれから戦う雑魚は残す。
- 分身の位置は「位置が届いているか」で判定する(以前は絵の表示/非表示で判定していた)。

## ボスの関門と登場(BossManager)

- 関門: HOST の距離ではなく `max(HOST の距離, WorldFrontDistance)` が関門に着いたら始める(マルチの HOST のみ)。
  - HOST が後方の時は、HOST の距離を関門へ合わせて動かさない(以前の 30m 以内の補正は、HOST が関門を少し越えている時だけ)。
- 登場位置: 最前で走っている人(`FrontRunning`)の画面の右端の外から。
- ボスの「画面の外」の間合い(`OffscreenAheadGap` / `OffscreenBehindGap`): 狙っている相手の画面を基準にする。
  - 以前は HOST のカメラの端そのものだったため、遠くの相手を狙うボスが急降下などで HOST の画面の端へ飛んでいた。
- 天空ボスの登場演出の位置、飛び道具の流れる速さも、狙っている相手を基準にした。

## ボスの置き去り防止(Scripts/Net/BossLeash.cs、マルチの HOST だけ)

ボスは狙っている相手(最も近い人)と並走する。全員が同じ速さで走るので、カード選択などで一度止まった人は、以前は二度と追いつけなかった。瞬間移動ではなく、速さと狙いで直す。

1. **減速**: 狙いの相手より後ろに、走っている ALIVE の人が `LeashStart`(16m)以上離れていたら、並走を 10〜20% 遅くする。
   - 先頭の人がボスを追い越すか、減速が `FrontFightTime`(6秒)続いたら、狙いを最後尾の人へ替える。
   - 最後尾の人へ向かう間は、`ArriveGap`(12m)まで近づくまで狙いを固定する(行ったり来たりしない)。
2. **戻り方**: 間合いから 6m 以上外れた時(狙いが替わった時など)だけ、速度で間合いへ戻る。
   - 後ろへ戻る時は、相手の速さの 75% までに抑える(後ろの人を待つように減速して見える)。前へは最大 22m/s。
   - 6m 以内は従来どおりの間合いの制限(攻撃の動きは変えない)。
3. **位置の補正**: 誰の画面にも映っていない時に限り、間合いから 70m 以上外れていたら、狙いの相手の画面のすぐ外まで直す。
4. **カード選択中の人を待つ**(`EnemyTargetSelector.waitRange` = 40m):
   - 狙っていた人がカード選択に入った時、他の人が全員 40m より遠ければ、その場で止まって待つ(最大12秒)。
   - 選択中の人は狙い/被弾の対象外なので、攻撃は当たらない。
   - 遠くの人へ乗り換えて、画面外の補正で行ったり来たりしないため。

- 待つ相手に数えないのは、カード選択中の人(選び終えて走り出すと 1 が働く)と、DOWN / 脱落の人(倒れた人のためにボスを止めない)。
- 何もしない場面: 後ろの人が十分近い時(16m 以内)。シングル / JOIN。
- VERSUS:
  - 先に進んだ人が先にボスと戦うのは残る(減速の間、約6秒)。
  - その後ボスは後ろの人へ向かうので、先頭の人はボスを抜けていく。
  - 変わるのは「離れた人が物理的に二度と追いつけない」状態だけ。
- 比較用の起動引数(開発版): `-netAutoNoLeash`(3.1以前と同じ動き)、`-netAutoLegacyAnchor`(出現の基準を HOST 自身に戻す)。

## 負荷の計測(Debug の NET COMBAT パネル / 自動テストの `load t=` 行)

`NetStats.LoadLine()` で、以下を1秒ごとに出す。

| 分類 | 項目 |
|---|---|
| プレイヤー | 接続数、参加中、ALIVE |
| この端末の総数 | 雑魚、ボス、飛び道具(BossProjectile / Fireball / PlayerBullet) |
| AI | この端末で AI が動いている敵/ボスの数(HOST / シングル。JOIN はパペットなので0) |
| 共有 | 共有している雑魚 / ボス / 攻撃の数 |
| 通信 | 送受信の件/s・KB/s(種類別は `KindSummary`) |
| Ping | 平均 / 最大 / この起動中の最大(UTP の RTT。HOST は全 JOIN、JOIN は HOST まで) |
| 描画 | FPS、フレーム時間の平均 / 最大 |
| 世界 | `WorldRange.Describe()`(最前 / 最後尾 / 各自の状態と距離) |

数はシーン内を1秒に1回だけ数える(毎フレームは数えない)。

通信量について(要望の19番):
- 敵の状態は、従来どおり「変化した時だけ」送る(遠い敵は間引いた生存確認だけ)。
- 死んだ敵 / 無効な敵は送らない。
- 画面外の VFX(雑魚の演出など)は同期しない。

既知の課題: 2人の距離が数 km 離れると、間にいる敵が全部「近い」扱いになり、同期数が増える(8人化の前に対策が必要)。

## 3.1 の試験(Windows 2プロセス、2026-10-02。Android 実機は未確認)

すべて exceptions=0 / errors=0。テスト台本は scratchpad の `net8/p31.sh`。

| 試験 | 主な結果 |
|---|---|
| HOST がカード選択で30秒停止 | JOIN の前に敵が出続けた(敵が前に無い秒 9/61、最長6秒)。旧方式(`-netAutoLegacyAnchor`)では 47/61、最長47秒 |
| HOST が DOWN、JOIN だけ走る | 最前=P2 で出現が続いた(最長7秒) |
| JOIN が常に120m前(2倍速、200秒) | 1000m / 2000m の関門は P2 の到着で開始(HOST は 877m / 1882m、HOST の距離は動かさない)。LastHit は P2 90回 / P1 24回、共有の状態は両端末で一致 |
| 先頭の入れ替わり(25秒ごと、150秒) | 二重生成0、二重防止の発動0、ボス1体、出現の基準 P1 10回 / P2 8回 |
| 3倍速 + JOIN が100m前 | 画面内に湧いた敵 0。最前の人から最短 51m 先に出現。JOIN が初めて見た敵は最短 37.9〜49.7m 先 |
| CASE A(HOST がボス戦中にカード選択8秒) | 41m 離れて、9.2秒後に再合流 |
| CASE B(JOIN がボス戦中にカード選択8秒×2回) | 42m / 40m 離れて、10.8秒 / 11.3秒後に再合流 |
| CASE C(130m 差を保ったままボス、CO-OP / VERSUS) | 先頭が約9秒戦った後、後ろの人へ。12.3秒後に再合流。ボスと最寄りの人の差は最大30m |
| 置き去り防止なし(`-netAutoNoLeash`)の比較 | 相手のカード選択の瞬間に、ボスが約35m 瞬間移動して戻るだけ(偶然が無ければ戻れない) |
| 再戦プール(4倍速、2人が数百m〜1km離れる) | ボスと最寄りの人の差は最大45m、位置の補正1回(修正前は最大1142m、補正1229回 / 4回) |
| CO-OP: 先頭が DOWN → 後ろの人が救出 | DOWN 地点の周りの敵は残り、救出できた |
| CO-OP 回帰(被弾・DOWN・HP譲渡・ボス) | 正常 |
| VERSUS: P1 脱落 → P2 だけ走る | 最前は翌秒に P2 へ切り替わった。結果は距離順 |
| シングル(QaSweep `-qaBoss` / `-qaBranch`) | 合格 |
| シングル(`-qaEncRuns`) | 「ボス撃破後に通常の敵が戻る」が時々不合格。3.1 以前のビルド(commit 10a6cfa)でも同じく不合格。ボス直後に BONUS ZONE が始まり、数える16秒の間は通常の敵が止まるため(既存の試験のタイミングの問題) |

# TODO: マルチ完成工程での必須の修正

## JOIN Player にもカード Build の実戦効果を正しく適用する(2026-10-04、カードバランス v3 で判明)

**今の状態**
JOIN のプレイヤーでは、次のカードの効果が働かない。
- 属性(炎上 / 冷気・凍結 / 落雷・連鎖 / 風刃・竜巻 / 出血)
- 追加攻撃(DOUBLE ATTACK / SHOCKWAVE / PIERCING BLADE の近接 / AERIAL BLADE / COMBO MASTER / GROUND BREAKER / SONIC BLADE / CHAIN EXPLOSION / INFERNO / COUNTER・FLAME COUNTER の反撃)
- PHOENIX / SECOND WIND / LAST CHANCE

**原因**
- 敵 / ボスの HP を決めるのは HOST だけ(`NetCombat.Authority`)。
  - 属性と追加攻撃は、HP を持つ端末でだけ判定している(`ElementSystem.Authoritative`、`CardProcs.OnPlayerHit`)。
  - JOIN から HOST へ届くのは命中のダメージの値だけ。
- JOIN の HP も HOST が決めている(被弾申告)。
  - 倒れる被弾の取り消し(PHOENIX)と低HPの回復(SECOND WIND)/ 無敵(LAST CHANCE)は、HOST の判定の中に入っていない。

**JOIN でも効いているもの**
攻撃力 / 条件 / 速度 / 攻撃速度 / 範囲 / ジャンプ / Shield(被弾の申告の前に自分の端末で消費)。

**直す方向(案)**
- JOIN の命中の申告に、そのプレイヤーの属性の値と追加攻撃の Lv を載せる(または HOST が各プレイヤーのカードの合計を持つ)。
  - HOST が、そのプレイヤーの分として属性/追加攻撃を判定する。
- JOIN の被弾を HOST が確定する時に、そのプレイヤーの PHOENIX / SECOND WIND / LAST CHANCE の状態を見て処理する。
- カードバランス v3 の調整と、ネットワーク同期の大きな変更は同時に行わない方針のため、今回は未対応。

## #100 ULTIMATE をマルチで使えるようにする(2026-10-04、ULTIMATE の実装で未対応として記録)

**今の状態**
マルチ(`NetMatch.Active`)では ULTIMATE を使えない。
- レベルアップの候補に出ない(`UltimateArt.Offerable`)。
- キャラカード枠などで持っていても、ボタンは「MULTI×」で発動しない(`UltimateArt.CanActivate` が「マルチ未対応」)。Gauge も溜まらない。

**使えるようにする時に必要なこと(原因)**
- 前進(100〜200m)は「着地点の足場を平らにする」(`TerrainManager.SetResumeFlatZone`)を使う。
  - マルチの地形は全端末で同じ順に生成する(`UpdateDeterministic`)。1人だけ足場を変えると地形が食い違う。
- 敵 / ボスの HP は HOST だけが決める。
  - JOIN の ULTIMATE のダメージは HOST へ申告して HOST が当てる必要がある(属性/追加攻撃の TODO と同じ経路)。
- 出現 / 関門 / ボスの間合いは、先頭のプレイヤー(`WorldRange` / `BossLeash`)が基準。
  - 1人が 200m 先へ出ると、他の人が取り残され、関門や雑魚の出方も変わる。
- 着地後の安全区間(`GameManager.UltimateSetSafeUntil`)は端末ごと。

**直す方向(案)**
- 発動は HOST が決める: JOIN は要求だけ送る。HOST が開始の時刻を全員へ配る(RunState と同じ時計)。
- 前進は「全員で一緒に進む」か「前進なし(または WorldRange の前端まで)」にする。地形の安全区間は、決まったチャンクの番号で全端末が同じように予約する。
- ダメージは HOST の権威で当てる(`NetCombat.AuthorityDamaged`)。

## FINAL EVOLUTION(2026-10-04 第1段階): マルチでは未対応(候補に出さない)

`FinalEvolutionTuning.disableInMultiplayer = true`(既定)で、マルチのランでは LEVEL UP の候補に FINAL EVOLUTION を出さない。
資格/READY の判定(ローカルの距離と能力Lv)は走るが、発動しないので効果は一切乗らない(既存のマルチの同期には触れていない)。

対応に必要なこと:
- 状態の持ち主: 各プレイヤーの FINAL EVOLUTION はその本人の端末が決める(カード選択と同じ)。HOSTの表(NetMatch)へ「誰が何を ACTIVE にしたか・残り」を送り、他の端末はオーラ等の見た目だけ再生する。
- 効果の同期:
  - ATTACK UP / ATTACK RANGE UP: 本人の攻撃力・射程だけなので、JOIN の命中は HOST へ届くダメージ値に乗る(今の申告方式で足りる)。斬撃波(KitProjectile)は HOST にしか当たり判定が無い → JOIN 側で出した斬撃波の命中を申告する経路が要る。
  - SPEED UP: 接敵の自動小攻撃は HOST だけが静かなダメージを入れる(JOIN は今スキップ)→ JOIN の分は HOST へ申告が要る。接触/障害物の保護は被弾申告の前で弾けば足りる。
  - VAMPIRE / PHOENIX / GREED: HP は HOST 権威(NetMatch)なので、Blood Shield・緊急復活・被ダメージ倍率は HOST の被弾確定の処理へ同じ判定を入れる必要がある(今は本人の TryDamagePlayer だけ)。
  - FLAME / THUNDER: 属性は HOST だけで判定(既存の課題と同じ)。JOIN の命中には乗らない。
  - EXP / MILE: 本人の端末の取得計算なので、そのまま使える見込み。
- CONTINUE はマルチでは使わないので不要。

### FINAL EVOLUTION 第2段階(2026-10-05: 再使用 + 全99枚)でも、マルチは未対応のまま(候補に出さない)
- 第2段階で増えたもの: 終われば READY へ戻る(何度でも)/ 99枚それぞれの「増幅(そのカード自身の効果×amplify)+追加(EffectType)」/ 同じ FE は重ならないが別の FE は同時に ACTIVE。
- 増幅/追加は `GameManager.RecomputeCardStats` の中で本人のカードの合計に乗るだけなので、本人の攻撃・移動・EXP/MILE の計算はマルチでもそのまま動く見込み。
- マルチで要る追加の同期(第1段階の項目に加えて):
  - 敵側に効くカード(MORE ENEMIES / TOUGH ENEMIES / HELL MODE / HORDE / PANDEMONIUM / BOSS CHALLENGE / BOSS RUSH / WANTED / ELITE ENEMIES 等の増幅): 敵の出現/HP/精鋭は HOST の EncounterDirector が決めるので、誰かの FE が ACTIVE の間の倍率を HOST へ送り、HOST が全員分をどう合わせるか(最大値/合計/本人の周りだけ)を決める必要がある(通常の Challenge カードのマルチでの扱いと合わせて確認する)。
  - 最大HPの増幅(HEART UP / FORTRESS / HEAVY ARMOR 等): HP は HOST 権威(NetMatch)なので、ACTIVE の開始/終了で `NetMatch.RequestSetMax` が飛ぶ(既存の経路)。終了時に最大HPが下がる時の現在HPの切り詰めを HOST 側でも同じにすること。
  - Shield / 被弾後の無敵 / のけぞり軽減の追加: 被弾の確定が HOST なので、HOST の被弾処理で本人の ACTIVE 状態を参照できるようにする(状態の表を NetMatch へ)。
  - 再使用: 状態(資格/READY/ACTIVE/残り/発動回数)は本人の端末で完結させ、HOST へは「ACTIVE の開始/終了」だけを知らせる(見た目のオーラと上の HOST 側の判定のため)。
  - 候補の公平さ(候補に出た回数)は本人の端末だけで足りる。

## COMBO 第1段階(2026-10-06): マルチでは効果なし
`ComboSystem.Enabled = !NetRunLauncher.IsMultiplayerRun`。マルチのランでは成立の計算も効果も出さない(HOST だけダメージが出る等の半端な状態にしない)。
対応に必要なこと:
- 成立は各プレイヤーの能力から本人の端末で計算できる(カードはプレイヤーごと)。HOST へ「成立中の COMBO の一覧」と FE による ENHANCED を送る(HUD/演出用)。
- 効果の判定は HOST 権威の敵へ当たるので、JOIN の命中から起きる COMBO(BLAZING EDGE/SHATTER/SONIC MOMENTUM/AIR ASSAULT/FINISHING BLOW/DEATH WISH/風)は、JOIN が「どの COMBO が誰に起きたか」を申告し HOST が DealQuiet する経路が要る(今の攻撃の申告と同じ形)。
- 属性の事件(凍結/落雷/炎上の撃破)は HOST だけで判定している(既存の課題と同じ)ので、その COMBO は HOST の側でプレイヤーごとの COMBO を見て出す必要がある。
- BLOOD AEGIS/AEGIS COUNTER は HP が HOST 権威なので、HOST の被弾確定の処理で本人の COMBO の数え(盾の数)を参照する。
- 同じ敵へのクールダウン/proc の予算はプレイヤーごとに持つか、HOST でまとめるかを決める。

## LAN の自動発見(2026-10-05)で既定の最大人数を 8 に
- `NetSession.DefaultMaxPlayers = PlannedMaxPlayers`(8)。部屋の知らせ/待機室は「n/8」。開発ビルドは `-netMaxPlayers N` で絞れる。
- 上の「2人の距離が数 km 離れると同期数が増える」課題は残っている(8 人の実機負荷は未確認)。詳細は Docs/LanDiscovery_2026-10-05.md。
