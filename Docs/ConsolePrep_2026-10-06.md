# 家庭用機への移植の準備(2026-10-06)

- 目的: Switch / PS5 などへ移す時に、差し替える場所をはっきりさせる。あわせて、PC のゲームパッドだけでゲーム全体を遊べるようにする(暫定)。
- 守ったこと:
  - スマホ(Android)の操作・見た目・セーブは変えない。
  - 入れ替えは「同じ動きの入口を1枚挟む」だけにする。

## 1. 構造

```
ゲームのコード
  ├─ 入力   GameInput(行動: Jump / AttackForward / … / Confirm / Cancel / Pause / Nav* / Tab* / Scroll* / Hold)
  │           └ IInputSource: KeyboardInputSource / LegacyGamepadInputSource(PC)… 機種ごとに差し替え
  ├─ メニュー PadNav(フォーカスの移動と決定。IMGUI のボタン + uGUI の画面のタップ判定を同じ仕組みで)
  │           ├ PadNav.Button(rect)           … DrawStyledButton / UiKit.Button / 部屋の操作対象 / 闘技場 など
  │           ├ UiHit.Hit / UiHit.Probe       … デッキ編集 / キャラ選択 / ステージ選択 / カード合成 / 報酬カード / 確認の窓
  │           └ PointerInput / TouchInputUtil … パッドの決定 = フォーカスの中心を叩く(仮想タップ)
  └─ 機種のサービス Platform(PlatformServices.cs)
              ├ Save    ISaveStore      … PlayerPrefsStore(今)。ゲーム本体は SaveStore.* を呼ぶ(PlayerPrefs と同じ形)
              ├ Haptics IHaptics        … NullHaptics(今)。被弾/倒れた時に Play を呼んでいる
              ├ Online  IOnlineCaps     … LanMultiplayer(今: あり)。false でホームの「マルチ」を出さない
              └ Display IDisplayProfile … 今は何もしない(スマホの挙動を変えない)。起動時に Apply
```

### ファイル

| ファイル | 内容 |
|---|---|
| `Platform/GameInput.cs` | 行動、入力元、ボタン配置(`GamepadBindings`) |
| `Platform/PadNav.cs` | フォーカス、`UiHit`、`PointerInput` |
| `Platform/PlatformServices.cs` | `ISaveStore` / `SaveStore` / `IHaptics` / `IOnlineCaps` / `IDisplayProfile` / `Platform` |
| `ProjectSettings/InputManager.asset` | 旧 Input Manager のパッドの軸 `Pad LX/LY/RX/RY/DX/DY/LT/RT` を追加 |

## 2. ボタン配置(仮。Xbox 表記。PS: A=×、B=○、X=□、Y=△)

| 場面 | 操作 |
|---|---|
| ラン中 | A=ジャンプ(上フリック)、X=前攻撃、Y=後ろ攻撃、B=下攻撃 |
| | 右スティック=フリックと同じ4方向 |
| | RB / RT=ULTIMATE、START=一時停止 |
| | BACK(View)長押し=脱出(1000m 以降) |
| 疾走出発 | 十字キー上下 / A / B でレーン |
| メニュー | 左スティック / 十字キー=移動、A=決定、B=戻る |
| | LB / RB=タブ(闘技場)、右スティック上下=一覧のスクロール |
| | スライダーはフォーカス中に左右で 5% ずつ |
| 結果画面 | A で進む |
| 闘技場 | START=設定を開く / 閉じる |
| キーボード | 矢印 / WASD=移動、Enter / Space=決定、Esc / Backspace=戻る、P=一時停止、Q / E=タブ |
| | ラン中: Space / W / ↑=ジャンプ、Z / D / →=前、B / X / A / ←=後ろ、S / ↓=下、C=ULTIMATE、H 長押し=脱出 |

- ラン中の HUD のボタン(II、闘技場の左のボタン)は HUD 層に置いている。
  - フォーカスは行かないので、ラン中の A はジャンプのまま。
  - 停止メニュー / 報酬カード / 確認の窓が開くとメニュー操作に切り替わる。
- フォーカスの枠は、パッド / キーボードを使っている時だけ出る。タッチ / マウスを使うと消えるので、スマホの見た目は変わらない。
- Esc(Android の戻る)は機器の切り替えに数えない。

