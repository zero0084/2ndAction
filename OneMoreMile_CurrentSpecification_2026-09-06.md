# One More Mile — 現在実装仕様書 (2026-09-06)

本書は「今後こうしたい」という新規仕様書ではなく、**現時点のコード・Data・Prefab・UIを読んだ事実ベースの棚卸し**です。推測で埋めた箇所はありません。判断できない箇所は明示的に「D. 不明」としています。

対象: `C:\GameProject\2ndAction\Unity\2ndAction`（Unity 6000.5.6f1, Built-in Render Pipeline）

## 分類凡例

| 記号 | 意味 |
|---|---|
| **A** | 現在実装済み（コード上で完結して動作する） |
| **B** | 一部実装 / 仮実装（動くが簡略化・プレースホルダー） |
| **C** | 未実装 |
| **D** | 仕様不明 / コード上判断しづらい |
| **E** | 以前の仕様が残っている可能性あり（コメント等に旧仕様の痕跡） |

数値は明記がない限り「現在コードにある実際の値」です。ゲームバランス的に未確定であることがコード内コメントで明言されている値には「(暫定値)」と付記しています。

---

## 1. Game Flow 【A】

| 状態遷移 | 実装 | 詳細 |
|---|---|---|
| TOP → Run Start | A | `GameManager.StartGame()`。`ScreenTransitionManager`経由（Gold Slash Wipe）で`HasStarted=true`、`ApplyCharacterCardEffects()`実行、BGM再生。Transition Manager不在時はフォールバックの素のFadeコルーチンあり。 |
| Gameplay | A | `HasStarted=true`かつ`!IsGameOver`の間、`PlayerController.Update()`が`transform.position.x - startX`を毎フレーム`GameManager.ReportDistance()`へ渡す。 |
| Level Up | A | `GameManager.GainExp()`が`Exp&gt;=ExpToNext`をwhileループで消化、都度`TriggerLevelUpChoice()`。Boss Presentation中/Boss Reward中は`levelUpDeferredPending`で遅延、`levelUpDeferredResumeDelay`(0.4秒)後に実行。 |
| Boss | A | `BossManager`が`MaxDistance&gt;=nextBossDistance`で`IsBossPhase=true`（演出開始前に即セット）。 |
| Boss Reward | A | `BossManager.CheckEncounterComplete()`→`GameManager.TriggerBossRewardChoice()`→（Boss Defeat演出待ち）→`RunBossRewardChoice()`。Level Upと同じ`RewardCardSequence`/`pendingChoices`機構を共有（`PendingChoiceKind`で区別）。HP全回復は**しない**（Level Upとの明確な差異）。 |
| ESCAPE | A | `GameManager.EscapeAvailable =&gt; HasStarted && !IsGameOver && escapeUnlocked`。`escapeUnlocked`は最初のBoss Reward完了時のみ`true`化される一方向フラグ（2026-09-06変更、旧仕様は距離1000m到達のみで解禁）。`escapeMinDistance=1000f`は現在Boss出現スケジュールの起点としてのみ使用され、Escape解禁条件からは切り離されている。Hold時間`escapeHoldDuration=3f`。誤爆防止用の`escapeChargeConfirmDelay=0.3f`あり（通常Tap/Swipeを長押しと誤判定しないための猶予）。 |
| FINISH | A | `PlayerController.DoEscapeSuccess()`→`GameManager.Win()`→`IsWin=true`→`FinishRun()`。`RunMile`は`IsWin`時のみ`AddMile()`で加算、`RunCheckpoint.Clear()`。 |
| GAME OVER | A | `TryDamagePlayer()`で`Lives&lt;=0`になった瞬間、死亡演出より先に`RunCheckpoint.Clear()`→`FinishRun()`（`IsWin=false`のまま）。`RunMile`は加算されず**全額喪失**。 |
| Return Home | A | `GameManager.ReturnToHome()`：`SaveInterruptState()`→`RetryWithTransition()`（GAME OVER再挑戦と同じシーンリロード経路を再利用）。`IsGameOver`/`IsWin`は不変、FINISHではない。Active Runは維持される。 |
| Continue | A | `GameManager.ContinueActiveRun()`→`BeginContinuedRun()`：Checkpoint距離へ復元、Build/能力は`upgradeHistoryCardIds`を`ApplyCardEffects`で再生して再構築（個別Stat保存ではない）。HPは中断時点の値のまま（全回復しない）。 |

