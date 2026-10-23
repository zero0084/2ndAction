# 製品版から試験/開発の動きに入れる経路の点検(2026-10-23)

対象: `Unity/2ndAction/Assets/Scripts` 全体(読むだけ。ファイルは変えていない)
前提: 製品版 = Development Build なし・追加の定義なし(Android はさらに `OMM_ADMOB;OMM_UNITY_IAP`)。
`Debug.isDebugBuild` は false、`#if UNITY_EDITOR || DEVELOPMENT_BUILD` の中はコンパイルされない。
撮影用ビルド(`DEVELOPMENT_BUILD` + `OMM_CAPTURE`)は対象外(影響だけ最後に書いた)。

Steam 版は、利用者がライブラリの「起動オプション」で好きな引数を付けられる。なので「起動引数で入れる」= だれでも入れる、として扱った。

## 対応(2026-10-23、同じ日に修正済み)
HIGH の 5 つは、開発版(Editor / Development Build)だけにコンパイルするようにした(`#if UNITY_EDITOR || DEVELOPMENT_BUILD`)。
- `Net/NetSession.cs` の `NetAutoTest` の起動と、`Net/NetAutoTest*.cs` の 5 ファイル全体
- `Encounters/EncounterDirector.cs` の `-encounterForce` / `-encounterSeed`
- `BonusZoneShots.cs` / `Audio/AudioAutoTest.cs` / `RunBuildHudAutoTest.cs` のファイル全体
- 同じ点検で見つかった、開発版の印の無かった `QaSweep.AirHit/HomeLayout/Result/Tap.cs`(製品版のビルドがコンパイルできなかった)

確認:
- 製品版の Windows ビルド(`Tools/2ndAction/Build Windows Product`)は成功。中の `Assembly-CSharp.dll` に `NetAutoTest` / `BonusZoneShots` / `AudioAutoTest` / `RunBuildHudAutoTest` / `encounterForce` / `encounterSeed` / `netAuto` / `QaSweep` / `storeCapture` / `DEV BUILD` / `Development Build` の文字が 0
- 製品版を `-storeCapture -qaTap -fileSave -netAuto host -netAutoOnline -audioTest -bonusShots -runBuildHudTest -encounterForce x -encounterSeed 5` 付きで起動 → 普通のホームのまま。試験のファイルは作られず、ログに試験の動きもオンラインのサインインも無い(保存のレジストリは起動前の控えへ戻して完全一致を確認)
- 開発版(Development Build)の Windows ビルドも成功(QA はそのまま使える)

まだ対応していない提案(ゲームの決まりを変えるので、マスターの判断):
- 開発用の操作をしたラン(`debugUsed`)は、製品版では MILE を加えない(入口の見落としがあっても経済に影響しないための保険)
- マルチの HOST がクライアントのダメージ/回復/最大 HP の申告をそのまま信じる点(ONLINE の公開の前に)

## 結論(先に)

製品版でも動いてしまう試験の入口が **5つ(HIGH)** ある。どれも起動引数だけで入れる。

