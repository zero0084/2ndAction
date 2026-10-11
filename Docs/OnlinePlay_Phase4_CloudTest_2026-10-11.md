# ONLINE PLAY Phase 4 — 本物の Unity Cloud での接続確認(2026-10-11)

マスターが Unity Cloud 側(Project Link、Relay・Lobby の production)を準備してくれたので、
2026-10-07 に作った ONLINE PLAY(`Docs/OnlinePlay_Phase4_2026-10-07.md`)を**本物の Cloud につないで**確認した。

- 確認した環境: Windows の開発版(同じ PC で 2〜4 プロセス、それぞれ別の匿名プレイヤー)
- **Android 実機での ONLINE は未確認**(APK は作成済み。実機で見てほしい点は 9 章)
- ゲームの中身(開始同期・敵/ボス共有・被弾・LastHit・CO-OP/VERSUS)は LOCAL と同じコードのまま。ONLINE 専用の判定は追加していない

---

## 1. 接続の基本

| 項目 | 結果 |
|---|---|
| Cloud Project | One More Mile(cloudProjectId 8568059a-…、組織 0084kazuma-gmail-com)。ProjectSettings にリンク情報を記録した |
| 環境 | `production` |
| UGS の初期化 | OK。ONLINE PLAY を押した時に初めて初期化(タイトル起動時・1人プレイでは初期化しない) |
| 認証 | 匿名サインイン OK。プレイヤーは別々(ログには ID の先頭6文字だけ出す。Token・秘密情報はログに出していない) |
| Session(部屋)作成 | OK。SessionId・人数(1/8 → 2/8)・ProtocolVersion=2・GameMode(coop / versus)を確認 |
| Relay | 接続方式 DTLS(暗号化)。リージョン asia-northeast1(QoS で測定。測れなかった時は SDK の自動選択) |
| 通信の種類 | Online(Relay 経由)。LOCAL(LAN の IP 直結)ではないことをログで確認 |
| FIND GAME | 作った部屋が一覧に出る(例: `QA-… coop 1/8 OPEN protocol=2`) |
| 1タップ JOIN | OK。IP・Port・コードの入力なし。JOIN を押してから接続まで 1.3〜10 秒(最終版: 5.4〜6.1 秒、一覧の検索込みで 8.9〜10.0 秒) |

### 同じ PC で複数プロセスを試す方法(開発版のみ)
`-ugsProfile <名前>` で匿名プレイヤーを分ける(指定しないと同じ PC のプロセスが同じプレイヤーになり、同じ部屋に2人として入れない)。
本番版ではこの引数は無視される。

---

## 2. 2人での確認(production、Windows)

| 項目 | 結果 |
|---|---|
| 待機室 | 1/8 → 2/8、Ready、カウントダウン、同時に GO |
| 開始のずれ | 走り出し(RunningDiff)0〜4ms、距離の数え始め(DistStartDiff)0〜18ms(最終版で 5 回: 0 / +4 / -7 / -18 / -9ms) |
| Ping(ゲーム自身の測定) | 平均 72〜153ms(最終版: 88〜117ms)、最大 約1,147ms(PC の負荷が高い時の一瞬)。Relay リージョン asia-northeast1 |
| プレイヤー同期・ジャンプ・攻撃 | OK |
| 敵/ボスの共有 | 両方で同じ敵の撃破(KILL)が記録される |
| 被弾 | HOST が HP を決める方式のまま、両方で一致 |
| LastHit とカード報酬 | 報酬を受け取る人が両方で一致 |
| CO-OP | DOWN → HP を分けて復活(2回)→ 全員 DOWN → RUN OVER。結果は両方で一致 |
| VERSUS | 脱落(ELIMINATED)→ 最後の1人 → 最終距離 → RUN OVER。結果は両方で一致 |

## 3. 4人での確認
4プロセス(HOST 1 + JOIN 3)で全員接続、開始のずれ 18ms 以内、例外 0 件。
**8人は未確認**(同じ PC で 8 プロセスは重すぎるため)。

## 4. 部屋の状態

| 項目 | 結果 |
|---|---|
| FULL | `-netMaxPlayers 2` の部屋で 2/2 → 一覧で FULL、JOIN 不可 |
| IN PROGRESS | 出発後は一覧で IN PROGRESS、JOIN 不可 |
| VERSION MISMATCH | 版を変えた(protocol 99)プロセスから見ると VERSION MISMATCH、JOIN 不可 |
| HOST が閉じる | 部屋を閉じてから約 3〜6 秒で FIND から消える(アプリを閉じる時も後始末を待ってから閉じる、最大 2.5 秒) |
| HOST が落ちる | プロセスを強制終了 → 約 26 秒で FIND から消える(Lobby の生存確認の期限切れ) |
| JOIN が抜ける | 2/8 → 1/8 に戻る。HOST 側のゲームは続く |

