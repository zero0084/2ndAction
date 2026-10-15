using UnityEngine;

// ラン中の MILE / EXP の受け取り口(2026-10-15)。決まりは RunRewards.cs の先頭。
//  計算の順番: 基本(種類の MILE × 距離/強さの係数)÷ 分配の人数 × 自分の補正(MILE 系のカード / GREED / 宝の補正)+ 端数の繰り越し
//  ・端数は1つの入れ物(mileCarry)に全部の経路から貯め、1 を超えた分だけ整数の MILE に入れる(少ない報酬が毎回の切り捨てで消えない)。
//    2026-10-15 第2弾: 以前は経路ごとに別の入れ物で、距離の分は毎回切り捨てていた → 経路ごとの端数が帰還で消え、
//    分配で 72 → 23(3人)/17(4人)になっていた(正しくは 24/18)。今は距離の分の整数化に残りの端数を足すので、
//    持ち帰り = floor(全経路の正確な合計)。帰還(と YES の受け取り)で整数にする時の 1 未満だけが切り捨てられる。
//    端数も中断データに保存する(再開で消えない・増えない)
//  ・分配の人数はランの始めに決めて保存する(途中で計算し直さない)
public partial class GameManager
{
    public int MileShareCount { get; private set; } = 1;   // 分配の人数(1 = ソロ)
    float mileCarry; // 全経路の端数(0〜1)
    public float MileCarryDebug => mileCarry; // 確認用
    // 確認用: 経路ごとの「分配前の基本」と「分配・補正の後の正確な値」の合計(0 = 距離 は都度計算、1 雑魚 / 2 ボス / 3 BONUS / 4 リング)
    public readonly float[] MileBaseDbg = new float[5], MileExactDbg = new float[5];
    public float DistanceMileBase => MileDistanceForRun / 100f * Mathf.Max(0f, 1f + Card.Get(EffectType.DistanceMilePct));
    public float DistanceMileExact => DistanceMileBase / Mathf.Max(1, MileShareCount);
    public float TakeHomeExactDebug => DistanceMileExact + MileExactDbg[1] + MileExactDbg[2] + MileExactDbg[3] + MileExactDbg[4];
    void Track(int ch, float baseAmount, float exact) { MileBaseDbg[ch] += baseAmount; MileExactDbg[ch] += exact; }

    // ランの始め(ApplyGameStart): CPU サポート = 自分 + 仲間 / 協力マルチ = 参加した人間 / それ以外 = 1
    void DecideMileShare()
    {
        int n = 1;
        if (NetRunLauncher.IsMultiplayerRun)
        {
            bool coop = NetMatch.Mode == MultiplayerGameMode.Coop;
            if (coop) n = Mathf.Max(1, NetSession.ConnectedPlayerCount);
        }
        else n = 1 + CpuParty.Allies.Count;
        MileShareCount = Mathf.Clamp(n, 1, 8);
        mileCarry = 0f; System.Array.Clear(MileBaseDbg, 0, 5); System.Array.Clear(MileExactDbg, 0, 5);
        Debug.Log($"[Reward] MILE share for this run: 1/{MileShareCount} ({(NetRunLauncher.IsMultiplayerRun ? NetMatch.Mode.ToString() : CpuParty.Allies.Count > 0 ? "CPU support" : "solo")})");
    }

    static int TakeWhole(ref float carry, float amount)
    {
        if (amount <= 0f) return 0;
        carry += amount;
        int whole = Mathf.FloorToInt(carry + 1e-4f);
        if (whole > 0) carry -= whole;
        if (carry < 0f) carry = 0f;
        return whole;
    }
    float Share(float baseAmount) => baseAmount / Mathf.Max(1, MileShareCount);

    // 雑魚: mileBase = 種類の MILE × 距離の係数(精鋭は ×3 済み)。grantExp=false は協力マルチの分け前だけ(EXP/撃破数はラストヒットの人)
    public void RegisterEnemyKill(float mileBase = 1f, float expMul = 1f, bool grantExp = true)
    {
        if (grantExp)
        {
            EnemyKillCount++;
            GainExp(enemyKillExp * KillExpScale * Mathf.Max(0f, expMul)); // v3: 精鋭は EXP×2
            UltimateArt.OnEnemyKilled(expMul > 1.5f); // #100 ULTIMATE の Gauge(発動中は溜まらない)
            TryLifesteal();
        }
        // Card Expansion/Gacha Evolution Ver.1 - Tough/Fast/Elite Enemies, Treasure Hunter, Mob Killer, Executioner, Hell Mode, etc.
        float exact = Share(Mathf.Max(0f, mileBase)) * MileGainMultiplier * FinalEvolution.MileMul; // FINAL EVOLUTION(GREED)
        Track(1, Mathf.Max(0f, mileBase), exact);
        RunEnemyMile += TakeWhole(ref mileCarry, exact);
    }