| # | 重さ | 場所 | 入口 | 製品版で起きること | 今止まっているか |
|---|---|---|---|---|---|
| 1 | HIGH | `Net/NetSession.cs:85` → `Net/NetAutoTest*.cs`(5ファイル) | `-netAuto` で始まる引数どれでも(`NetAutoTest.cs:338`) | マルチの試験ボットが自動で HOST/JOIN。**本番の UGS(production)の ONLINE の部屋**を作る/入る。自分を無敵にする、走る速さを倍にする、Lv9 のカードを付ける、前の敵/ボスを 9,999,999 ダメージで倒す(JOIN からでも HOST が受け付ける)、コードネームを書き換えて本番ランキングの名前も変える、指定したパスへファイルを書く | 止まっていない(一部の機能だけ `#if` の中) |
| 2 | HIGH | `Encounters/EncounterDirector.cs:135-140` | `-encounterForce <隊形ID>` / `-encounterSeed <数>` | 敵の隊形をラン中ずっと1種類に固定する / 敵の並びを毎回同じにする。**ランに印が付かないので BEST・ランキングの対象のまま** | 止まっていない |
| 3 | HIGH | `BonusZoneShots.cs:11-19` | `-bonusShots`(`-bonusSky` で天空) | 自動でランを始め、無敵+残り999にして、BONUS ZONE(MILE RUSH / JACKPOT / MIMIC / CARD HUNT)を強制で起こす。仮の MILE が中断データに残る可能性(下に詳細)。インストール先の `BonusZoneShots/*.png` を消してから撮影し、終了する | 止まっていない(ボットの入力だけ `#if` の中、距離ワープは DevGate で止まる) |
| 4 | HIGH | `Audio/AudioAutoTest.cs:17-29` | `-audioTest` | 自動でランを3本。無敵、ボスを 99,999 ダメージで倒す、天空で `Win()` → **MILE が財布に入る**。音量を変えて最後に戻す。インストール先に `AudioAutoTest.txt` を書いて終了 | 止まっていない(距離ワープだけ DevGate で止まる) |
| 5 | HIGH | `RunBuildHudAutoTest.cs:17-30` | `-runBuildHudTest` | 自動でランを3本。**所持カードに1枚足す**(`:91` `CardInventory.AddCard`、キャラカード未装備の時)、無敵、速さ6倍(`:212`)、天空で `Win()` → **MILE が入る**。`RunBuildHudShots/*.png` を消して撮影、`RunBuildHudAutoTest.txt` を書いて終了 | 止まっていない |
| 6 | LOW | `Net/NetCombat.cs:152-154` | `-netEnemyDelay <秒>` | JOIN 側の敵の表示の遅れ(補間の幅)を変える。見た目のなめらかさだけ。記録/報酬には関係しない | 止まっていない |
| 7 | LOW | `Net/NetMatch.cs:120-121` | `-netChoiceInvincible` | フラグが立つだけ。今はどこからも使われていない(コメントどおり判定に使っていない) | 実害なし |
| 8 | LOW | `Online/Leaderboard.cs:283-288` | (`Arg()` の補助だけ) | 引数を読む関数は製品版にも残るが、呼ぶのは `#if` の中の `-lbMock` だけ | 止まっている |
| 9 | LOW | `FreezeDiagnostics.cs` / `BossDiagnostics.cs` | 入口なし(常時) | 異常を検知すると `persistentDataPath` に `hitch_log.txt` / `boss_freeze_log.txt` を追記する。画面の表示(診断ボタン/トースト)は DebugMode の時だけ = 製品版では出ない | 表示は止まっている。ログの書き出しは仕様(端末内だけ) |
| 10 | LOW(参考) | `Monetization/MonetizationServices.cs:130-133` | 入口なし | 本番の広告ユニット ID が空なので、Android 製品版でも Google のテスト広告のまま(コメントどおり「未設定」)。Steam は Mode=None で関係なし | 公開前に入れる予定の値 |

MEDIUM(記録/経済に触れない、試験の自動操作や画面だけ)に当たる物は、製品版にコンパイルされる所には見つからなかった。

## 確かめて「問題なし」だった所(OK)

依頼の一覧のファイルは全部見た。引数を読む所はすべて `#if UNITY_EDITOR || DEVELOPMENT_BUILD` の中、またはファイル全体が `#if UNITY_EDITOR`(Editor だけ)だった。

