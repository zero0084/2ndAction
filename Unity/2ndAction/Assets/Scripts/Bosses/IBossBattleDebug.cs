// 天空ボス強化(2026-10-05): 開発用の強制操作(段階/必殺技/BREAK)と状態の表示を、WildBossBase 系と
// 専用コントローラーのボス(ドラゴン/魔人)で同じ形で扱うための入口。DEBUG の「ボス試験」と自動テストが使う。
public interface IBossBattleDebug
{
    string DebugName { get; }
    bool DebugAlive { get; }
    int Phase { get; }
    int PhaseCount { get; }
    bool Broken { get; }
    int BreakCount { get; }
    int UltimatesUsed { get; }
    bool UltimateRunning { get; }
    float StaggerFraction { get; }
    void DebugSetPhase(int p);
    bool DebugForceUltimate();
    void DebugForceBreak();
}
