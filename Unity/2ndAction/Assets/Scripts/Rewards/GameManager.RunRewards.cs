using UnityEngine;

// ラン中の MILE / EXP の受け取り口(2026-10-15)。決まりは RunRewards.cs の先頭。
//  計算の順番: 基本(種類の MILE × 距離/強さの係数)÷ 分配の人数 × 自分の補正(MILE 系のカード / GREED / 宝の補正)+ 端数の繰り越し
//  ・端数は種類(雑魚/ボス/BONUS/リング)ごとに貯め、1 を超えた分だけ整数の MILE に入れる(少ない報酬が毎回の切り捨てで消えない)。
//    端数も中断データに保存する(再開で消えない・増えない)
//  ・分配の人数はランの始めに決めて保存する(途中で計算し直さない)
public partial class GameManager
{
    public int MileShareCount { get; private set; } = 1;   // 分配の人数(1 = ソロ)
    float carryEnemy, carryBoss, carryBonus, carryRing;
    public float MileCarryDebug => carryEnemy + carryBoss + carryBonus + carryRing; // 確認用

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
        carryEnemy = carryBoss = carryBonus = carryRing = 0f;
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
        RunEnemyMile += TakeWhole(ref carryEnemy, Share(Mathf.Max(0f, mileBase)) * MileGainMultiplier * FinalEvolution.MileMul); // FINAL EVOLUTION(GREED)
    }

    // BONUS ZONE / WANTED の MILE(宝の補正込み)。実際に整数として入った量を返す
    public int AddRunBonusMile(int amount)
    {
        if (amount <= 0 || IsGameOver) return 0;
        // カード v3: TREASURE HUNTER は宝・報酬(BONUS ZONE の宝運びゴブリン/黄金スライム/ミミック/CLEAR/PERFECT、WANTED の賞金)を増やす
        // マルチ: BONUS ZONE は出ない。WANTED の賞金は HOST の端末だけで数える個人の報酬(今までどおり)なので割らない
        float baseAmount = NetRunLauncher.IsMultiplayerRun ? amount : Share(amount);
        int add = TakeWhole(ref carryBonus, baseAmount * MileGainMultiplier * FinalEvolution.MileMul * Mathf.Max(0f, 1f + Card.Get(EffectType.TreasureMilePct)));
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
        int add = TakeWhole(ref carryBoss, Share(Mathf.Max(0f, mileBase)) * BossMileGainMultiplier * FinalEvolution.MileMul);
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
    int ShareRingMile(int amount) => TakeWhole(ref carryRing, Share(amount));

    // ---- ボス戦の途中の中断(2026-10-15) ----
    // 以前: ボスを倒すたびに中断データへ報酬を書いていたが、再開はチェックポイント(前のボスの報酬を選んだ所)から
    //   なので、同じボスをもう一度倒すと報酬が二重になった(ボスラッシュの途中、撃破〜報酬の選択の間)。
    // 今: ボス戦が始まった瞬間(誰も倒していない)を1回保存し、そのボス戦の報酬が決まる(SaveCheckpoint)まで書き換えない。
    //   台本のボス戦(ラスダンの三姉妹)は、その後の YES の所の SaveCheckpoint まで。
    bool encounterSaveFrozen, lastBossPhaseSeen;
    public int InterruptSavesFrozen { get; private set; }
    public bool EncounterSaveFrozen => encounterSaveFrozen;
    void WatchEncounterSaveFreeze()
    {
        bool phase = BossManager.Instance != null && BossManager.Instance.IsBossPhase && !ArenaMode.Active;
        if (phase && !lastBossPhaseSeen && HasStarted && !IsGameOver && !NetRunLauncher.IsMultiplayerRun && !encounterSaveFrozen)
        {
            SaveInterruptState();
            encounterSaveFrozen = true;
            Debug.Log($"[Reward] boss encounter started at {MaxDistance:F0}m - interrupt data frozen until the reward is settled (bossMile={RunBossMile})");
        }
        lastBossPhaseSeen = phase;
    }
    void ResetEncounterSaveFreeze() { encounterSaveFrozen = false; lastBossPhaseSeen = false; }

    // 中断データ
    void FillRewardSnapshot(RunCheckpoint.Data d)
    {
        d.mileShare = MileShareCount;
        d.carryEnemy = carryEnemy; d.carryBoss = carryBoss; d.carryBonus = carryBonus; d.carryRing = carryRing;
        d.heldBossExp = heldBossExp;
    }
    void RestoreRewardSnapshot(RunCheckpoint.Data d)
    {
        MileShareCount = Mathf.Clamp(d.mileShare > 0 ? d.mileShare : 1 + CpuParty.Allies.Count, 1, 8);
        carryEnemy = d.carryEnemy; carryBoss = d.carryBoss; carryBonus = d.carryBonus; carryRing = d.carryRing;
        heldBossExp = d.heldBossExp;
    }
}