- ファイル全体が `#if UNITY_EDITOR`(どの実行ファイルにも入らない): `HighSpeedAssistAutoTest.cs` `LancerStressAutoTest.cs` `ObstacleBreakAutoTest.cs` `SkyEncounterAutoTest.cs` `WastelandEncounterAutoTest.cs` `Reapers/ReaperChaseAutoTest.cs` ほか *AutoTest / *VisualTour の多く。`#endif` の後ろに続くのも `#if UNITY_EDITOR` のメニューだけ(`HitReactionAutoTest.cs` `QaSweep*.cs` `DebugPanel.cs` も確認)。
- ファイル全体が `#if UNITY_EDITOR || DEVELOPMENT_BUILD`: `QaArgs.cs`(= Android の `qa_args.txt`)、`QaSweep*.cs`、`Dev/FileSaveStore.cs`(`-fileSave` / `qa_filesave.txt`)、`Dev/MemorySaveStore.cs`(`-memSave`)、`Dev/EndgameDebug.cs`、`Dev/HitboxOverlay.cs`、`Dev/SaveRecoverHooks.cs`、`Settings/DebugPanel*.cs`、`CardTest/CardBalanceTest.cs`、`Net/LanAutoTest.cs`、`LastDungeon/LastDungeonQa.cs`、`LastCorridor/LastCorridorTour.cs`、*DemoCapture、`DiagUiDemo.cs`、`AttackFeelTour.cs`。
- ファイルの中の `#if` で止まっている: `AudioManager.cs:301`(-auLog)、`Balance/BalanceTuning.cs:110-172`(-balVariant)、`BossManager.LastDungeon.cs:126-139`(さらに使う所も `Debug.isDebugBuild`)、`LastCorridor/LastCorridorDirector.cs:198-208`、`LastDungeon/LastDungeonFlow.cs:94-96`、`Monetization/MonetizationServices.cs:100-105`(-monetizationMock / Dev キー)、`Net/LanDiscovery.cs:41-44`、`Net/LanSecurity.cs:23-28`(-lanPlain。製品版は常に暗号化)/`:128-137`、`Net/NetPlayer.cs:94-98`、`Net/NetRunLauncher.cs:122-126`、`Net/NetSession.cs:35-38`(-netMaxPlayers)、`Net/OnlineServices.cs:113-116`(-ugsProfile)/`:447-450`(-relayProto)、`Net/UgsEnvironment.cs:17-46`(製品版は production 固定、引数で変えられない)、`Net/NetLatencySim.cs`、`Online/PrivacyLinks.cs:20-23`、`Party/CpuParty.cs:49-61`、`PlayerController.cs:985-988` ほか、`PortraitCameraRig.cs:85-95`、`Save/CommitJournal.cs:108-135`、`Save/SaveSystem.cs:687-741`(保存先の切り替え)、`Save/SaveProfile.cs`、`Save/SaveTransfer.cs`、`Sprint/GameManager.Sprint.cs:65-67`、`UI/FoldView.cs:44-51`、`UI/FoldViewOblique.cs:25-36`、`UI/TextFit.cs:24-27`(さらに `isDebugBuild`)、`UI/ZigRoad.cs:27-55`。
- `Debug.isDebugBuild` で止まっている: `PlayerController.ContactGrace.cs:70`(-qaNoLungeStop)、`StallProbe.cs`、`NetMatch.cs:379/385`(JOIN からの「HP を設定」「強制脱落」の要求を HOST が受け付けない)、`Net/NetDebugUI.cs:253`(IP 直接入力の画面)。
- 開発用の保存キー(SaveKeys の Dev 分類): `InvincibleMode` / `DebugMode`(`GameManager.cs:1304-1305`、`isDebugBuild`)、`Dev.AllCardsOpen`(`isDebugBuild`)、`Dev.ResumeEase`(`isDebugBuild`)、疾走の全解放(`isDebugBuild`)、`Dev.UnlockAll`(`DevGate.On`)、`Dev.BossHpPlanHits`(`#if`+`isDebugBuild`)、ラスダン常時(`#if`)、`Dev.MonetizationMock` / 模擬パス / 模擬ストア(`#if` の中でしか模擬にならない)。**PlayerPrefs(レジストリ)を手で書き換えても製品版では効かない**。
- 距離ワープ `GameManager.DebugWarpToDistance`(`:5774`)と `:5709` は `DevGate.On` で止まる(製品版では何もしない)。
- `[RuntimeInitializeOnLoadMethod]` の起動処理: 上の HIGH 4件(NetSession 経由の NetAutoTest、AudioAutoTest、RunBuildHudAutoTest、BonusZoneShots)以外は、どれも製品の機能(HighSpeedAssist、FloatingOrigin、EncounterDirector、NetCombat/NetMatch、SaveSystem、Settings、Tutorial など)で、試験の部品を勝手に作る物はなかった。`DiagnosticsOverlay` は常に作られるが、描くのは DebugMode の時だけ。
- デバッグ用のキー操作(F1〜F12 など)、環境変数の読み取りは、製品版にコンパイルされる所には無かった。

## 詳細

### 1. NetAutoTest(マルチの試験ボット)— HIGH

