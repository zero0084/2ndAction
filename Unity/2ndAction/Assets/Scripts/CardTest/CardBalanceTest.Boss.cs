#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

// CARD BALANCE TEST の「ボス試験」タブ(2026-10-02)。
//  ・今のPlayerの「ボスへの1発」(見積り)から、15/20/25発で倒れるHPのテストボスを出す(実際に殴って長さを決めるため)
//  ・テストボスの残りHP/段階/当てた回数/経過時間/1発の平均を表示し、倒した時の発数と時間を残す
//  ・ボスHPの再設計案(現行/15/20/25発、BossHpPlan)を切り替える(開発版だけ。次に出るボスから)
public partial class CardBalanceTest
{
    const int BossTabRows = 9;
    static readonly WildBossKind[] TestKinds = { WildBossKind.Golem, WildBossKind.Cyclops, WildBossKind.BlackKnight, WildBossKind.Hydra, WildBossKind.Wolf };
    int testKindIndex;
    bool useComboAverage;
    WildBossBase testBoss;
    int testHits, testTargetHits, testStartHp, testLastHp, testDamageSum;
    float testStartTime;
    string testResult = "";

    // 自動テスト用
    public int TestBossHits => testHits;
    public WildBossBase TestBoss => testBoss;
    public static int BossHpFor(int hitDamage, int hits) => Mathf.Max(1, hitDamage * hits);

    int ReferenceDamage(PlayerController pc) => useComboAverage ? pc.BossHitComboAverage : pc.BossHitEstimate;

    public bool SpawnTestBoss(int hits)
    {
        var pc = PlayerController.Instance; var bm = BossManager.Instance;
        if (pc == null || bm == null) return false;
        if (bm.IsBossPhase && (testBoss == null || testBoss.IsDead)) { lastAction = "ボス戦中は出せません(倒してから)"; return false; }
        if (testBoss != null && !testBoss.IsDead) Destroy(testBoss.gameObject);
        int dmg = ReferenceDamage(pc);
        int hp = BossHpFor(dmg, hits);
        BossManager.NetTestBossHpOverride = hp;
        var before = new System.Collections.Generic.HashSet<WildBossBase>(FindObjectsByType<WildBossBase>(FindObjectsSortMode.None));
        try { bm.NetTestSpawnWild(TestKinds[testKindIndex], 1); }
        finally { BossManager.NetTestBossHpOverride = 0; }
        testBoss = null;
        foreach (var w in FindObjectsByType<WildBossBase>(FindObjectsSortMode.None)) if (!before.Contains(w)) testBoss = w;
        if (testBoss == null) { lastAction = "テストボスを出せませんでした"; return false; }
        // ApplyTuningのhpScaleが1以外でも、狙ったHPちょうどにする
        testBoss.maxHp = hp;
        testBoss.DebugSetHp(hp);
        testHits = 0; testTargetHits = hits; testStartHp = testLastHp = hp; testDamageSum = 0;
        testStartTime = Time.time; testResult = "";
        lastAction = $"テストボス {TestKinds[testKindIndex]} HP{hp:N0}(1発{dmg:N0} × {hits}発)";
        return true;
    }

    void TrackTestBoss()
    {
        if (testBoss == null) return;
        int hp = testBoss.Hp;
        if (hp < testLastHp) { testHits++; testDamageSum += testLastHp - hp; testLastHp = hp; }
        if (testBoss.IsDead || hp <= 0)
        {
            if (string.IsNullOrEmpty(testResult))
                testResult = $"撃破: {testHits}発 / {Time.time - testStartTime:F1}秒(目安{testTargetHits}発、1発平均{(testHits > 0 ? testDamageSum / testHits : 0):N0})";
        }
    }


