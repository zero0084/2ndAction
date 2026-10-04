# LAN マルチの自動発見(2026-10-05)

## 流れ(通常の画面に IP 入力はない)
MULTIPLAYER → LOCAL PLAY → CO-OP / VERSUS → CREATE GAME / FIND GAME

| 画面 | 内容 |
|---|---|
| CREATE GAME | この端末で部屋を作る(`NetSession.StartHost`)→ 部屋の知らせを開始。待機室「WAITING FOR PLAYERS n/8」(参加すると増える)。「部屋を閉じる」で終了・知らせを止める。「閉じる(部屋はそのまま)」→ 扉 → Stage Select で出発 |
| FIND GAME | 近くの部屋の一覧(名前 / モード / 人数 / OPEN・FULL・IN PROGRESS・VERSION MISMATCH)。選んだモードの部屋を上に。1 回のタップで JOIN(見つかった部屋の Address/Port を既存の `NetSession.StartClient` へ渡すだけ)。空の時は「ローカルゲームを探しています…」→ 4 秒で「ゲームが見つかりません」、「再検索」ボタン |
| ADVANCED | 従来の IP 直接入力(開発用)。HOST はここからでも部屋を知らせる |

- GameMode は HOST が決める(参加者は HOST のモードで遊ぶ。一覧に部屋のモードを表示)。
- FULL / IN PROGRESS / VERSION MISMATCH の部屋は JOIN ボタンが押せない。無理に接続しても HOST が断る(`ApproveConnection`: ROOM FULL / RUN IN PROGRESS / VERSION MISMATCH)。
- 接続できない時は一覧の画面に「CONNECTION FAILED」(例外/固まりなし、全画面の切断表示は出さない)。

## 仕組み(`Net/LanDiscovery.cs`、ゲームの同期とは別)
- UDP のブロードキャスト(ポート 47777)。HOST は 1 秒ごとに部屋の情報を送る。探す側は 1.5 秒ごとに「誰かいますか」を送り、HOST はすぐ直接答える(見つかるまで通常 1 秒以内)。3.5 秒聞こえなければ一覧から消す。毎フレームではなく 0.1 秒ごとに受信を読む(メインスレッド、非ブロッキング)。
- 部屋の情報: RoomId / RoomName(既定「端末名's Room」、保存キー `net.roomName` で後から変えられる)/ GameMode / CurrentPlayers / MaxPlayers(8)/ GameVersion(Application.version)/ ProtocolVersion / Port / 状態。接続先の IP は知らせの差出人の IP を使う。
- `LanDiscovery.MultiplayerProtocolVersion = 1`。接続時の版(`NetSession.ProtocolVersion = "OMM-NET-1"`)も同じ値から作る。違えば一覧で VERSION MISMATCH、接続も断る。
- 送り先: 255.255.255.255 + 各ネットワークの「その網の全員」宛て(テザリングの親機の網にも届くように)+ 同じ PC の別プロセス用に 127.0.0.1。
- `ConnectionType { Local, Online }` を拡張の入口として用意(今は Local だけ)。
- 部屋の知らせを止める: 部屋を閉じる / HOST の終了(`OnServerStopped`)/ アプリの終了 / バックグラウンドへ回った時(戻ったら再開)。探すのを止める: 戻る / 閉じる / JOIN 成功。
- 最大人数: `NetSession.DefaultMaxPlayers` を 8 にした(開発ビルドは `-netMaxPlayers N` で絞れる)。

## Android
- 方式: Unity Android(IL2CPP)でそのまま動く `System.Net.Sockets` の UDP(NSD / mDNS の Java プラグインは使わない)。
- 権限(最小限、`Assets/Editor/AndroidLanPermissions.cs` が生成したマニフェストへ足す): `ACCESS_WIFI_STATE`、`CHANGE_WIFI_MULTICAST_STATE`(どちらも通常の権限 = 許可ダイアログなし)。INTERNET / ACCESS_NETWORK_STATE は従来どおり。位置情報/近くのデバイスの権限は使わない。
- `LanMulticastLock`: 探している間/知らせている間だけ WifiManager.MulticastLock を取り、止めたら必ず放す(参照カウントなし)。

## 家庭の Wi-Fi / テザリング
- 家庭の Wi-Fi: 同じルーターの同じ網なら見つかる。ルーターの「AP アイソレーション(端末どうしの通信を禁止)」やゲスト用 Wi-Fi では見つからず、接続もできない(IP 直接入力でも同じ)。
- テザリング: 親機(テザリングを出す端末)が HOST でも子機が HOST でもよい。親機は 255.255.255.255 を自分の網へ出さない端末があるため、網宛てのブロードキャストも送る。一部の機種/キャリアはテザリングの端末どうしの通信を止めているので、その場合は見つからない(接続もできない)。
- 5GHz / 2.4GHz の別々の SSID、メッシュ Wi-Fi の中継器をまたぐ場合、ブロードキャストが届かない機器がある。

## 開発用 / 確認
- `NetAutoTest -netAutoJoin lan`(IP を使わず一覧から JOIN)、`-lanWatch <秒>`(見張り: FOUND / UPDATED / LOST を記録)、`-lanForceJoin <秒> [-lanForceJoinPort N]`(一覧の可否を無視して JOIN → 断られる/接続できないの確認)、`-lanProtocol N`(版違いの部屋を作る)。
- ログ: `[LAN] Advertising room` / `Discovery started` / `Room found` / `Room updated` / `Room lost` / `Join requested` / `Join succeeded` / `Join failed`。
