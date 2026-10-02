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
        // お嬢様騎士Run読みやすさ改善(2026-09-13) - runFpsも同じスナップ
        // ショット/上書きパターンで検証する。
        animator.runFps = 10f;
        // お嬢様騎士 二段ジャンプ演出バグ修正(2026-09-13) - doubleJumpFrames
        // も同じスナップショット/上書きパターンで検証する。
        Sprite defaultDoubleJump = MakeDummySprite("DefaultDoubleJump");
        animator.doubleJumpFrames = new[] { defaultDoubleJump };
        // 3人目の主人公追加(2026-09-13、双剣士) - attackFramesSmall/Large
        // も同じスナップショット/上書きパターンで検証する(黒剣士本来の
        // Small/Largeが、専用Small/Largeを持たないキャラへ紛れ込まない
        // ことの確認が目的)。
        // 双剣士専用アニメ追加(2026-09-13深夜) - downAttackFrames/
        // downAttackLandFramesも同じスナップショット/上書きパターンで
        // 検証する。
        Sprite defaultDownAttack = MakeDummySprite("DefaultDownAttack");
        Sprite defaultDownAttackLand = MakeDummySprite("DefaultDownAttackLand");
        animator.downAttackFrames = new[] { defaultDownAttack };
        animator.downAttackLandFrames = new[] { defaultDownAttackLand };

        // 黒剣士(専用アート未設定=CharacterDefinitionの各配列が空) - 適用
        // しても何も変わらないこと(初回呼び出しでスナップショットも取る)。
        var swordsmanDef = ScriptableObject.CreateInstance<CharacterDefinition>();
        animator.ApplyCharacterAnimationSet(swordsmanDef);
        bool swordsmanUnchanged = animator.runFrames.Length == 1 && animator.runFrames[0] == defaultRun
            && animator.attackFrames.Length == 1 && animator.attackFrames[0] == defaultAttack
            && animator.attackFramesSmall != null && animator.attackFramesSmall.Length == 1
            && animator.runFps == 10f
            && animator.doubleJumpFrames.Length == 1 && animator.doubleJumpFrames[0] == defaultDoubleJump
            && animator.downAttackFrames.Length == 1 && animator.downAttackFrames[0] == defaultDownAttack
            && animator.downAttackLandFrames.Length == 1 && animator.downAttackLandFrames[0] == defaultDownAttackLand;

        // お嬢様騎士(専用アート設定あり) - 実際に差し替わり、Small/Largeは
        // nullになること(1段攻撃のみのフォールバック設計)。
        Sprite nobleLadyRun = MakeDummySprite("NobleLadyRun");
        Sprite nobleLadyAttack = MakeDummySprite("NobleLadyAttack");
        var nobleLadyDef = ScriptableObject.CreateInstance<CharacterDefinition>();
        nobleLadyDef.runFrames = new[] { nobleLadyRun };
        nobleLadyDef.attackFrames = new[] { nobleLadyAttack };
        nobleLadyDef.runFps = 6f;
        Sprite nobleLadyDoubleJump = MakeDummySprite("NobleLadyDoubleJump");
        nobleLadyDef.doubleJumpFrames = new[] { nobleLadyDoubleJump };
        // nobleLadyDefはdownAttackFrames/downAttackLandFramesを設定しない
        // (canUseDownAttack=falseのキャラ)- 黒剣士のデフォルトへフォール
        // バックすることを検証する。
        animator.ApplyCharacterAnimationSet(nobleLadyDef);
        bool nobleLadyApplied = animator.runFrames.Length == 1 && animator.runFrames[0] == nobleLadyRun
            && animator.attackFrames.Length == 1 && animator.attackFrames[0] == nobleLadyAttack
            && animator.attackFramesSmall == null
            && animator.runFps == 6f
            && animator.doubleJumpFrames.Length == 1 && animator.doubleJumpFrames[0] == nobleLadyDoubleJump
            && animator.downAttackFrames.Length == 1 && animator.downAttackFrames[0] == defaultDownAttack
            && animator.downAttackLandFrames.Length == 1 && animator.downAttackLandFrames[0] == defaultDownAttackLand;

        // 双剣士(専用のSmall/Mid/Large 3段階アートを持つキャラ) - 黒剣士の
        // Small/Largeが紛れ込まず、このキャラ自身の3段階アートに正しく
        // 差し替わること。
        Sprite dualBladeRun = MakeDummySprite("DualBladeRun");
        Sprite dualBladeAttackMid = MakeDummySprite("DualBladeAttackMid");
        Sprite dualBladeAttackSmall = MakeDummySprite("DualBladeAttackSmall");
        Sprite dualBladeAttackLarge = MakeDummySprite("DualBladeAttackLarge");
        Sprite dualBladeDownAttack = MakeDummySprite("DualBladeDownAttack");
        Sprite dualBladeDownAttackLand = MakeDummySprite("DualBladeDownAttackLand");
        var dualBladeDef = ScriptableObject.CreateInstance<CharacterDefinition>();
        dualBladeDef.runFrames = new[] { dualBladeRun };
        dualBladeDef.attackFrames = new[] { dualBladeAttackMid };
        dualBladeDef.attackFramesSmall = new[] { dualBladeAttackSmall };
        dualBladeDef.attackFramesLarge = new[] { dualBladeAttackLarge };
        dualBladeDef.downAttackFrames = new[] { dualBladeDownAttack };
        dualBladeDef.downAttackLandFrames = new[] { dualBladeDownAttackLand };
        animator.ApplyCharacterAnimationSet(dualBladeDef);
        bool dualBladeApplied = animator.runFrames.Length == 1 && animator.runFrames[0] == dualBladeRun
            && animator.attackFrames.Length == 1 && animator.attackFrames[0] == dualBladeAttackMid
            && animator.attackFramesSmall != null && animator.attackFramesSmall.Length == 1 && animator.attackFramesSmall[0] == dualBladeAttackSmall
            && animator.attackFramesLarge != null && animator.attackFramesLarge.Length == 1 && animator.attackFramesLarge[0] == dualBladeAttackLarge
            && animator.downAttackFrames.Length == 1 && animator.downAttackFrames[0] == dualBladeDownAttack
            && animator.downAttackLandFrames.Length == 1 && animator.downAttackLandFrames[0] == dualBladeDownAttackLand;

        // 黒剣士へ戻す - 蓄積的な上書きになっていなければ、ここで元の
        // デフォルトへ正しく復元される(このテストの核心)。
        animator.ApplyCharacterAnimationSet(swordsmanDef);
        bool revertedToDefault = animator.runFrames.Length == 1 && animator.runFrames[0] == defaultRun
            && animator.attackFrames.Length == 1 && animator.attackFrames[0] == defaultAttack
            && animator.attackFramesSmall != null && animator.attackFramesSmall.Length == 1 && animator.attackFramesSmall[0] == defaultAttackSmall
            && animator.runFps == 10f
            && animator.doubleJumpFrames.Length == 1 && animator.doubleJumpFrames[0] == defaultDoubleJump
            && animator.downAttackFrames.Length == 1 && animator.downAttackFrames[0] == defaultDownAttack
            && animator.downAttackLandFrames.Length == 1 && animator.downAttackLandFrames[0] == defaultDownAttackLand;

        bool pass = swordsmanUnchanged && nobleLadyApplied && dualBladeApplied && revertedToDefault;

        string result = pass ? "PASS" : "FAIL";
        Debug.Log($"[PlayerAnimatorCharacterSetSelfTest] {result} - swordsmanUnchanged={swordsmanUnchanged} " +
                  $"nobleLadyApplied={nobleLadyApplied} dualBladeApplied={dualBladeApplied} revertedToDefault={revertedToDefault}");

        if (!pass)
        {
            Debug.LogError("[PlayerAnimatorCharacterSetSelfTest] FAIL - see individual flags above for which check failed.");
        }

        Object.DestroyImmediate(rootGO);
        Object.DestroyImmediate(swordsmanDef);
        Object.DestroyImmediate(nobleLadyDef);
        Object.DestroyImmediate(dualBladeDef);
    }

    static Sprite MakeDummySprite(string name)
    {
        var tex = new Texture2D(4, 4);
        Sprite s = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
        s.name = name;
        return s;
    }
}