- 入口: `Net/NetSession.cs:85` `if (NetAutoTest.ShouldRun) go.AddComponent<NetAutoTest>();`
  `Net/NetAutoTest.cs:338` `ShouldRun => Array.Exists(Environment.GetCommandLineArgs(), a => a.StartsWith("-netAuto"));`
  NetSession は起動時に必ず作られる(`[RuntimeInitializeOnLoadMethod]`)ので、引数があれば製品版でも動く。
- 製品版で起きること(主な物):
  - `-netAutoHost` / `-netAutoJoin <IP|lan|online>`: 2秒後に自動で部屋を作る/入る。`-netAutoOnline` / `online` なら **本番の UGS(Sessions + Relay)で部屋を作る/一覧から探して入る**(`:128-191`)。`-netAutoQuick` は最初に見つかった他人の部屋へ入る。`-netAutoObserve` は本番の部屋の一覧を見続ける。
  - 無敵: `KeepAlive` が毎フレーム `InvincibleMode=true`、残り990(`:1029-1030`)。HOST は被弾しない、JOIN は被弾の申告を送らない → **VERSUS で本物の相手に対して無敵**。
  - `-netAutoSpeed N`: `pc.runSpeed *= N`(`:1040-1045`)。走る速さが N 倍(VERSUS の距離勝負で有利)。
  - `-netAutoCards id:lv,...`: ラン開始時にカード効果を Lv9 まで付ける(`:902-911`)。
  - `-netAutoRwKills t:killall` / `t:boss`(`NetAutoTest.Rewards.cs:96-120`): 前45m の敵、ボスを 9,999,999 ダメージで倒す。JOIN は `NetCombat.RequestHit` で HOST へ送り、HOST 側は 999,999 に丸めるだけで受け付ける(`NetCombat.cs:860`)→ **他人の部屋でも敵/ボスを一撃で倒せる**。協力の MILE の分け前にも乗る。
  - `-netAutoEliteAt`(HOST): 敵をエリートに変える。`-netAutoEncDist`: 敵の距離の帯を先へずらす(`:464`)。`-netAutoLegacyAnchor` / `-netAutoNoLeash`: 出現の基準/ボスの置き去り防止を変える(`NetAutoTest.World.cs:24-25`)。`-netAutoLead` / `-netAutoSpread`: 走る速さの倍率(`DebugRunOnlyScale`)を 0.5〜1.7 で動かす。
  - `-netAutoCodename <名前>`(`:369`): 普段の保存のコードネームを書き換え、ランキングに参加済みなら **本番ランキングの名前も更新**(`Codename.Set` → `Leaderboard.OnCodenameChanged`)。`-netAutoName` も `-memSave` を一緒に付ければ同じ(`NetAutoTest.Aura.cs:25`。製品版では `-memSave` の保存は無いので普段の保存に書く)。
  - `-netAutoTrace <パス>` / `-netAutoEnemyTrace <パス>`: 指定したパスへ上書きで CSV を書き続ける(`:394-403`)。`-netAutoShotDir <フォルダ>`: そこへ画面を撮る。
  - 最後に `Application.Quit()`(`:1349`)。
- 記録: 無敵で `RunLedger.MarkDebug("invincible")` が付くので、BEST/ランキングには入らない(マルチはもともとランキング外)。ただし **MILE は止まらない**(`GameManager.cs:3822-3824` は `Win` なら `debugUsed` でも `AddMile(RunMile)` する)。
- 今の止まり方: ワープ(`-netAutoSceneryWarp`)は DevGate で無効、HP 設定/強制脱落の要求は HOST が `isDebugBuild` で断る、ボス出現/WANTED/全解放/入力の注入は `#if` の中。上に書いた物は止まっていない。
- 直し方(最小): `Net/NetSession.cs:85` を
  ```csharp
  #if UNITY_EDITOR || DEVELOPMENT_BUILD
          if (NetAutoTest.ShouldRun) go.AddComponent<NetAutoTest>();
  #endif
  ```
  にする。できれば `NetAutoTest.cs` / `.Aura.cs` / `.BreakCombo.cs` / `.Rewards.cs` / `.World.cs` の5ファイル全体も `#if UNITY_EDITOR || DEVELOPMENT_BUILD` で囲む(他から参照しているのは NetSession の1行だけなので、そこを囲めばコンパイルは通る)。1行で済ませるなら `ShouldRun => Debug.isDebugBuild && ...` でもよい。
