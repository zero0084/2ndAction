# ONLINE PLAY(マルチプレイ Phase 4)2026-10-07

LOCAL PLAY(同じ Wi-Fi)に加えて、インターネット越しに遊ぶ ONLINE PLAY を追加した。
ゲーム本体(開始同期・敵/ボス共有・被弾・LastHit・CO-OP/VERSUS)は LOCAL と同じコードを使う。差し替えたのは「つなぎ方」だけ。

## 1. 構成

| 項目 | 内容 |
|---|---|
| 追加パッケージ | `com.unity.services.multiplayer` 2.2.4(Sessions + Relay + Lobby + QoS。Authentication / Core も依存で入る)、`com.unity.multiplayer.tools` 2.2.9(開発版の人工遅延 Network Simulator) |
| 据え置き | Netcode for GameObjects / Unity Transport(LOCAL と同じ NetworkManager を使う) |
| 認証 | Unity Authentication の匿名サインイン(Anonymous)。ONLINE PLAY を押した時に初めて UGS を初期化する |
| 部屋 | MPS の Session(公開、最大8人)。中身は Lobby(一覧/公開情報/HOST の生存確認)+ Relay(通信の中継) |
| Relay | DTLS(暗号化)。リージョンは HOST 側で QoS を測って一番近い所を指定(測れなければ SDK の自動選択) |
| HOST/Client の開始 | 自前の `OnlineNetworkHandler`(INetworkHandler)が SDK から Relay の接続先を受け取り、`NetSession.StartRelayHost/StartRelayClient` へ渡す。版の照合・満員・途中参加の拒否・切断の処理は LOCAL と共通 |
| 権威 | HOST 権威のまま(変更なし) |
| Matchmaker | 使っていない(公開 Session の一覧 → 選んで JOIN) |

### 画面の流れ
MULTIPLAYER → **LOCAL PLAY** / **ONLINE PLAY** → CO-OP / VERSUS → CREATE GAME / FIND GAME → 待機室(WAITING FOR PLAYERS n/8)。
- FIND GAME(ONLINE): 部屋の名前 / モード / 人数 / 状態(OPEN・FULL・IN PROGRESS・VERSION MISMATCH)。参加できる部屋だけ JOIN が押せる。IP やコードの入力・表示は無し(1タップ)。
- 一覧の更新: 画面を開いた時 + 10秒ごと + 手動の再検索(2秒あける)。Lobby の検索は1回/秒が上限の目安なので、それより十分遅くしてある。
- 失敗: サービスが使えない → `ONLINE SERVICE UNAVAILABLE` / 通信を確認してください / RETRY / BACK。部屋を作れない・入れない → `CONNECTION FAILED`(一覧の画面に戻り、一覧を取り直す)。
- 新しい画面の文は 30 言語に訳した(`Tools/loc/keys_extra4.tsv` → `out/*.w.tsv`)。既存の LAN 画面の文も今回から訳が付く(GUILayout の文を `Loc.Auto` に通した)。

### Session の公開情報(検索/表示用、秘密は入れない)
| キー | 索引 | 値 |
|---|---|---|
| (Session 名) | — | 部屋の名前(LAN と同じ RoomName) |
| GameMode | String1 | `coop` / `versus`(検索の絞り込みに使う) |
| ProtocolVersion | String2 | LAN と同じ版(`LanDiscovery.AdvertisedProtocol`) |
| GameVersion | String3 | `Application.version` |
| RunState | String4 | `open` / `inprogress`(出発したら HOST が書き換え、同時に Session を締め切る IsLocked) |
| ConnectionType | — | `online` |
| StageId | — | 作った時の選択ステージ |
| RelayRegion | — | HOST が選んだ Relay リージョン(表示用) |

最大人数は `NetSession.MaxPlayers`(8)。人数は `MaxPlayers - AvailableSlots`。版違いは一覧で VERSION MISMATCH(JOIN 不可)、それでも接続してきた場合は HOST の承認で断る(LOCAL と同じ)。

