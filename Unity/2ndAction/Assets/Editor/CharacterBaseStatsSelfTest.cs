using System.Reflection;
using UnityEditor;
using UnityEngine;

// プレイアブル主人公追加(2026-09-12、お嬢様騎士) - 「黒剣士の値をベタ書き
// しているだけの構造にはしないでください」/「既存の黒剣士の戦闘性能を
// 変更しない」という2つの要件を、実際にPlayerController.ApplyCharacterBaseStats
// (CharacterDefinition)を1回呼び出した直後の状態で直接検証する。
//
// このセッションにはPlay Modeでの検証手段がないため、他の自己診断テスト
// 同様「1回の呼び出し直後の状態」だけを対象にする(複数フレームにわたる
// 実際のジャンプ軌道・ステージの隙間が本当に飛び越えられるかどうかは、
// 実機でのマスター自身の確認が必要 - report参照)。
//
// GameManager.ApplyCharacterBaseStats(maxLives/Livesの設定+PlayerController
// への委譲)自体は、GameManagerが多数のSceneBuilder依存コンポーネントを
// 前提とするためEdit-mode単体では検証していない(既知の限界、コード
// レビューでStartGame両分岐/BeginContinuedRunへの配線を確認済み)。
public static class CharacterBaseStatsSelfTest
{
    [MenuItem("Tools/2ndAction/Self-Test: Character Base Stats")]
    public static void Run()
    {
        // 既存アセットを上書きしないビルダーのため、まだ無ければ生成する
        // (このテストが単独で走ってもCharacterDatabase.AllCharactersが
        // 空にならないようにするため)。
        CharacterDatabaseBuilder.Build();
        CharacterDatabase.Reset();

        CharacterDefinition swordsman = CharacterDatabase.FindById("swordsman");
        CharacterDefinition nobleLady = CharacterDatabase.FindById("noble_lady");

        if (swordsman == null || nobleLady == null)
        {
            Debug.LogError("[CharacterBaseStatsSelfTest] FAIL - swordsman/noble_lady CharacterDefinition not found.");
            return;
        }

        // 黒剣士の実プレイ用フィールドは全て「無変更」を意味する既定値
        // (倍率1.0、既存のAttackPower=2/maxComboChain=3/maxJumps=2)である
        // こと自体を先に検証する - CharacterDatabaseBuilder.DefaultBaseline
        // が将来誤って変更された場合に検知できる。
        bool swordsmanSpecOk = swordsman.attackPower == 20 && swordsman.attackComboCount == 3
            && Mathf.Approximately(swordsman.attackSpeedMultiplier, 1f)
            && Mathf.Approximately(swordsman.attackRangeMultiplier, 1f)
            && Mathf.Approximately(swordsman.knockbackPowerMultiplier, 1f)
            && swordsman.jumpCount == 2
            && Mathf.Approximately(swordsman.jumpForceMultiplier, 1f)
            && Mathf.Approximately(swordsman.groundMobilityMultiplier, 1f)
            && Mathf.Approximately(swordsman.airControlMultiplier, 1f)
            && swordsman.canUseUpAttack && swordsman.canUseAirAttack && swordsman.canUseDownAttack
            && !swordsman.challengeFlag;

        // お嬢様騎士 - 「CHALLENGE HERO/特別枠/専用バッジは不要」という
        // 今回の新しい明示指示(前回パスのchallengeFlag=trueを覆す)。
        bool nobleLadyBadgeOk = !nobleLady.challengeFlag;
        bool nobleLadyStatsOk = nobleLady.baseLives == 30 && nobleLady.baseMaxLives == 30
            && nobleLady.attackPower < swordsman.attackPower
            && nobleLady.attackComboCount == 1
            && nobleLady.jumpCount == 1
            && nobleLady.jumpForceMultiplier < 1f && nobleLady.jumpForceMultiplier > 0f
            && !nobleLady.canUseUpAttack && !nobleLady.canUseAirAttack && !nobleLady.canUseDownAttack;

        // PlayerController側 - RequireComponentのためRigidbody2D/
        // BoxCollider2Dも一緒に付与してから、Awake()経由でbaseRunSpeed等の
        // 「素の値」キャプチャを再現する(EnemyGroundKnockbackVelocitySelfTest
        // と同じReflection Invoke方式)。
        GameObject playerGO = new GameObject("CharacterBaseStatsSelfTest_Player",
            typeof(Rigidbody2D), typeof(BoxCollider2D), typeof(PlayerController));
        PlayerController pc = playerGO.GetComponent<PlayerController>();
        InvokePrivate(pc, "Awake");

        float defaultRunSpeed = pc.runSpeed;
        float defaultJumpForce = pc.jumpForce;
        float defaultGravity = pc.gravity;
        int defaultMaxJumps = pc.maxJumps;
        int defaultMaxComboChain = pc.maxComboChain;
        int defaultAttackPower = pc.AttackPower;

        // 1) 黒剣士のSpecを適用しても、既存デフォルトから一切値が変わら
        // ないこと(既存の黒剣士の戦闘性能を変更しない、という変更禁止
        // 事項の直接検証)。
        pc.ApplyCharacterBaseStats(swordsman);
        bool swordsmanUnchanged =
            Mathf.Approximately(pc.runSpeed, defaultRunSpeed)
            && Mathf.Approximately(pc.jumpForce, defaultJumpForce)
            && Mathf.Approximately(pc.gravity, defaultGravity)
            && pc.maxJumps == defaultMaxJumps
            && pc.maxComboChain == defaultMaxComboChain
            && pc.AttackPower == defaultAttackPower
            && Mathf.Approximately(pc.AttackRangeMultiplier, 1f)
            && Mathf.Approximately(pc.AttackSpeedMultiplier, 1f)
            && Mathf.Approximately(pc.KnockbackPowerMultiplier, 1f)
            && pc.canUseUpAttack && pc.canUseAirAttack && pc.canUseDownAttack;

        // 2) 同じインスタンスへお嬢様騎士のSpecを適用すると、今度は明確に
        // 弱い値へ切り替わること(「入力遅延ではなく性能値」の直接証拠 -
        // ここで変わるのは全て数値フィールドであり、フリック検出や入力
        // 処理そのものには一切触れていない)。
        pc.ApplyCharacterBaseStats(nobleLady);
        bool nobleLadyApplied =
            pc.maxJumps == 1
            && pc.maxComboChain == 1
            && pc.AttackPower < defaultAttackPower
            && pc.jumpForce < defaultJumpForce && pc.jumpForce > 0f
            && pc.runSpeed > 0f // 「完全に詰む」の最低限の安全確認(0や負値になっていない)
            && Mathf.Approximately(pc.KnockbackPowerMultiplier, 0.5f)
            && !pc.canUseUpAttack && !pc.canUseAirAttack && !pc.canUseDownAttack;

        // 3) RunCheckpoint.Data.characterIdがJsonUtility経由で正しく往復
        // すること(Acceptance Test項目10「再起動をまたいだ永続化」の
        // 直接的な裏付け)。
        var saved = new RunCheckpoint.Data { active = true, characterId = "noble_lady", maxLives = 3, lives = 2 };
        string json = JsonUtility.ToJson(saved);
        var restored = JsonUtility.FromJson<RunCheckpoint.Data>(json);
        bool checkpointRoundTripOk = restored != null && restored.characterId == "noble_lady" && restored.maxLives == 3 && restored.lives == 2;

        bool pass = swordsmanSpecOk && nobleLadyBadgeOk && nobleLadyStatsOk && swordsmanUnchanged && nobleLadyApplied && checkpointRoundTripOk;

        string result = pass ? "PASS" : "FAIL";
        Debug.Log($"[CharacterBaseStatsSelfTest] {result} - swordsmanSpecOk={swordsmanSpecOk} " +
                  $"nobleLadyBadgeOk={nobleLadyBadgeOk} nobleLadyStatsOk={nobleLadyStatsOk} " +
                  $"swordsmanUnchanged={swordsmanUnchanged} nobleLadyApplied={nobleLadyApplied} " +
                  $"checkpointRoundTripOk={checkpointRoundTripOk} " +
                  $"(nobleLady jumpForce={pc.jumpForce:F2}/default={defaultJumpForce:F2}, runSpeed={pc.runSpeed:F2}/default={defaultRunSpeed:F2})");

        if (!pass)
        {
            Debug.LogError("[CharacterBaseStatsSelfTest] FAIL - see individual flags above for which check failed.");
        }

        Object.DestroyImmediate(playerGO);
    }

    static void InvokePrivate(object target, string methodName, params object[] args)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (method == null)
        {
            Debug.LogError($"[CharacterBaseStatsSelfTest] Method not found: {target.GetType().Name}.{methodName}");
            return;
        }
        method.Invoke(target, args);
    }
}