**安全弁（2026-09-06追加、Boss Reward処理が止まった場合の対策）**：`bossRewardStuckTimer`（12秒、Presentation待ちがスタックした場合）と`pendingChoiceStuckTimer`（30秒、Card選択自体がスタックした場合）の二段構え。`RewardCardSequence`自体も「20秒間タップが無ければ最初のカードを自動選択」という既存の安全策を持つ。

---

## 2. Player 【A】（数値は全て`PlayerController.cs`/`GameManager.cs`のInspector公開フィールドの現在値）

| 項目 | 値 |
|---|---|
| Auto Run基礎速度 | `runSpeed=5f`。`speedUpStartDistance=100f`以降、100mごとに`speedUpPer100m=0.05`（+5%）、上限`maxSpeedMultiplier=2f`。 |
| Jump | `jumpForce=9f`、`maxJumps=2`（＝ダブルジャンプ）。 |
| Swipe Attack | `swipeThreshold=60px`。Swipeは指を離す前に閾値超えた瞬間に発動（Tapへは戻らない）。Tapは指を離した瞬間のみ、かつSwipe直後`jumpSuppressionAfterAttack=0.18秒`はJump抑制。 |
| Attack Combo | `maxComboChain=3`、`comboWindowStart=0.5`（`attackActiveTime=0.4秒`の50%地点でCombo入力受付開始）、`attackCooldown=0.3秒`。`AttackSpeedMultiplier`が両方に乗算される。 |
| HP | `startingLives=3`、`maxLives=5`（Card等で拡張、上限`maxLivesCap=10`）。 |
| Damage判定順 | GameOver済み→Presentation中→InvincibleMode→Shield消費→Lives-1。`hitInvincibleDuration=5秒`（被弾後の無敵点滅）。 |
| Death | `OnDeath()`：Sprite/Hitbox非表示、死亡SE、BGM Fadeout、Explosion VFX(0.6秒、16粒子)。 |
| Attack Range | Hitbox: `scaleMul=(1+step*0.18)*AttackRangeMultiplier`、位置も同係数でstep*0.25分前方へ。Visual(斬撃VFX)は同じ`AttackRangeMultiplier`をX軸へ全量・Y軸へ`rangeInfluenceY=0.4`倍のみ反映（2026-09-06追加、Hitboxとの乖離防止）。 |
| Movement Speed Card | `runSpeed *= 1+value`（乗算） |

---

## 3. Enemy 【A（サイズ調整含む）／一部C】

### 3.1 種別一覧（`EnemyDatabaseBuilder.cs`実測、.assetと一致確認済み）

| id | category | Behavior | hpMultiplier | mileReward | visualScaleMultiplier(現在値) | Facing |
|---|---|---|---|---|---|---|
| goblin | Normal | None | 1 | 1 | **1.14** | 有効(defaultFacingRight=false) |
| goblin_elite | Normal | None | 1 | 1 | **1.14** | 有効 |
| flying_wyvern | Flying | Flying | 1 | 2 | **0.92** | 有効 |
| irregular_imp | Irregular | Irregular | 1 | 2 | **1.69** | 有効 |
| shooter_archer | Shooter | Shooter | 1 | 3 | **1.19** | 有効 |
| heavy_ogre | Heavy | Heavy | 3(+bigKnockback) | 5 | **0.98** | 有効 |
| chaser_runner | Chaser | Chaser | 1 | 3 | **1.52** | 有効 |
| rusher_runner | Rusher | Rusher | 1 | 3 | **1.52** | 有効 |