### 秘密の扱い
- Relay の参加コードは SDK が「メンバーだけ」の項目に入れる(公開の項目に出ない)。こちらでは扱わない・表示しない・ログに出さない。
- トークンをこちらで PlayerPrefs / ファイル / 公開の項目に保存することはしていない。ただし **Unity Authentication SDK 自身が、同じ匿名プレイヤーで再サインインするための session token を PlayerPrefs に保存する**(SDK の正式な仕組み。Windows ではレジストリ)。アクセストークンはメモリだけ。
- 接続データ(NGO の ConnectionData)には「版 + UGS の PlayerId」を載せる。PlayerId は公開の識別子で秘密ではない(HOST が、通信の切れた JOIN を Session から外すのに使う)。
- 自動テストで [ONLINE]/[NET] のログに token / join code などが出ていないことを確認。

### HOST 終了 / JOIN 切断
- HOST が「部屋を閉じる」/ 通信の故障 / アプリ終了 → Session を削除(アプリ終了は間に合わない場合がある)。
- 異常終了で削除できなかった場合: HOST の生存確認(heartbeat、SDK が自動で送る)が止まり、**約30秒で検索結果から消える**(Lobby の既定の有効時間)。1時間で期限切れになり、その後削除される(Lobby の仕様)。
- JOIN が退出 → Session から抜ける(人数が減る)。JOIN の通信が切れた → JOIN 側は抜け、HOST 側も NGO の切断を受けて Session からその人を外す(アプリが落ちた場合も人数が戻る)。待機室とゲーム内の一覧は NGO の接続数なので、従来どおり更新される。
- 本格的な再接続は未実装(Session の再参加を後から足せるよう、作成/参加/退出は OnlineServices にまとめてある)。

### Environment
既定は `production`(UGS に最初からある)。開発版は起動引数 `-ugsEnv development` で切り替えられる(Dashboard に development を作っておくこと)。製品版は常に production。

### 開発用の表示 / 計測
- 開発版の NET COMBAT パネルと待機室に `ONLINE relay <リージョン> | ping 今/平均/最大 | out/in KB/s`。
- Ping は自前の往復計測(`NetPing`、1秒ごとの小さなメッセージ)。Unity Transport の RTT は確実な通信の確認応答から出すため、通信が少ないと遅延0でも約200ms と出てしまい、使えなかった。
- 人工遅延: 起動引数 `-netLatency <ms>`(+ `-netJitter`、`-netLoss`)。Network Simulator は開発版と Editor でのみ働く。片側の値の約4倍が往復に足される(送信と受信の両方で遅れるため)。
- ONLINE の時だけ、他プレイヤーの表示の補間の待ちを 0.10 → 0.15 秒(見た目だけ。判定は変わらない)。
- QUICK PLAY 用の `OnlineServices.QuickJoinAsync(mode)`(画面はまだ無い)。

## 2. テスト結果(Windows 開発版)

この PC のプロジェクトは Unity Cloud に未連携のため、**本物の Relay 越しの2人接続はまだ試せていない**(下の Dashboard 作業の後に行う)。代わりに次を確認した。

| テスト | 結果 |
|---|---|
| `-qaOnline` | 起動時に UGS を初期化しない / ONLINE PLAY → ONLINE SERVICE UNAVAILABLE(原因: 未連携)/ RETRY で再試行して同じ画面 / BACK / その後 LOCAL の部屋が作れる / 部屋の判定(OPEN・VERSION MISMATCH・FULL・IN PROGRESS)/ 一覧画面 / シングルのラン / ログに秘密なし — 全て PASS、例外0 |
| 人工遅延の2人接続(LAN の同じ経路、戦闘混在+ボス、40秒) | 実測 Ping 平均 94 / 165 / 232 / 240 / 362 / 433 ms で全て例外0。出発の同期差 0〜19ms、距離の加算開始の差 ≤19ms、ボス・雑魚の LastHit と生存中の敵の一覧(ID と HP)が両端末で一致、表示の段差 maxStepErr ≤0.41 |
| LOCAL の回帰(同期 / 敵・ボス共有+LastHit / 被弾 / CO-OP / VERSUS) | 例外0。CO-OP は DOWN → 復活2回 → 渡せない時は渡さない → 全員 DOWN で終了。VERSUS は脱落 → FinalDistance → 順位 |
| LAN の自動発見(3プロセス) | 発見 / JOIN / 人数更新 / IN PROGRESS / FULL / VERSION MISMATCH の拒否 / IP 直接接続 — 従来どおり |
| シングルの回帰 `-qaSave` | PASS |

