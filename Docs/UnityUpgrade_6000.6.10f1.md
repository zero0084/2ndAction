# Unity 6000.5.6f1 → 6000.6.10f1 の更新(2026-11-25)

- 作業ブランチ: `unity-6000.6.10f1`。更新前の状態: タグ `pre-unity-6000.6.10f1`(6000.5.6f1)。
- パッケージは一括更新していない。下の表は **Unity がこのエディターの最低版として自動で上げた物** とその依存。
  manifest.json を旧版へ戻して開き直しても再び上げられることを確認した(= このエディターでは旧版を使えない)。
- コードの修正: `Assets/Editor/SceneBuilder.cs` の `AndroidPlatformIconKind.Round`(6000.6 で廃止)。最低対応 API 26 ではアダプティブアイコンが丸いアイコンを兼ねるため、Round の枠への設定を外した(エディター用の道具。ビルドの経路ではない)。
- 画像の .meta 2,358 件: 新しいエディターがスプライトの範囲(sprites/internalIDToNameTable)を書き足した。参照の ID(1 枚もの = 21300000)は変わらず、
  `Assets/Editor/MissingRefAudit.cs` で参照切れ 0(Prefab/ScriptableObject の参照 1,952 件・うちスプライト 1,208 件、シーンの参照 10,537 件)。

## 版が変わったパッケージ(packages-lock.json の比較)
| パッケージ | 更新前 | 更新後 | manifest | 取得元 |
|---|---|---|---|---|
| com.unity.burst | 1.8.29 | 2.0.0 | 依存 | builtin |
| com.unity.collections | 6.5.0 | 6.6.0 | 依存 | builtin |
| com.unity.modules.tetgen | (なし) | 1.0.0 | 直接 | builtin |
| com.unity.multiplayer.center | 1.0.1 | 2.1.0 | 直接 | builtin |
| com.unity.multiplayer.tools | 2.2.9 | 2.2.12 | 直接 | registry |
| com.unity.services.authentication | 3.7.3 | 3.8.1 | 依存 | registry |
| com.unity.services.multiplayer | 2.2.4 | 2.4.1 | 直接 | registry |
| com.unity.services.qos | 1.4.1 | 1.5.1 | 依存 | registry |
| com.unity.services.wire | 1.4.4 | 1.6.2 | 依存 | registry |
| com.unity.test-framework | 1.7.0 | 1.8.0 | 依存 | builtin |
| com.unity.test-framework.performance | 3.5.0 | 6.6.0 | 依存 | builtin |
| com.unity.transport | 6.5.0 | 6.6.0 | 依存 | builtin |
| com.unity.ugui | 2.5.0 | 2.6.0 | 直接 | builtin |

## 確認の結果(2026-11-25〜26)
秘密の値・攻撃の具体的な手順は載せていない。本番(production)への書き込みはしていない(ONLINE/ランキングの試験は development)。本物のセーブには触れていない(-memSave / -fileSave / 別製品名の試験版)。

### 更新で見つけて直したこと
1. **Run In Background が通信の後も有効のまま**(`52bbb2a0`): NGO の NetworkManager は有効になった瞬間に Application.runInBackground = true にする
   (NGO の RunInBackground の初期値)。作る時だけ NGO の設定を切り、元の値へ戻す。通信の間だけ有効にする仕組み(11-25)はそのまま。
2. **ONLINE の自動の立て直しが Windows の裏の窓で止まる**(`f57e85dd`、1. の副作用): 部屋の作り直し(Relay の失敗)/ JOIN のもう1回の間は
   Run In Background を戻さない(NetSession.HoldRunInBackground)。8人の試験で 113 秒止まって作り直しが失敗していた。APK459 と同じく立て直しは裏でも進む。

### 試験
| 項目 | 結果 |
|---|---|
| 回帰試験(Windows 開発版、保存・引き継ぎ・ログイン特典・CONTINUE・ランキング(development)・UI 縦横・戻る・ボス・停止戦・報酬・20km・fps) | 新旧(6000.6.10 / 11-04 の 6000.5.6 版)で同じ結果。更新で悪くなった項目は無し。残る不合格は旧版でも同じ = 後から入った機能(実績の MILE、ランの受付)に試験の期待値が追いついていない |
| 製品版と同じ中身の確認用ビルド(Windows、初回プレイ → 1km ボス → CONTINUE → 強制終了 → CONTINUE → 脱出 → 再起動 / 敵の出現 8 ケース) | 合格(1件の不合格は実績の MILE +80 を確認側が知らないため。所持 MILE の加算は1回だけで正しい)、例外 0 |
| 製品版に開発用の仕組みが入っていない | Windows 製品版の Assembly-CSharp に QaSweep / NetAutoTest / FileSaveStore 等が 0 件(開発版は 59 件)。ReleaseCompileCheck 合格 |
| Android 製品版のマニフェスト | enableOnBackInvokedCallback=false(予測型の戻るは無効)、debuggable 無し、権限は APK459 と同じ 10 個、構成要素の数も同じ |
| Android 画面の出入り(エミュレーター、修正後の APK) | 裏→復帰→戻る×2・回転・消灯/点灯・OS の強制終了→CONTINUE・部屋を出た後/JOIN 失敗の後に Run In Background が False へ戻る: すべて合格 |
| 通信版 15 の新旧の互換(ONLINE、development、DTLS) | 旧 Android HOST ↔ 新 Windows JOIN 3回 / 新 Windows HOST ↔ 旧 Android JOIN 1回: すべて協力ランを完走 |
| ONLINE 8人(同じ PC で 8 プロセス) | 新 2/2・旧 2/2 で 8人とも完走・同期の値が一致・例外 0。※エミュレーターを同時に動かした高負荷では新旧とも HOST の Relay が一度切れることがあり(旧版でも発生)、上の 2. の修正後は作り直しで回復 |
| ONLINE 作成/参加/退出/再参加 | 合格 |
| 不正な知らせの拒否(LOCAL) | セキュリティ監査と同じ 5 種類を拒否、合格 |
| 通信版の違い(14 → 15) | VERSION MISMATCH で拒否 |
| LOCAL 8人 | **未確認**(同じ PC では LAN の部屋の発見が3人程度までしか届かない = 試験の環境の限界、旧版でも同じ)。実機の複数台で確認が必要 |
| iOS | Xcode プロジェクトの書き出しまで成功(エラー 0、Info.plist 1.0.0 / Build 7、広告の設定 OK、終わった後に版を元へ戻した)。**IPA は手元(Windows)では作れない** → UBA で確認 |

### メモリ・性能
- Android(エミュレーター、5,000m を2回ずつ): 実行中の PSS 旧 861〜1,056MB → 新 787〜806MB。走る時間・速さは同じ。
- Android 長距離(20km、60秒ごと): 802MB → 4分で 865MB → 以後 865〜892MB で横ばい。ネイティブのヒープ 53〜57MB。
- Windows 20km: 501MB → 約 810MB で頭打ち(12分以降ほぼ一定)。fps 60。
- ONLINE 8人: 1プロセス 1.2〜1.7GB(60秒時点、新旧同程度)。

### 残る注意
- 6000.6 で `DEVELOPMENT_BUILD` の定義が非推奨の警告(UAC0009)になった。今は従来どおり効く(上の製品版の確認)。将来の版で消える可能性 → 次の更新の前に置き換えを検討。
- 未確認(実機): ジェスチャーの戻る(エミュレーターの操作では起こせない)、Android 16 実機、LOCAL 8人、iOS の IPA と実機。
- UBA: クラウドのビルド設定の Unity の版を 6000.6.10f1 に合わせる必要がある(マスターの作業)。