全8種`enableVisualFacing=true`（2026-09-05修正でgoblin/goblin_eliteも有効化）。

**重要な技術的注記**: Chaser/Rusherの走行アニメーション5フレーム(`Assets/Art/RunnerRun/`)は2026-09-06に**PPUを724→374へ修正**（静止画ポートレートと走行フレームのPPUが食い違い、走行中の実世界サイズが静止画のほぼ半分になっていた不具合の修正）。

### 3.2 Behavior（`EnemySpecialBehavior.cs`、全てInspector公開・現在値）
- Flying: 一定高度を保ちX方向のみ追跡、`flyingStopDistance=3`、`flyingApproachSpeed=0.8`、正弦Bob。
- Irregular: Pause/Move/Hopを`0.5〜1.5秒`ごとにランダム選択、Leash範囲`±2.5`。
- Shooter: `shooterRange=11`以内で発射、Cooldown`1.6秒`、`shooterRetreatDistance=3.5`で後退。
- Heavy: `heavyDetectionRange=14`以内で接近、`heavyApproachSpeed=0.7`。
- Chaser: `chaseDetectionRange=16`、`chaseSpeed=3.2`。
- Rusher: Idle→Telegraph(0.35秒)→Dash(`7.5`速度、0.5秒)→Recover(1秒)のFSM、検知距離`8`。
- Chaser/Rusherはプレイヤーの`giveUpDistanceBehindPlayer=20`後方で自動非アクティブ化。

### 3.3 Spawn／Formation
- `EnemyWallManager`：距離間隔ベース（`wallInterval=500`、Debug`100`）、`GameManager.EnemySpawnRateMultiplier`で間隔短縮。`IsBossPhase`/`IsInSafeZone`中は完全停止。
- Formation：`EnemyFormationType`は10種（Single/SmallGroup/HorizontalLine/VerticalLine/Cluster/DiagonalUp/GroundAir/FrontlineShooter/HeavyNormal/Rush）。**依頼書にあった「Burst/Step」という区分はコード上に存在しません**（全て単一アンカーからの固定オフセット方式に統一済み）。Gap+Enemy/Pincer/Route Choiceの3種は明示的に未実装【C】（地形の複数経路生成が必要なため）。

### 3.4 HP Scaling
`DistanceTierManager.EnemyHpFor` = `Max(1, RoundToInt(CurrentEnemyHp * hpMultiplier * GameManager.EnemyHpMultiplier))`、`CurrentEnemyHp = 1 + floor(distance/2000)`（`hpIncreaseDistance=2000`）。

---

## 4. Boss 【A】