    void DrawBossTest(float x, ref float y, float rowH, float gap)
    {
        var pc = PlayerController.Instance;
        if (pc == null) return;
        int one = pc.BossHitEstimate, avg = pc.BossHitComboAverage;
        GUI.Label(new Rect(x, y, W - 16f, rowH), $"ボスへの1発(見積り): 基本 {one:N0} / 連撃平均 {avg:N0}  (今の攻撃力{pc.AttackPower:N0}・HP条件・速度・ボス特効込み、技の倍率は1)", sLabel);
        y += rowH + gap;
        GUI.Label(new Rect(x, y, 92f, rowH), "基準", sLabel);
        if (B(new Rect(x + 92f, y, 120f, rowH), "基本の1発", !useComboAverage)) useComboAverage = false;
        if (B(new Rect(x + 216f, y, 120f, rowH), "連撃の平均", useComboAverage)) useComboAverage = true;
        GUI.Label(new Rect(x + 344f, y, 70f, rowH), "ボス", sLabel);
        if (B(new Rect(x + 392f, y, 32f, rowH), "◀")) testKindIndex = (testKindIndex + TestKinds.Length - 1) % TestKinds.Length;
        GUI.Label(new Rect(x + 428f, y, 110f, rowH), TestKinds[testKindIndex].ToString(), sTitle);
        if (B(new Rect(x + 540f, y, 32f, rowH), "▶")) testKindIndex = (testKindIndex + 1) % TestKinds.Length;
        y += rowH + gap;
        int dmg = ReferenceDamage(pc);
        GUI.Label(new Rect(x, y, 92f, rowH), "テストボス", sLabel);
        float bx = x + 92f;
        foreach (int h in new[] { 15, 20, 25 })
        {
            if (B(new Rect(bx, y, 170f, rowH), $"{h}発 = HP{BossHpFor(dmg, h):N0}")) SpawnTestBoss(h);
            bx += 174f;
        }
        y += rowH + gap;
        if (testBoss != null)
        {
            int ph = testBoss.Phase;
            GUI.Label(new Rect(x, y, W - 16f, rowH), testBoss.IsDead ? "テストボス: 撃破済み"
                : $"テストボス: HP {testBoss.Hp:N0} / {testBoss.maxHp:N0}({100f * testBoss.Hp / Mathf.Max(1, testBoss.maxHp):0}%) 段階{ph}  当てた{testHits}発  {Time.time - testStartTime:F1}秒  1発平均{(testHits > 0 ? testDamageSum / testHits : 0):N0}", sLabel);
        }
        else GUI.Label(new Rect(x, y, W - 16f, rowH), "テストボス: なし(上のボタンで出す。荒野街道ならHPの段階で行動が変わる)", sSmall);
        y += rowH + gap;
        GUI.Label(new Rect(x, y, W - 16f, rowH), string.IsNullOrEmpty(testResult) ? "" : testResult, sTitle);
        y += rowH + gap;
        // ボスHPの再設計案
        GUI.Label(new Rect(x, y, 92f, rowH), "通常のボスHP", sLabel);
        bx = x + 92f;
        foreach (int h in BossHpPlan.Choices)
        {
            if (B(new Rect(bx, y, 80f, rowH), BossHpPlan.Label(h), BossHpPlan.Hits == h)) { BossHpPlan.Hits = h; lastAction = $"通常のボスHP: {BossHpPlan.Label(h)}(次に出るボスから)"; }
            bx += 84f;
        }
        float d = BossHpPlan.CurrentDistance;
        GUI.Label(new Rect(bx + 6f, y, W - (bx - x) - 22f, rowH), BossHpPlan.Active
            ? $"今の距離の倍率 ×{BossHpPlan.Multiplier(d):0.0}(10km節目ボスの目安HP {BossHpPlan.TargetMilestoneHp(d, BossHpPlan.Hits):N0})"
            : "現行 = ボスごとの値(10倍スケール)", sSmall);
        y += rowH + gap;
        GUI.Label(new Rect(x, y, W - 16f, rowH), "再設計案は開発版だけ。距離ごとの想定1発(最大級ビルドの成長曲線)×発数で10km節目のボスを決め、他のボスも同じ倍率(個性の差はそのまま)", sSmall);
        y += rowH + gap;
    }
}
#endif
