using UnityEditor;
using UnityEngine;

// キャラクター専用アニメーション差し替え(2026-09-13) - PlayerAnimator.
// ApplyCharacterAnimationSetの核心ロジック(「素のスナップショットを一度
// だけ取り、以後は常にそこから再計算する」)をEdit-mode単発実行で直接
//検証する。特に重要なのは③ - お嬢様騎士を適用した後に黒剣士へ戻すと、
// 黒剣士本来のSceneBuilder焼き込みアートへ正しく復元されること(蓄積的な
// 上書きになっていないこと)の確認。
public static class PlayerAnimatorCharacterSetSelfTest
{
    [MenuItem("Tools/2ndAction/Self-Test: Player Animator Character Set")]
    public static void Run()
    {
        GameObject rootGO = new GameObject("PlayerAnimatorCharacterSetSelfTest_Root");
        GameObject visualGO = new GameObject("Visual");
        visualGO.transform.SetParent(rootGO.transform, false);
        visualGO.AddComponent<SpriteRenderer>();
        PlayerAnimator animator = rootGO.AddComponent<PlayerAnimator>();

        // SceneBuilderが焼き込む黒剣士のデフォルトアートに相当するダミー。
        Sprite defaultRun = MakeDummySprite("DefaultRun");
        Sprite defaultAttack = MakeDummySprite("DefaultAttack");
        Sprite defaultAttackSmall = MakeDummySprite("DefaultAttackSmall");
        animator.runFrames = new[] { defaultRun };
        animator.attackFrames = new[] { defaultAttack };
        animator.attackFramesSmall = new[] { defaultAttackSmall };

        // 黒剣士(専用アート未設定=CharacterDefinitionの各配列が空) - 適用
        // しても何も変わらないこと(初回呼び出しでスナップショットも取る)。
        var swordsmanDef = ScriptableObject.CreateInstance<CharacterDefinition>();
        animator.ApplyCharacterAnimationSet(swordsmanDef);
        bool swordsmanUnchanged = animator.runFrames.Length == 1 && animator.runFrames[0] == defaultRun
            && animator.attackFrames.Length == 1 && animator.attackFrames[0] == defaultAttack
            && animator.attackFramesSmall != null && animator.attackFramesSmall.Length == 1;

        // お嬢様騎士(専用アート設定あり) - 実際に差し替わり、Small/Largeは
        // nullになること(1段攻撃のみのフォールバック設計)。
        Sprite nobleLadyRun = MakeDummySprite("NobleLadyRun");
        Sprite nobleLadyAttack = MakeDummySprite("NobleLadyAttack");
        var nobleLadyDef = ScriptableObject.CreateInstance<CharacterDefinition>();
        nobleLadyDef.runFrames = new[] { nobleLadyRun };
        nobleLadyDef.attackFrames = new[] { nobleLadyAttack };
        animator.ApplyCharacterAnimationSet(nobleLadyDef);
        bool nobleLadyApplied = animator.runFrames.Length == 1 && animator.runFrames[0] == nobleLadyRun
            && animator.attackFrames.Length == 1 && animator.attackFrames[0] == nobleLadyAttack
            && animator.attackFramesSmall == null;

        // 黒剣士へ戻す - 蓄積的な上書きになっていなければ、ここで元の
        // デフォルトへ正しく復元される(このテストの核心)。
        animator.ApplyCharacterAnimationSet(swordsmanDef);
        bool revertedToDefault = animator.runFrames.Length == 1 && animator.runFrames[0] == defaultRun
            && animator.attackFrames.Length == 1 && animator.attackFrames[0] == defaultAttack
            && animator.attackFramesSmall != null && animator.attackFramesSmall.Length == 1 && animator.attackFramesSmall[0] == defaultAttackSmall;

        bool pass = swordsmanUnchanged && nobleLadyApplied && revertedToDefault;

        string result = pass ? "PASS" : "FAIL";
        Debug.Log($"[PlayerAnimatorCharacterSetSelfTest] {result} - swordsmanUnchanged={swordsmanUnchanged} " +
                  $"nobleLadyApplied={nobleLadyApplied} revertedToDefault={revertedToDefault}");

        if (!pass)
        {
            Debug.LogError("[PlayerAnimatorCharacterSetSelfTest] FAIL - see individual flags above for which check failed.");
        }

        Object.DestroyImmediate(rootGO);
        Object.DestroyImmediate(swordsmanDef);
        Object.DestroyImmediate(nobleLadyDef);
    }

    static Sprite MakeDummySprite(string name)
    {
        var tex = new Texture2D(4, 4);
        Sprite s = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
        s.name = name;
        return s;
    }
}