| 項目 | 値/実装 |
|---|---|
| Spawn Distance | `bossRepeatInterval=1000`(Debug`bossRepeatIntervalDebug=200`)。初回は1インターバル目(1000m)。`majinCycleLength=5`ごとにMajin+1・Dragonカウントがラップ。Mechanical Dragon追加は`mechanicalDragonUnlockDistance=20000`以降。Death(GrimReaper)は`deathSpawnDistance=100000`（一度きり、ラン終了しない）。 |
| Boss Gate | `GameManager.ReportDistance()`内`if (IsBossPhase) return;`が唯一のゲート。`EnemyWallManager`/`TerrainManager`も同フラグを参照して新規Spawn/Formation抑制。 |
| Distance Stop | **2026-09-06に修正**：`IsBossPhase`は以前「Boss討伐の瞬間」に`false`化していたが、現在は「Boss Reward処理完了時」（`SaveCheckpoint()`直後、`BossManager.EndBossPhase()`呼び出し箇所3箇所）まで維持される。 |
| Boss Defeat | `DragonController`/`MajinController.FinalHitAndDie()`：Final Hit演出→Death Flash/Scale Punch→Death Smoke→HP Bar Fade→非アクティブ化→`RegisterBossDefeat(mileReward)`→`OnDragonDefeated/OnMajinDefeated`→`CheckEncounterComplete()`。 |
| Boss Reward | Level Upと同一Deckプール、同一`RewardCardSequence`機構。HP全回復なし。Pool空の場合も`SaveCheckpoint`/`EndBossPhase`/`UnlockEscape`は必ず実行。 |
| Checkpoint | `SaveCheckpoint()`はBoss Reward完了に紐づく3箇所でのみ呼ばれ、これが`checkpointDistance`が進む唯一の場所。 |
| Resume | `RestoreNextBossDistance(checkpointDistance)`：`nextBossDistance = checkpointDistance + EffectiveRepeatInterval()`。 |
| Boss Scale | `dragonScale=2.3`、`majinScale=2.7`（2026-09-06、明確に巨大化）。`dragonMaxHp=20`、`mechanicalDragonMaxHp=30`、`majinHpMultiplier=6`（Majin実質HP=120）。MILE: Dragon50 / Majin100 / Mechanical Dragon200。 |

---

## 5. Distance 【A】

- 計算式：`PlayerController.Update()`が毎フレーム`transform.position.x - startX`を`ReportDistance()`へ渡す。
- `MaxDistance`（表示中の距離）、`HighestReachedDistance`（このRun生涯の最高到達、Checkpoint関係なく単調増加）、`BestDistance`（永続ベスト、PlayerPrefs）の3値が独立して管理。
- Distance MILE算出式：`FloorToInt(Max(MaxDistance, HighestReachedDistance)/100)`（`FinishRun()`内）。
- Boss中：4章の通りDistance停止。
- Continue時：`MaxDistance=checkpointDistance`、`HighestReachedDistance=Max(保存値, checkpointDistance)`。

---

## 6. MILE 【A】

| 項目 | 実装 |
|---|---|
| Run MILE | `RunMile =&gt; RunDistanceMile + RunEnemyMile + RunBossMile`（計算プロパティ） |
| Total MILE | `TotalOwnedMile`、PlayerPrefsキー`"TotalOwnedMile"`。`AddMile`/`TrySpendMile`。 |
| Distance MILE | 5章参照 |
| Enemy Kill MILE | `RunEnemyMile += Round(mileReward * MileGainMultiplier)`。Enemy種別ごとの基礎値は`EnemyDefinition.mileReward`（3章表）。 |
| Boss MILE | `RunBossMile += Round(mileReward * BossMileGainMultiplier)`。基礎値はBoss種別ごと固定（4章）。 |
| GAME OVER時 | `if (IsWin) AddMile(RunMile);` — **IsWin=falseなので加算されず、未確定MILEは全額喪失**。 |
| FINISH時 | 同条件文、`IsWin=true`なので全額加算。 |
| Continue時 | `RunEnemyMile`/`RunBossMile`はCheckpointから復元。`RunDistanceMile`は次回`FinishRun()`時に距離から再計算（保存されない）。 |

---

## 7. Card System 【A】