- QA への影響: マルチの自動試験(mp8.sh、lantest.sh、ONLINE 試験)は Development Build で回しているので、どちらの直し方でも動く。撮影用ビルドは使っていない。

### 2. EncounterDirector の -encounterForce / -encounterSeed — HIGH

- 入口: `Encounters/EncounterDirector.cs:135-140`(`Awake`、毎回起動時に作られる)。
- 製品版で起きること:
  - `-encounterForce <隊形ID>`: `ForcedFormation` を設定、`ForcedRemaining = -1`(= ずっと)。全マップ・全距離で毎回その隊形だけが出る(`:645-716`)。簡単な隊形を選べば道中が楽になる。
  - `-encounterSeed <数>`: 出現の乱数を固定(`:166`)。毎回同じ並びになり、覚えて走れる。マルチの HOST でも効く。
  - どちらも RunLedger に印を付けないので、**BEST・解放・累計・ランキングに普通に入る**。
- 直し方(最小): `:135-140` の引数の読み取りを `#if UNITY_EDITOR || DEVELOPMENT_BUILD` で囲む。
- QA への影響: 使っているのは開発版の試験だけ。撮影用ビルドは使っていない(`QaSweep.StoreCapture.cs` に参照なし)。

### 3. BonusZoneShots(BONUS ZONE の撮影)— HIGH

- 入口: `BonusZoneShots.cs:11-19`、`-bonusShots`。
- 製品版で起きること: 剣士・荒野(`-bonusSky` で天空)のランを自動で開始(`:35`)、無敵+残り999(`:36-37`)、ボスを止める(`:38`)。BONUS ZONE を `Force("mile_rush")` → `jackpot` → `mimic_bash` → `card_hunt` と強制(`:43-63`)。最後に `Application.Quit(0)`。
  - 仮の MILE(`RunBonusMile`)はランの中だけで、`Win` しないので普通は消える。ただし `:65` は「始める前に中断データが無かった」時は何もしないので、ラン中に書かれた中断データ(`RunCheckpoint` は `runBonusMile` も持つ)が残ると、次の起動で CONTINUE → 脱出で MILE が財布に入る可能性がある(未検証)。
  - インストール先の `../BonusZoneShots/*.png` を消してから撮る(`:26-28`)。
  - 距離ワープ(`:39`)は DevGate で無効、ボットの入力(`:81-83`)は `#if` の中。
- 直し方(最小): ファイル全体を `#if UNITY_EDITOR || DEVELOPMENT_BUILD` で囲む。
- QA への影響: 撮影は開発版の実行ファイルで行う物。撮影用ビルド(ストア素材)は使っていない。

### 4. AudioAutoTest(音の自動確認)— HIGH

- 入口: `Audio/AudioAutoTest.cs:17-29`、`-audioTest`。
- 製品版で起きること: 3本のランを自動で走る。無敵+残り999+EXP 0倍(`:209-211`)、ボスを 99,999 ダメージで倒す(`:244-246`)、レベルアップの選択を自動で選ぶ(`:257-273`)、荒野は `FinishRun`(ゲームオーバー)、**天空は `gm.Win()`(`:169`)→ そのランの MILE が財布に入る**。音量を全部4にしてから戻す、PlayerPrefs の音量を書く。`Application.dataPath/../AudioAutoTest.txt` を書いて `Application.Quit`。
  - 距離ワープ(`WarpTo` → `DebugWarpToDistance`)は DevGate で無効なので、途中の確認は失敗するが、ランと Win は進む。
  - 無敵で `debugUsed` が付くので BEST/ランキングには入らない。MILE は入る(上と同じ理由)。
- 直し方(最小): ファイル全体を `#if UNITY_EDITOR || DEVELOPMENT_BUILD` で囲む(中の Editor メニューは今の `#if UNITY_EDITOR` のまま入れ子になる)。
- QA への影響: Editor のメニューと開発版の実行ファイルで使う物なので、どちらも動く。

### 5. RunBuildHudAutoTest(RUN BUILD HUD の自動確認)— HIGH

