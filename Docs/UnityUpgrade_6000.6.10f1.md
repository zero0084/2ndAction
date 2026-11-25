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