注: 自動テストの CO-OP は開始 HP 5 のままだと(戦闘の数値10倍化の後)すぐ倒れて復活まで届かない。HP 50 で確認した。

## 3. マスターにお願いする Dashboard / Editor の作業

1. **プロジェクトを Unity Cloud に連携**(Editor): `Edit > Project Settings > Services` → 組織を選ぶ → 既存の Unity Cloud プロジェクトを選ぶ(または新しく作る)→ Link。`ProjectSettings/ProjectSettings.asset` の `cloudProjectId` が入るので、それをコミット。
2. **サービスを有効化**(Unity Cloud Dashboard https://cloud.unity.com → そのプロジェクト): `Products` から **Relay** と **Lobby** を有効にする(Sessions はこの2つで動く)。Authentication の匿名サインインは既定で使える。
3. **Environment**: `production` は最初からある。開発用を分けるなら `Project settings > Environments` で `development` を追加(開発版を `-ugsEnv development` で起動)。
4. 終わったら知らせてください。Windows 2台(2プロセス)と Android 実機で本物の ONLINE 2人接続 → 4人の順で確認します。

### 使用している UGS サービスと利用量の確認場所
- 使用: **Authentication**(匿名)、**Lobby**(Session の一覧/公開情報/生存確認)、**Relay**(通信の中継)、QoS(リージョンの測定)。Matchmaker / Multiplay Hosting / Distributed Authority / Cloud Save は使っていない。
- 確認場所: Unity Cloud Dashboard の各サービスの画面(`Relay` / `Lobby` の Overview・Usage)と、組織の `Billing`(使用量と請求)。Relay は通信量と同時接続、Lobby は呼び出し数などで無料枠を超えると料金がかかる可能性がある。

## 4. 既知の問題 / Android 実機で確認すること
- 本物の Relay 越しの接続(開始同期の時計合わせ、ボス共有、CO-OP/VERSUS)は未確認。上の作業の後に行う。
- アプリ終了時の Session の削除は間に合わないことがある(その場合は約30秒で一覧から消える)。
- Android: モバイル回線(4G/5G)と Wi-Fi の組み合わせ、アプリを裏に回した時(HOST の生存確認が止まると約30秒で一覧から消える)、端末のスリープ。
- 人工遅延は開発版だけ(製品版では働かない)。
- 4人・8人の ONLINE は未確認(2人の確認の後)。

## 5. ファイル
- 追加: `Net/OnlineServices.cs`(初期化・認証・作成/検索/参加/退出・公開情報・RunState・切断した人の除外・QuickJoin・OnlineNetworkHandler)、`Net/NetPing.cs`、`Net/NetLatencySim.cs`、`QaSweep.Online.cs`、`Tools/loc/keys_extra4.tsv` + `out/*.w.tsv`
- 変更: `Net/NetSession.cs`(Relay での開始、接続の種類、PlayerId の対応、ONLINE の退出)、`Net/NetDebugUI.cs`(ONLINE の画面、文の翻訳、画面の切り替えを Layout の時だけに)、`Net/NetStats.cs`(Ping 今/平均/最大、PingLine)、`Net/NetCombat.cs`(パネルに PingLine)、`Net/NetPlayerSnapshot.cs`(ONLINE の補間)、`Net/NetAutoTest.cs`(ログに Ping)、`QaSweep.cs`、`Packages/manifest.json`、`Tools/loc/merge.py`