## 3. Switch / PS5 での差し替え口

1. 入力
   - Input System の `IInputSource` を作り、`GameInput.SetSources(...)` で入れ替える(家庭用機は Input System が前提)。
   - ボタン配置は `GamepadBindings` と入力元の表だけ。決定 / 戻るの入れ替え(日本の○決定など)もここで決める。
2. 保存
   - 機種のセーブ API の `ISaveStore` を作り、`Platform.Install(save: …)` で差し替える。
   - キーと形式(`SaveKeys` / `SaveSystem`)はそのまま使う。
   - 書き込み中の表示、容量、ユーザー切り替えは機種の要件に合わせる。
3. 振動
   - `IHaptics` を作る(Input System の `Gamepad.SetMotorSpeeds` 等)。
   - 呼び出し(被弾 / 倒れた)は入れてある。
4. オンライン
   - `IOnlineCaps.LanMultiplayer=false` でマルチを隠す。
   - LAN の自動発見(UDP ブロードキャスト)と NGO / UTP の直接接続は、機種のネットワーク規約に合わせて無効または置き換える。
5. 画面 / 品質
   - `IDisplayProfile.Apply` で解像度 / フレームレート / 品質を決める。
   - UI は画面の短い辺で拡大するので、1920×1080 と 1280×720 で確認済み。
6. 起動
   - `Platform.Boot`(`RuntimeInitializeOnLoadMethod BeforeSceneLoad`)で機種ごとに `Install` する。

## 4. まだ Android(スマホ)に依存している所

| 所 | 内容 |
|---|---|
| `Net/LanMulticastLock.cs` | Android の WifiManager(`#if UNITY_ANDROID`)。LAN の自動発見だけ |
| 画面の向き | `GameManager` / `ViewModeToggle` の `Screen.orientation`(縦画面の切り替え)。据え置き機では設定から隠す必要あり(未対応) |
| タッチだけの操作 | ラン中の COMBO のアイコンを押して説明を見る(`RunBuildHud`) |
| | 闘技場のカード検索の文字入力 |
| | マルチの ADVANCED(IP の直接入力、開発用) |
| | CARD BALANCE TEST(開発用) |
| 入力の残り | ゲーム本体のタップ / フリックはまだ旧 Input Manager の `Input.touches` / `Input.mousePosition` を直接読む(`PlayerController` / `UltimateArt` / 各 uGUI 画面)。Input System へ移す時はここも替える |
| セーブ | ゲーム本体は `SaveStore` 経由になった。自動テスト(`QaSweep.*`)と開発用(`DebugPanel` 等)は `PlayerPrefs` のまま(製品版に入らない / 同じ保存先) |

## 5. テスト

`-qaPad <dir> [-qaPadShots 1]`(開発版のビルド)。入力は `GameInput.Inject`(パッドと同じ「行動」)だけで、タッチ / マウスは使わない。

| 記号 | 内容 |
|---|---|
| A | ホームでフォーカスが出て動く。扉 → ステージ選択(uGUI)→ B で戻る |
| B | 出発 → A=ジャンプ、X=前攻撃 → START=停止 → B=閉じる → START → RETURN TO HOME → 確認 → ホーム |
| C | デッキ編集(ベッド): 枠を外す / 入れる、B で閉じる |
| D | 設定: スライダーを左右、B で閉じる |
| E | 闘技場: 準備画面、RB でタブ、START で閉じて戦闘 / 開く |
| G | タッチを使うと枠が消える |

- 1280×720 と 1920×1080 の両方で合格。
- タッチ側の回帰: キャラ選択、UI、キャラカード、Mastery、Retry、Save、闘技場、疾走出発、Resume、COMBO。

## 6. 限界(未確認)

- 実機のパッドでの確認は未実施。Windows のビルドで、入力を注入して確かめた。
  - 旧 Input Manager の軸番号は XInput の配置を前提にしている。DirectInput のパッドは軸がずれることがある。
- Android にパッドをつないだ時の動作は未確認。軸は同じ名前で読むが、機種によって番号が違うことがある。
- 本番の家庭用機の SDK は入っていないので、差し替え口は空の実装だけ。
