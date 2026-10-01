# OneMoreMile セーブデータ仕様(2026-10-01)

保存先はすべて Unity の `PlayerPrefs`(Android=アプリ専用の shared_prefs XML / Windows=レジストリ)。
ゲーム本体がファイルへ保存している物は無い(テスト用ツールの出力だけ)。
バックアップだけ `Application.persistentDataPath/SaveBackups/` にファイルで置く。

全キーの表は `Assets/Scripts/Save/SaveKeys.cs`。**新しい保存項目を足したら必ず登録する**(分類で、製品版移行の初期化・バックアップ・検査の対象が決まる)。

## キー一覧

| キー | 分類 | 型 | 内容 |
|---|---|---|---|
| SaveSchemaVersion | Meta | int | セーブ形式の版(今 1)。アプリの versionCode とは無関係 |
| SaveReleaseGeneration | Meta | int | リリース世代(0=開発版 / 1=製品版) |
| OwnedCardsV1 | 進行 | JSON | 所持カード(cardId=能力一式の `v2|…` キー / Lv / 枚数)。サブ能力はキーに含まれる |
| NewUnconfirmedCardsV1 | 進行 | 文字列 | NEW表示の未確認カード |
| CardDataFormat | 進行 | int | カードの旧形式→能力一式形式の変換済み印(2) |
| DeckCardIds | 進行 | 文字列 | デッキ(最大10) |
| CharacterCardSlots | 進行 | 文字列 | キャラ専用カード枠 |
| TotalOwnedMile | 進行 | int | 所持MILE(通貨) |
| BestDistance | 進行 | float | 全体の最高距離(ガチャの段階・距離解放の判定) |
| BestTime | 進行 | float | 最高記録の時間 |
| BestDistance_legacyBackup | 進行 | float | 旧・共通BESTの退避(マップ別BEST導入時の控え。今は参照されない) |
| BestDistance_v2_&lt;stageId&gt; | 進行 | double文字列 | マップ別BEST |
| UnlockedIds | 進行 | 文字列 | 距離で解放した敵/カード/エリア(起動時に BestDistance から補完もされる) |
| ActiveRunCheckpointV1 | 進行 | JSON | 中断中のラン(CONTINUE) |
| SelectedCharacterId | 進行 | 文字列 | 選択中のキャラ |
| SelectedStageId | 進行 | 文字列 | 選択中のステージ |
| LifetimeDistance | 進行 | double文字列 | **新規** 累計走行距離 |
| ReaperMet_Eldest / _Second / _Youngest | 進行 | int | **新規** 死神三姉妹と遭遇 |
| FinalDungeonUnlocked | 進行 | int | **新規** ラスダン(LAST CORRIDOR)解放 |
| MasterVolume / BgmVolume / SfxVolume / EnvVolume | 設定 | float | 音量 0〜1 |
| AudioMuted | 設定 | int | 消音 |
| ScreenShakeEnabled | 設定 | int | 画面揺れ |
| GlowIntensity | 設定 | float | 発光演出の強さ |
| HighSpeedAssistEnabled / HighSpeedAssistEngageKmh | 設定 | int / float | 高速時の自動操作補助 / 開始速度 |
| PreferredOrientation | 設定 | int | 画面の向き |
| net.lastHostIp / net.lastPort / net.mode | 設定 | 文字列/int/int | マルチの接続先とモード |
| InvincibleMode / DebugMode / Dev.FinalDungeonAlwaysOpen | 開発 | int | 開発版だけ(リリース版では読まない) |
| CardTest.&lt;key&gt;.&lt;0..2&gt; | 開発 | float | カード調整パネルの候補値(表の対象外) |
| MasterVolumeLevel 等4つ | 旧 | int | 旧い0〜4段階の音量。形式1への移行で0〜1へ読み替えて削除 |

保存していない物: キャラの解放(全キャラ最初から使える)、キャラの能力(アセットの値)、ボスの撃破、ガチャの段階(BestDistance から計算)、振動・言語(機能自体が無い)。

## 起動時の処理(SaveSystem、シーン読み込み前に1回)

1. **新規インストール**(形式の版も進行も無い)→ `DefaultSave.WriteNewProgress()`。
2. **形式の移行**: `SaveSchemaVersion` が `CurrentSchemaVersion` より古ければ1段ずつ変換。
   変換前に `pre_migration_v*.json` を書き、失敗したらその状態へ戻して旧い形式のまま起動(次回また試す)。
   保存している版の方が新しい(アプリを戻した)時は何もしない。
3. **リリース世代**: 保存の世代 < ビルドの世代(`SaveSystem.BuildReleaseGeneration`)の時だけ、
   `release_reset_*.json` を残してから **進行と開発用の値だけ** 初期化し、設定は残す。保存の世代をビルドの値にするので二度と走らない。
   ビルドの方が古くても(世代が下がっても)何も消さない。**アプリの更新(versionCode)では何も初期化しない。**
4. **検査と修復**: 壊れたJSON(所持カード/中断中のラン)、負のMILE、NaN/負のBEST、読めないマップ別BEST/累計距離を検出。
   壊れた中身は `corrupt_*.txt` に退避し、直近の正常な状態(`lastgood.json`)から戻す。戻せない時だけその項目を初期値にする(全体は上書きしない)。
5. 正常を確認した状態を `lastgood.json`(1つ前は `lastgood_prev.json`)に書く。

## 製品版の初期状態(DefaultSave)

- キャラ: 一覧の先頭(現在の仕様では全キャラを最初から使える)
- カード: 距離0で解放済みのカードの先頭10枚を各1枚Lv1で所持し、デッキにする
- MILE 0 / マップ別BEST 無し / 累計走行距離 0 / 距離解放 無し / 中断中のラン 無し
- 三姉妹の遭遇 すべて false / ラスダン 未解放 / ステージ 荒野街道

## 累計走行距離・三姉妹・ラスダン(ProgressStats)

- 累計走行距離: ランで距離が伸びた分を足す(マルチも含む。デバッグの距離ワープは含めない)。
  100mごと・死亡/正常終了・途中帰還・アプリの一時停止・終了で保存。強制終了で失うのは最大100m弱。
- 三姉妹: 「正式に出現した」時点(100,000mの追跡の死神 / ラスダン最終戦)で false→true、その場で保存。その後死んでも残る。
- ラスダン: 累計 ≥ 1,000,000m かつ 3人とも遭遇 → `FinalDungeonUnlocked=1`。一度解放したら条件の値が変わっても戻さない。
- ステージ選択: `StageDatabase.IsAvailable`。開発版は `Dev.FinalDungeonAlwaysOpen`(既定ON)で常に選べる。リリース版は解放フラグだけを見る。

## 製品版を作る時の手順

1. `SaveSystem.BuildReleaseGeneration` を 1 にしてリリースビルドを作る。
2. 開発版のデータを持った端末では、初回起動時に一度だけ進行が初期化される(設定は残る。元のデータは `release_reset_*.json` に残る)。
3. 以後のアップデートでは 1 のまま変えない。保存形式を変える時は `CurrentSchemaVersion` を上げ、`SaveSystem.MigrateStep` に1段の変換を足す。