## 5. 見つけた問題と対策

### Relay への接続が時々失敗する(原因は Cloud 側の応答、対策済み)
- 症状: `Failed to establish connection with the Relay server (server didn't answer any BIND message)`。HOST でも JOIN でも起きる。
- 頻度: DTLS で約 11 回に 2 回。さらに 09:58〜10:16 の約 18 分間は毎回失敗した(Relay 側の一時的な不調と思われる)。UDP では 5/5 成功、その後 DTLS 5 回で 1 回失敗。
- 端末側のポートの問題ではない(Relay は毎回空いているポートを使う)。
- **暗号化(DTLS)はやめずに**、次の自動回復を入れた:
  - 待機中の HOST: Relay が切れたら部屋を自動で作り直す(最大 2 回)。
  - JOIN: 部屋に入った後、通信が実際につながるまで最大 30 秒待つ。Relay の接続失敗(満員・版違い・断られた、ではない)なら自動でもう 1 回だけ試す。
  - 最後の 5 回の確認では、失敗 1 回がこの自動回復で成功した。

### 同じ PC の別プロセスが同じプレイヤーになる → `-ugsProfile`(開発版のみ)で解決

### QoS(リージョン測定)が空で返ることがある → もう 1 回測る。それでも空なら SDK の自動選択

## 6. 回帰確認

| 項目 | 結果 |
|---|---|
| LOCAL の 2人(同期/戦闘/被弾/CO-OP/VERSUS) | 5 本すべて例外 0。CO-OP の全員 DOWN → RUN OVER、VERSUS の脱落 → RUN OVER、ボス報酬の受け取り手が一致 |
| LAN 自動発見 | 部屋が 2.9〜3.1 秒で見つかる。満員(FULL)は一覧に出て無理に JOIN すると ROOM FULL で断られる。出発後は IN PROGRESS で断られる |
| LAN 版違い | 一覧で VERSION MISMATCH、無理に JOIN しても断られる(テストの設定が古く一度は版違いになっていなかったので、版 99 で取り直した) |
| 手動 IP(127.0.0.1) | 接続して走れる |
| つながらないポート | CONNECTION FAILED、例外・固まりなし |
| UGS が使えない時 | 存在しない環境名で試験 → `ONLINE SERVICE UNAVAILABLE`(RETRY / BACK)、その後 LOCAL PLAY で部屋を作れる |
| 1人プレイ | 起動時に UGS を初期化しない(ONLINE PLAY を押すまで初期化しない)。`-qaOnline` 全項目合格 |
| セーブ | `-qaSave` 全項目合格、レジストリの内容は前後で同一 |

## 7. 人工遅延
本物の Cloud で確認した上で、補助として開発版の Network Simulator も使える(前回の文書 参照)。完了の判断には使っていない。

## 8. ダッシュボードの使用量
**Lobby: 使用を確認済み**(マスターがダッシュボードで確認、2026-10-11)。Lobby → 概要 → Requests by Type に
11:20〜11:25(UTC)の Create / Create Token / Heartbeat / Get / Update / Update Player の要求が出ている。
これは最終回帰の ONLINE 4 本(11:21〜11:25 UTC)の時刻と一致する。合計 45 KiB。
Relay の Usage はまだ見ていない(以下は確認方法の控え)。
マスターに見てほしい所: Unity Dashboard → Multiplayer → **Relay の Usage**(接続数/通信量)と **Lobby の Requests**。
2026-10-11 の検証で、2人 × 数十回、4人 × 数回の部屋を作った分の使用量が出ているはず。

## 9. 実機で確認してほしいこと(Android)
1. ONLINE PLAY → CREATE GAME(待機室に `ONLINE` と Relay リージョンが出るか)
2. もう1台(または Windows 版)から FIND GAME → 一覧に出るか → 1タップで JOIN
3. 2人で出発 → 敵を倒す・被弾・カード選択・CO-OP の復活・VERSUS の脱落
4. モバイル回線(Wi-Fi を切った状態)でも同じことができるか
5. 途中でアプリを裏へ回す/画面を消す → 戻した時の状態(切断扱いになるか、HOST 側に残骸が残らないか)
6. HOST の端末でアプリを閉じる → 相手の画面と、FIND から部屋が消えるまでの時間
7. 待機室の Ping の値(画面の ONLINE 表示)

## 10. ダッシュボードでマスターにお願いしたいこと
- 使用量の確認(8 章)だけ。追加の設定は不要(Relay・Lobby は今の production 設定のまま動いた)。
- 料金プランの変更・本番の公開設定は行っていない。

## 11. 分かっている問題・未確認
- Relay の BIND 失敗が時々起きる(自動回復で吸収。回復できなかった時は CONNECTION FAILED を出して一覧へ戻る)
- PC の負荷が高いと Ping が一瞬 1 秒を超える
- 8人・Android 実機・モバイル回線は未確認