- `CardDefinition`フィールド：`cardId, cardName, icon, description, category, sortOrder, recommendPriority(既定5), effects[], rarity(既定1,1-5), unlockDistance(既定0), gachaStage(既定1,1-5), element(既定None)`、計算プロパティ`RarityStars`。
- Card Lv：`CardInventory.MaxCardLevel=5`。「Lv.N = N回`ApplyCardEffects`を積む」という設計（`ApplyCardEffectsStacked`）。Character Card装備時は装備Lv回、Run中Level Up/Boss Rewardは1回ずつ加算。
- Quantity/Stack：`CardInventory.Stack{cardId, level, count}`。(cardId,level)単位で所持数管理、個体別管理ではない。
- Character Cards：3枠（`CharacterCardSlotCount=3`）。
- Deck：`DeckCapacity=10`。**同名カードの重複登録を明示的に許可**（Speed Up×10も可）。所有数を超えた登録は拒否（Deck+Character Card使用数の合計でチェック）。
- Level Up/Boss Reward取得：どちらも同一のDeckベースPool→`ApplyUpgradeByCardId`。**CardInventoryには一切触れない**（Run限定強化として正しく分離されている）。
- EffectType：既存13種＋新規11種（Ground/ComboFinal/FirstHit/LowHp/FullHp/Momentum/BossDamageの各AttackPower系、EnemyHp/BossHp/MileGain/BossMileGainの各Multiplier系）、全て`GameManager.ApplyCardEffects`の単一switchで処理。
- **カード全84種の詳細一覧（id/Rarity/unlockDistance/gachaStage/element/効果）は別紙参照可能（本書では11章の表に集約、必要なら追加で全表出力可）。**

---

## 8. Card UI 【A（一部★1のみB/C）】

- Rarity Frame：`CardRarityFrames`が`Resources.Load&lt;Sprite&gt;("CardFrames/CardFrameRarity{2-5}")`を実行時に遅延ロード（2026-09-06、以前はEditor専用プロセスでのみ読み込む設計ミスがあり実機に一切反映されていなかった不具合を修正済み）。**★1は元画像にAlpha Channelが存在しない不具合により専用Frameが無く、常に旧汎用Frame(`CardFrame.png`)へフォールバック**【B/C】。
- Lv/Quantity表示：呼び出し元ごとにフォーマットが異なる文字列を`LevelLine`へ設定（専用UI Modeフィールドは持たせていない、over-engineering回避のためTitle/Description同様「呼び出し側が組み立てる」既存パターンを踏襲）。
  - Deck表示：所有していれば`"Lv.N"`のみ、未所有なら空欄。
  - Level Up/Boss Reward：`"Lv.現在 -&gt; Lv.次"`または`"NEW  Lv.1"`。
  - Collection：`"Lv.N xCount"`。
  - Character Card：`"Lv.N"`（Countなし）+EQUIPPEDバッジ。
  - Fusion一覧：`"Lv.N xCount"`+EQUIPPED/IN DECKタグ（Description側）。
- Gacha Result Popupのみ**IMGUI直書き**（RewardCardUIを使わない別実装）、それ以外(Level Up/Boss Reward/Collection/Deck/Character Card/Fusion)は共通の`RewardCardUI`（`CreateRewardCard`ファクトリ経由）。
- 2026-09-06にレイアウトを全面再調整（EQUIPPEDバッジの見切れ修正、Rarity/Level/Title/Descriptionへ`resizeTextForBestFit`導入）。

---

## 9. Fusion 【A（一部B）】

| 項目 | 値 |
|---|---|
| Same-name Fusion | 常に成功（確率判定なし）。同(cardId,Lv)を2枚消費→Lv+1を1枚付与。上限`MaxCardLevel=5`。 |
| Cross-name Fusion | Main/Sub各1枚を先に消費。`MainInheritChance=0.50`、`SubInheritChance=0.25`（**独立判定**、2つの別`Random.value`）。 |
| Main成功 | Mainを+1Lvで返却。Subも成功していれば追加で`FusionSubBonusMile=50`。 |
| Main失敗+Sub成功 | Mainは元Lvのまま返却、`FusionSubBonusMile=50`加算。 |
| 両方失敗 | Main/Sub共に消滅、`FusionFailureRefundMile=200`加算。 |
| Equipped Card制限 | `IsCardInUse`(Character Card装備中 or Deck使用中ならブロック)、Fusion画面では`GetAvailableCountForStack`(所有数−Deck/Character Card使用数)で選択可否を判定。 |

