# ラスダン終盤 / エンディングの開発用ワープ(2026-10-02)

開発版(Editor / Development Build。Windows の開発版 exe と開発版 APK)だけで使える。
リリース版には、ページも、ラン中の表示/ボタンも、起動経路も入らない(コードごとコンパイルされない)。
確認方法は `ReleaseCompileCheck.Run`(下記)。

## 開き方

- ホームの **DEBUG** → 右上の **「ラスダン終盤…」**。
- DEBUG RUN 中は、画面左下の赤い帯「DEBUG RUN · NO SAVE / NO RECORD」の **≡ DEBUG** から同じページを開ける。
  - ラン中・RESULT の上からでも開ける。三姉妹戦で倒れても、ここから即再挑戦できる。

## 項目

| ボタン | 始まる所 | 確認できること |
|---|---|---|
| LAST DUNGEON 0km | ラスダンの開始状態 | 通常のラスダン |
| LAST DUNGEON 90km | 89,900m(ボスラッシュの100m手前) | 90〜98kのボスラッシュ(雑魚なし、順番、同時2体、時間差、撃破→次) |
| LAST DUNGEON 99km | 98,950m(最後の関門の後) | 99〜100kの静寂(距離表示、曲が消える、暗転)→ そのまま三姉妹へ |
| REAPER SISTERS BOSS | 99,950m(100,000mの50m手前) | 三姉妹戦 → 撃破 → エンドロール |
| ENDING CREDITS | 静寂区間(約99,030m)で、三姉妹撃破後と同じ状態から | 文字の位置/乗れる/攻撃/石板/END → ONE MORE MILE? |
| ONE MORE MILE? | 同上、YES/NO の選択エリアから | YES/NO の攻撃・ひび・確定・他方のロック、YES→BEYOND、NO→減速→停止→暗転→ホーム |
| LAST ENDING FLOW | 98,950m から通し | 静寂 → 100,000m → 三姉妹 → エンドロール → ONE MORE MILE? → YES/NO |
| NEXT BOSS | ボスラッシュ中だけ | 今いるボスを倒す → 同じ関門の次のボス、または次の関門の40m手前へ |

性能の切り替え(ページ上部):

| 性能 | 内容 |
|---|---|
| 通常性能 | そのまま |
| 死亡しにくい(既定) | HP が60%を切ったら満タンへ |
| 死亡しにくい+攻撃UP | 上に加えて攻撃力 +120 |

キャラとデッキは、今選んでいるものをそのまま使う。

## 毎回まっさらから始める仕組み(EndgameDebug.Launch)

1. ボタンを押すと状態を戻す(SafeReset)。
   - 時間 / 一時停止 / ヒットストップ(`TimeControl.ResetAll`、`timeScale = 1`)
   - 音の一時停止、速度の上書き(`DebugSpeedScale` / `DebugRunOnlyScale` / `ScriptedSpeedCapMps`)
   - カメラの寄せ、曲の上書き(`BgmDirector`)
   - ラスダンの静的な状態(ボスラッシュ / 関門停止 / 三姉妹の差し込み / 帰還禁止 / 経験値停止 / 文字の足場 / 三姉妹HPの試験値)、DEBUG パネルの入力止め
2. シーンを読み直す。
   - 旧ボス・死神・敵・飛び道具・演出・UI・コルーチン・コライダーは全部消える。
   - AudioManager(シーンをまたぐ)の曲は、上書きを戻してあるので二重にならない。
3. ホームで DEBUG RUN を始め、選択中のステージを保存せずにラスダンを開始する。
4. カウントダウンの後、その地点へワープする。
   - 距離のワープ + ボス戦の除外距離を0に戻す + スタート地点で出ていた敵/障害物を消す。
   - エンドロール / ONE MORE MILE? は、ワープ前の地形を抜けて静寂区間(平ら、穴・障害物・敵なし)へ入ってから始める。
   - 三姉妹撃破後と同じ状態にする: 関門停止 / 帰還禁止 / 経験値停止 / 保留の選択を捨てる / 100,000m の三姉妹戦を出さない。

## 正式記録に残さない仕組み(DebugRun、Scripts/Save/DebugRun.cs)

- `DebugRun.IsActive` = 記録対象外のラン。リリース版では常に false(開始する処理がコンパイルされない)。
- **1) 保存の入口で止める**。DEBUG RUN 中は書かない(止めた回数は `[DebugRun] save blocked:` のログ)。対象は次のとおり。

  | 分類 | 保存先(PlayerPrefs) |
  |---|---|
  | BEST | マップ別BEST、全体の BEST / 時間 |
  | 通貨 | MILE |
  | カード | 所持カード、NEW 表示 |
  | 進行 | 距離で解放したもの、デッキ、選択中のステージ |
  | 中断中のラン | CONTINUE の保存 / 消去 |
  | 累計・解放 | 累計走行距離(ワープした距離も足さない)、三姉妹の遭遇、ラスダン解放 |
  | ランキング / オンライン記録 | 現在このゲームには無い |

- **2) 控えへ戻す**。
  - 開始時に「進行」(SaveKeys の Progress 分類)の全キーを控える(メモリ + `persistentDataPath/DebugRunRestore.json`)。
  - 終了時(ホームへ戻る / 次のワープ / アプリの終了)に、控えと違うキーを元へ戻す(正常なら0件)。
  - 設定(音量など)と開発用の値(無敵など)は戻さない。
- **3) 落ちた時**: 次の起動で、SaveSystem の起動時処理がファイルから進行を戻す。

## 状態遷移ログ(開発版)

| タグ | 出るタイミング |
|---|---|
| `[FinalDungeon] Enter <m>` | ラスダンに入った時(ワープ先の距離) |
| `[BossRush] Start` / `Gate <m> Start` / `Boss <n> Spawn` / `Boss Defeated` | ボスラッシュ |
| `[SilentSection] Start` | 静寂区間 |
| `[ReaperBoss] Start` / `Solo i/3 Start` / `Group Start` / `Defeated` | 三姉妹戦 |
| `[Ending] Credits Start` | エンドロール開始 |
| `[Ending] Credits Finished -> [EndingChoice] Start` | ONE MORE MILE? 開始 |
| `[EndingChoice] YES Selected` / `NO Selected` / `[Ending] Finished (Home)` | 選択と終了 |
| `[GameState] ...` | DEBUG RUN 中、GameManager の大きな状態が変わった時 |

- ラスダンの各遷移には `GM started/over/win/ts/pause/levelUp/choiceSeq/boss/lives/d/autoRun/v/idle/finishing/escapeBlocked/expBlocked` を併記する。
- `[GameState]` の内容: 開始 / 終了 / 勝利 / 時間 / 選択 / ボス / 自動前進 / 立ち止まり / 帰還禁止。

## 自動テスト

```
OneMoreMile.exe -ldQa <dir> -ldQaMode warps
```

A〜J と、落ちた時の復旧を確認する。結果は `<dir>/ld_warps.txt`。

## リリース版の確認

```
Unity.exe -batchmode -projectPath ... -executeMethod ReleaseCompileCheck.Run -quit
```

- `DEVELOPMENT_BUILD` 無しでコンパイルする。出力は `Builds/ReleaseCompileCheck/Assembly-CSharp.dll`。
- `EndgameDebug` / `LastDungeonQa` / `DebugBeginCredits` / `NO SAVE` が DLL に含まれないことを確認する。