    // BONUS ZONE / WANTED の MILE(宝の補正込み)。実際に整数として入った量を返す
    public int AddRunBonusMile(int amount)
    {
        if (amount <= 0 || IsGameOver) return 0;
        // カード v3: TREASURE HUNTER は宝・報酬(BONUS ZONE の宝運びゴブリン/黄金スライム/ミミック/CLEAR/PERFECT、WANTED の賞金)を増やす
        // マルチ: BONUS ZONE は出ない。WANTED の賞金は HOST の端末だけで数える個人の報酬(今までどおり)なので割らない
        float baseAmount = NetRunLauncher.IsMultiplayerRun ? amount : Share(amount);
        float exact = baseAmount * MileGainMultiplier * FinalEvolution.MileMul * Mathf.Max(0f, 1f + Card.Get(EffectType.TreasureMilePct));
        Track(3, amount, exact);
        int add = TakeWhole(ref mileCarry, exact);
        RunBonusMile += add;
        return add;
    }

    // ボス: mileBase = 種類の MILE × 1.5 × 強さの係数(出した時に決めた値)/ expFraction = 次のレベルまでの量に対する割合
    public void RegisterBossDefeat(float mileBase, float expFraction, bool grantExp = true)
    {
        if (grantExp)
        {
            BossKillCount++;
            CpuParty.OnBossDefeated(); // CPU サポート: 仲間も1枚取る(報酬はランに1回だけ。仲間の分は増やさない)
            float exp = Mathf.Max(RunRewards.BossExpMin, ExpToNext * Mathf.Max(0f, expFraction));
            LastBossExpGranted = exp * KillExpScale * expGainMultiplier;
            GainBossExp(exp * KillExpScale);
            UltimateArt.OnBossKilled(); // #100 ULTIMATE の Gauge
            TryLifesteal();
        }
        // Card Expansion/Gacha Evolution Ver.1 - Boss Challenge/Rush, One More Mile, Pandemonium, etc.
        float exact = Share(Mathf.Max(0f, mileBase)) * BossMileGainMultiplier * FinalEvolution.MileMul;
        Track(2, Mathf.Max(0f, mileBase), exact);
        int add = TakeWhole(ref mileCarry, exact);
        RunBossMile += add;
        LastBossMileGranted = add;
        // Item 11 - a Boss kill is rare and meaningful enough to save immediately
        RequestInterruptSave(); // 2026-10-10: 戦闘中は間引いて保存(Android の保存は毎回 XML を書き直す)
    }
    // 以前の形(固定の MILE)。種類の格は分からないので大ボス扱い
    public void RegisterBossDefeat(int mileReward = 50) => RegisterBossDefeat(mileReward, RunRewards.BossExpFraction(RunRewards.BossClass.Big));
    public float LastBossExpGranted { get; private set; }
    public int LastBossMileGranted { get; private set; }

    // ボスの EXP は、カードを選んでいる最中(ソロ)でも捨てずに預かり、選び終えてから入れる(連続撃破で消えないように)
    float heldBossExp;
    void GainBossExp(float amount)
    {
        if (amount <= 0f) return;
        if (levelUpPending && !NetMatch.Active) { heldBossExp += amount; return; }
        GainExp(amount);
    }
    void FlushHeldBossExp()
    {
        if (heldBossExp <= 0f || levelUpPending || IsGameOver) return;
        float a = heldBossExp; heldBossExp = 0f;
        GainExp(a);
    }

    // 疾走のリングの代わりの MILE(全カードが Lv9 の時)
    int ShareRingMile(int amount) { float exact = Share(amount); Track(4, amount, exact); return TakeWhole(ref mileCarry, exact); }
    // 帰還/YES で整数にした後(距離の分の整数化が残りの端数を含むので、ここで空にする)
    void ConsumeMileCarry() { mileCarry = 0f; System.Array.Clear(MileBaseDbg, 0, 5); System.Array.Clear(MileExactDbg, 0, 5); }

    // 中断データ
    void FillRewardSnapshot(RunCheckpoint.Data d)
    {
        d.mileShare = MileShareCount;
        d.mileCarry = mileCarry; d.carryEnemy = d.carryBoss = d.carryBonus = d.carryRing = 0f;
        d.heldBossExp = heldBossExp;
        d.pendingLevelUps = pendingLevelUpCount; // ボスの報酬の直後は、ボス戦の間のレベルアップがまだ順番待ち
    }
    void RestoreRewardSnapshot(RunCheckpoint.Data d)
    {
        MileShareCount = Mathf.Clamp(d.mileShare > 0 ? d.mileShare : 1 + CpuParty.Allies.Count, 1, 8);
        mileCarry = Mathf.Repeat(d.mileCarry + d.carryEnemy + d.carryBoss + d.carryBonus + d.carryRing, 1f); // 以前の形(経路ごと)も1つへ
        System.Array.Clear(MileBaseDbg, 0, 5); System.Array.Clear(MileExactDbg, 0, 5);
        heldBossExp = d.heldBossExp;
        pendingLevelUpCount = Mathf.Max(0, d.pendingLevelUps);
    }
}