**【B】複合能力カード生成（"Attack Up【Vampire】"のような合成）は実装されておらず、Sub成功時はMILEボーナスのみに簡略化**（依頼当時から明示的に許可されていた簡略化）。

---

## 10. Gacha 【A】

| 項目 | 値 |
|---|---|
| Cost | `GachaCostMile=500` |
| Pool | `CardDatabase.AllCards`全体から`GachaStage.IsCardEligible`（`BestDistance&gt;=unlockDistance && currentStage&gt;=gachaStage`）を満たすものだけ抽出。**Deck/Level-Up用の別unlockシステム(`UnlockManager`)は一切参照しない**（＝Gachaで引けてもDeckに入れられない/その逆のケースが構造上存在しうる）。 |
| Rarity Weight | `{50, 30, 15, 4, 1}`（★1〜★5、**暫定値**、依頼時点で最終値未確定と明言）。Pool内に存在しないRarityは重み0扱い（バケット自体が生成されない）。 |
| Gacha Stage | `GachaStage.StageForDistance(BestDistance)`— **保存されない、常にBestDistanceから再計算**。 |
| Gacha Evolution | 5段階、Tintのみ（専用アートなし、`GachaMachineStageTint`のColor.white置換で無効化可能な設計） |
| Visual | Home Room上、機の位置は2026-09-06に`x=0.85/y=0.50`へ再調整＋接地影(Contact Shadow)追加。 |
| Animation | MILE消費/カード付与は**タップ直後に同期実行**、Shake→Popupは純粋な演出遅延。★4/★5抽選時のみ`GachaResultRevealDuration=0.8秒`のパルス発光。 |

---

## 11. Gacha Distance Unlock 【A】

`GachaStage.Thresholds = {0, 5000, 20000, 50000, 100000}`、`StageNames = {OLD, REPAIRED, MECHANICAL, MAGICAL, DEATH-TOUCHED}`。**Poolは累積式**（一度解禁されたカードは後段でも排出プールに残り続ける、`IsCardEligible`に削除ロジックは一切存在しない）。

| BEST到達距離 | Stage名 | 新規解禁カード数 | 主な内容 |
|---|---|---|---|
| 0m | OLD | 22種 | 既存基礎12種＋Risk系新規10種（more_enemies, brake_attack, first_strike, executioner, treasure_hunter, combo_plus, mob_killer, ground_fighter, tough_enemies, fast_enemies）全て★1-2 |
| 5,000m | REPAIRED | 23種 | 既存Build系4種（vampire, berserker, heavy_armor, greed）＋新規★3　19種（combo_edge〜boss_challenge） |
| 20,000m | MECHANICAL | 29種 | ★4基本能力発展・属性カード群（momentum〜wind_cutter、Thunder/Fire/Ice/Wind属性タグ付き4種含む） |
| 50,000m | MAGICAL | 5種 | hell_mode/boss_rush/wanted(★4)＋phoenix/ultimate(★5) |
| 100,000m | DEATH-TOUCHED | 5種 | one_more_mile/deaths_contract/no_turning_back/the_long_road/pandemonium（全★5） |

合計84種（22+23+29+5+5）。

**別系統（Deck/Level-Up側）のunlock**：`UnlockManager`/`UnlockDefinition`は現在**2件のみ**（`pathfinder`カード＝500m、`goblin_elite`＝1000m）。これはGachaのunlockDistanceとは完全に独立した別の距離判定です（例：pathfinderはGachaでは0mから排出されるが、Deckに入れられるのは500m到達後）。

---

## 12. Home Room 【A】