- 入口: `RunBuildHudAutoTest.cs:17-30`、`-runBuildHudTest`。
- 製品版で起きること: 3本のランを自動で走る。キャラカードが未装備なら **解放済みの最初のカードを所持へ1枚足して装備**(`:88-92`、保存に残る)。無敵+残り999+EXP 0倍(`:104-106`)、カードを正規の取得処理で付ける、走る速さ6倍(`:212` / `:319`)、**天空で `gm.Win()`(`:290`)→ MILE が入る**。`RunBuildHudShots/*.png` を消して撮影、`RunBuildHudAutoTest.txt` を書いて `Application.Quit`(`:341-345`)。
- 直し方(最小): ファイル全体を `#if UNITY_EDITOR || DEVELOPMENT_BUILD` で囲む。
- QA への影響: 同上(Editor と開発版で動く)。

### 6〜10(LOW)

- `-netEnemyDelay`(`Net/NetCombat.cs:152-154`): JOIN の敵の表示の遅れを変えるだけ。記録/報酬への影響なし。ついでに `#if UNITY_EDITOR || DEVELOPMENT_BUILD` で囲むと、製品版の引数の入口がゼロになってすっきりする。
- `-netChoiceInvincible`(`Net/NetMatch.cs:120-121`): フラグは今どこにも使われていない。消すか `#if` で囲む。
- `Leaderboard.Arg()`(`Online/Leaderboard.cs:283-288`): 製品版で呼ぶ所なし。
- 診断ログ(`FreezeDiagnostics.cs` / `BossDiagnostics.cs`): 製品版でも端末内(`persistentDataPath`)へ追記する。画面には出ない。仕様どおり。
- Unity 自体の起動引数(`-screen-width`、`-screen-fullscreen`、`-force-d3d11`、`-logFile` など)はゲームの記録に関係しないので対象外。

## ついでに気づいた点(入口ではないが、守りを足すなら)

- **デバッグの印が付いたランでも MILE は入る**: `RunLedger.MarkDebug` は BEST/解放/ランキングを止めるが、`GameManager.FinishRunCore`(`GameManager.cs:3822-3824`)の `AddMile(RunMile)` は `debugUsed` を見ない。開発版で無敵を使った時の動きとしては今のままでよいが、製品版で万一試験の入口が残った時の保険として「製品版(`!Debug.isDebugBuild`)で `RunLedger.Current.debugUsed` のランは MILE を入れない」を足すと、上の 1/3/4/5 の経済への影響がまとめて消える。
- **マルチの HOST は JOIN の申告を広く信じる**: 与えたダメージ(`NetCombat.cs:860` で 999,999 まで)、回復/最大 HP の要求(`NetMatch.cs:345-361`)はそのまま受け付ける。今回の「試験の入口」とは別の話(改造した JOIN なら同じことができる)だが、ONLINE を公開するなら上限の見直しを検討してよい。

## 撮影用ビルド(OMM_CAPTURE)への影響

撮影用ビルドは `DEVELOPMENT_BUILD` を定義するので、`#if UNITY_EDITOR || DEVELOPMENT_BUILD` で囲んだ物はそのまま入る(今と同じ)。撮影が使う `QaSweep -storeCapture`、`FileSaveStore -fileSave`、`HighSpeedAssist`、`BossManager.DebugSpawnBossForTest` は、上の直し方のどれにも関係しない。撮影の手順(`report.md`)も `-netAuto` / `-encounter*` / `-bonusShots` / `-audioTest` / `-runBuildHudTest` を使っていない。
`Debug.isDebugBuild` で止める方を選んだ場合、撮影用ビルドでもその試験は動かなくなるが、撮影には不要なので問題ない。

## 直したあとの確かめ方(案)

1. 製品版(Development Build なし)の Windows exe を作る。
2. `OneMoreMile.exe -netAutoHost -netAutoOnline -encounterForce <ID> -bonusShots -audioTest -runBuildHudTest` で起動し、Player.log に `[NETTEST]` `[AudioTest]` `[RunBuildHudTest]` が出ないこと、ホーム画面のまま止まること、ランを始めても隊形が普通に変わることを見る。
3. 開発版で `-netAuto...` / `-audioTest` などが今までどおり動くことを見る。
