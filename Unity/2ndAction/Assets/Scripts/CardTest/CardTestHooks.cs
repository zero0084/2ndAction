#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

// CARD BALANCE TEST(2026-10-01)用の受け口。開発ビルド/Editorだけでコンパイルされる(リリースビルドには存在しない)。
// CardBalanceTest から「カードが変える値」を読み書きするためだけのもので、ゲームの処理からは呼ばれない。

public partial class PlayerController
{
    public float CardTestBaseRunSpeed => baseRunSpeed;     // キャラ倍率を掛ける前の走る速さ(m/s)
    public float CardTestBaseJumpForce => baseJumpForce;
    public float CardTestGravity => gravity;
    public float CardTestNaturalMultiplier => GetSpeedMultiplier(); // 自然加速の倍率(距離で決まる)
    public void CardTestSetAttackPower(int v) => AttackPower = v;
    public void CardTestSetAttackRange(float v) => AttackRangeMultiplier = Mathf.Max(0.1f, v);
    public void CardTestSetAttackSpeed(float v) => AttackSpeedMultiplier = Mathf.Max(0.1f, v);
    public void CardTestSetShield(int v) => ShieldCharges = Mathf.Max(0, v);

    // カードだけが増やす攻撃系の補正を0へ(キャラの基本値は持たない値)
    public void CardTestClearCardBonuses()
    {
        AirAttackPowerBonus = 0; GroundAttackPowerBonus = 0; ComboFinalStageBonus = 0; FirstHitBonus = 0;
        LowHpAttackBonus = 0; FullHpAttackBonus = 0; MomentumBonus = 0; BossDamageBonus = 0;
        ShieldCharges = 0;
    }
}

public partial class GameManager
{
    public float CardTestExpMultiplier { get => expGainMultiplier; set => expGainMultiplier = value; }
    public void CardTestSetMileMultipliers(float mile, float bossMile) { MileGainMultiplier = mile; BossMileGainMultiplier = bossMile; }

    // 最大HPを変えて全回復(カードのMaxHpと同じ扱い)
    public void CardTestSetMaxLives(int max)
    {
        maxLives = Mathf.Clamp(max, 1, maxLivesCap);
        Lives = maxLives;
        NetMatch.RequestSetMax(maxLives, true);
    }

    // カードが変える値(プレイヤー以外)を初期値へ
    public void CardTestClearCardState()
    {
        expGainMultiplier = 1f;
        lifestealChance = 0f;
        lifestealAmount = 0f;
        EnemySpawnRateMultiplier = 1f;
        EnemyHpMultiplier = 1f;
        BossHpMultiplier = 1f;
        MileGainMultiplier = 1f;
        BossMileGainMultiplier = 1f;
    }

    // ラン中にキャラクターを差し替える(テスト専用)。Run開始時と同じキャラの基本性能・見た目を適用し、
    // カードの効果は外れる。選択キャラ(次のNEW RUNのキャラ/保存値)は変えない。
    public bool CardTestSwitchCharacter(string characterId)
    {
        var def = CharacterDatabase.FindById(characterId);
        if (def == null || !HasStarted || IsGameOver) return false;
        activeRunCharacterId = characterId;
        CardTestClearCardState();
        if (PlayerController.Instance != null) PlayerController.Instance.CardTestClearCardBonuses();
        ApplyCharacterBaseStats(def);
        return true;
    }
}
#endif