| 項目 | 実装 |
|---|---|
| Door | `FracRect(0.40,0.14,0.565,0.65)`。`RunCheckpoint.HasActiveRun`でCONTINUE/NEW RUNとStart Runを分岐。NEW RUN選択時は確認ダイアログ必須。 |
| Card Edit | ベッド`FracRect(0.0,0.52,0.32,1.0)`→`DeckEditUI`（Canvas UI、Collection/Deck/Character Card 3画面構成、Convert機能あり）。 |
| Fusion | 本の山`FracRect(0.78,0.78,1.0,1.0)`→`CardFusionUI`。 |
| Gacha | 机上のプロップ（部屋絵自体には存在せず動的描画）、`x=0.85/y=0.50`、影付き。 |
| BEST/MILE | 画面左上/右上に常時表示（Home Room・in-run HUD双方）。 |
| Continue | Doorの挙動に統合（専用ボタンなし）。 |
| Active Run | `RunCheckpoint.Data.active`。GAME OVER/FINISH/NEW RUN確定時のみ`Clear()`、Return Homeでは維持。 |

---

## 13. Save / Load 【A】

PlayerPrefsキー一覧（Assets/Scripts全体をgrepし網羅確認）：

| キー | 所有クラス | 内容 |
|---|---|---|
| `BestDistance` | GameManager | 永続ベスト距離 |
| `BestTime` | GameManager | 永続ベストタイム |
| `PreferredOrientation` | GameManager | 画面向き設定 |
| `InvincibleMode` | GameManager | Debug無敵トグル |
| `DebugMode` | GameManager | Debug表示トグル |
| `DeckCardIds` | GameManager | 現在のDeck構成 |
| `TotalOwnedMile` | GameManager | MILE所持総量 |
| `CharacterCardSlots` | GameManager | Character Card 3枠 |
| `ActiveRunCheckpointV1` | RunCheckpoint | Active Run/Checkpoint全体(JSON) |
| `OwnedCardsV1` | CardInventory | 所持カードStack一覧(JSON) |
| `UnlockedIds` | UnlockManager | Deck/Level-Up側の距離解禁フラグ |
| `BgmVolumeLevel`/`SfxVolumeLevel` | AudioManager | 音量設定 |

**Gacha Stageは保存されません**（10章参照、常にBestDistanceから再計算）。

`RunCheckpoint.Data`保存項目：`active, checkpointDistance, highestReachedDistance, lives, maxLives, level, exp, expToNext, enemyKillCount, bossKillCount, runEnemyMile, runBossMile, escapeUnlocked, upgradeHistoryCardIds`。

---

## 14. Pause / Return Home 【A】

- Pauseボタン：画面右下、`levelUpPending`中は非表示（Level Up/Boss Rewardの独自Pauseと二重化させないため）。
- RESUME：`Time.timeScale=1f`のみ。
- RETURN TO HOME：確認ダイアログ→`SaveInterruptState()`→シーンリロード。FINISHではない、Active Run維持。
- NEW RUN：Active Runが既に存在する状態でのみ表示、確定時`RunCheckpoint.Clear()`。

---

## 15. Presentation 【A】

- Scene Transition：`ScreenTransitionManager`のGold Slash Wipe（Close 0.25s/Hold 0.08s/Open 0.25s、Angle 32度）。Level Up/死亡演出には使用されない。
- Level Up：`RewardCardSequence`、Announcement→Deck出現→Card Draw→Flip→Idle Pulse→**20秒タイムアウト付き選択待機**→選択演出→Apply、という多段階シーケンス（詳細な各秒数は本文参照可）。
- Boss Spawn：`BossMilestonePresentation`、`Time.timeScale`を実際に1→0.4→0まで落とす本物のテンポダウン演出。初回`2秒`/以降`1.1秒`。
- Boss Defeat：`BossDefeatPresentation`、Gameplayを止めない演出。初回のみ「GAME CLEAR」「KEEP RUNNING→」を追加表示。
- Death：`PlayerController.OnDeath()`、Sprite/Hitbox非表示＋Explosion VFX(0.6秒)。専用のDeath Presentationクラスは無く、Result画面(`DrawResults`)の「FAILED」ヘッドラインで代替。
- Card Reveal：`RewardCardUI.FlipToFront`/`FlashFrame`/`StartIdlePulse`。Rarity Frameは`CardRarityFrames`経由（8章参照）。

