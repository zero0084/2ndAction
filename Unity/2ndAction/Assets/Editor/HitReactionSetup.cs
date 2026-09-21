using UnityEditor;
using UnityEngine;

// 被弾リアクション(2026-09-22) - 各キャラクターの見せ方(のけぞり角度/よろけ/ノックバック倍率)を設定する。
// 専用のHurt/Recovery絵が用意できたら、各CharacterDefinitionのhurtFrames/recoveryFramesへ設定するだけで差し替わる。
public static class HitReactionSetup
{
    [MenuItem("Tools/OneMoreMile/Apply Hit Reaction Settings")]
    public static void Apply()
    {
        int n = 0;
        foreach (var def in Resources.LoadAll<CharacterDefinition>(""))
        {
            switch (def.characterId)
            {
                case "swordsman": // 黒剣士: 重い鎧で踏ん張りながら少しのけぞる
                    def.hurtLeanDegrees = 9f; def.hurtStaggerDistance = 0.07f; def.recoveryCrouchDepth = 0.16f; def.hurtKnockbackMultiplier = 0.85f; break;
                case "noble_lady": // お嬢様騎士: やや大きく体勢を崩してよろける
                    def.hurtLeanDegrees = 20f; def.hurtStaggerDistance = 0.2f; def.recoveryCrouchDepth = 0.12f; def.hurtKnockbackMultiplier = 1.2f; break;
                case "dual_blade": // 双剣士: 軽く後方へ流されるようによろける
                    def.hurtLeanDegrees = 13f; def.hurtStaggerDistance = 0.26f; def.recoveryCrouchDepth = 0.14f; def.hurtKnockbackMultiplier = 1.1f; break;
                default: continue;
            }
            EditorUtility.SetDirty(def); n++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[HitReactionSetup] updated {n} characters");
    }
}