---

# 不整合・要注意リスト

以下は実装調査中に見つかった「仕様と実装の食い違い」「旧仕様の残存」「今後バグの温床になりうる箇所」です。

1. **【重要/E】Enemy Formationの「Burst/Step」区分は現在のコードに存在しない。** 現行は単一の`EnemyFormationType`(10種、固定オフセット方式)に統一されており、以前の会話で言及された"Burst=一括配置/Step=複数チャンクにまたがる配置"という区分は見当たりませんでした。マスターの想定とズレている可能性があります。

2. **【重要/B】★1カードには専用Rarity Frameが存在しない。** 提供された元画像にAlpha Channelが無く(Format24bppRgb)、常に旧汎用フレームへフォールバックしています。本物の透過PNGが必要です。

3. **【重要/D】「Boss撃破後にゲームが停止する」報告への対応は、根本原因を特定できないまま2段のタイムアウト安全弁（12秒/30秒）で対処した状態です。** 真の原因（何が`IsRunning`/`levelUpPending`を止めているか）は未特定のままなので、再発する場合はログを要確認。

4. **【B】Gacha PoolとDeck/Level-Up Poolは完全に独立した別のunlockシステムを使用しています。** `UnlockManager`(2件のみ登録：pathfinder@500m, goblin_elite@1000m)と、Card自体が持つ`unlockDistance`/`gachaStage`フィールドは無関係。同じ「距離による解禁」という概念が2箇所に分散しているため、将来カードを追加する際にどちらの系統に登録すべきか混同しやすい設計です。

5. **【B】Fusionの複合能力カード生成は未実装。** Sub成功時は本来期待されていたであろう「Main+Subの複合効果を持つ新カード」ではなく、MILEボーナス(+50)のみに簡略化されています。

6. **【D】`GameManager.IsCardInUse`と`GetAvailableCountForStack`系メソッドは、目的の似た「使用中カード判定」を2つの異なるロジックで別々に行っています。** 前者はcardId単位の名前ベース判定、後者はDeckの実使用数をLv昇順で貪欲に割り当てる近似ロジック。同じ概念を2箇所で管理しているため、将来の改修で片方だけ直して不整合を起こすリスクがあります。

7. **【E】Enemy Visual Sizeの調整は本セッション中に3回再計算されています**（当初Goblin基準→Player基準へ再測定→Runner種のPPU不整合発見で再修正）。現在の値は全て2026-09-06時点の最新値ですが、実機での見た目確認はまだ完了していません。

8. **【D】Gacha機のHome Room内位置（x=0.85/y=0.50）は、Editor側の画像合成テストでは正しく見えたものの、実機で「浮いて見える」という報告が2回続いています。** 正確な実機アスペクト比を確認できていないため、今回の調整も確証を持てていません。

9. **【B】Chaser/Rusherの`useRunnerRunFrames`使用時、走行フレームの実測は5フレーム中の代表値による近似です。** 全フレームの精密な逐次差分までは調整しきれていない可能性があります。

10. **【D】`RunCheckpoint.escapeUnlocked`は2026-09-06に追加されたばかりのフィールドです。** それ以前に保存された古いセーブデータ（フィールド自体が存在しない）を読み込んだ場合、C#のデフォルト値`false`になります（既存Runなら再度Boss討伐が必要になり、実質的な後退が発生する可能性があります）。

11. **【未検証/D】`AddMile`が負の値のamountを渡された場合の下限保護(`Mathf.Max(0,...)`)は存在しますが、現在それを負値で呼び出すコード箇所は見当たりません。** 将来「MILEを消費するCard効果」等を追加する際の設計流用先として認識しておくと良い箇所です。

---

*本書はコード読解時点(2026-09-06)のスナップショットです。以後の修正で内容が古くなる可能性があります。*
