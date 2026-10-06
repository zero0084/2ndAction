#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

// DebugPanel: BOSS FINISH TEST(2026-10-06)。ランの中で、ボスを出して指定の攻撃で倒す(撃破の見た目の比較)
public partial class DebugPanel
{
    void DrawBossFinishPage(Rect p)
    {
        float x = p.x + 24f, y = p.y + 64f, full = p.width - 48f;
        var lab = UiKit.Label(12f, TextAnchor.UpperLeft, false, new Color(0.85f, 0.85f, 0.9f));
        var gm = GameManager.Instance;
        bool run = gm != null && gm.HasStarted && !gm.IsGameOver;
        GUI.Label(new Rect(x, y, full, 34f), run ? "ボスを選んで「出して倒す」(登場が終わったら、選んだ攻撃で HP を 0 にします。報酬も通常どおり)"
                                               : "ランを始めてから使ってください(ホームの扉 → 出発 → DEBUG)", lab);
        y += 34f;
        string[] fam = { "荒野", "洞窟", "天空" };
        float fw = (full - 2f * 6f) / 3f;
        for (int i = 0; i < 3; i++) if (UiKit.Button(new Rect(x + i * (fw + 6f), y, fw, 30f), fam[i], 13f, BossFinishDebug.Family == i, false)) { BossFinishDebug.Family = i; BossFinishDebug.Kind = 0; }
        y += 36f;
        var names = BossFinishDebug.Family == 1 ? System.Enum.GetNames(typeof(CaveBossKind)) : BossFinishDebug.Family == 2 ? System.Enum.GetNames(typeof(SkyBossKind)) : System.Enum.GetNames(typeof(WildBossKind));
        float kw = (full - 3f * 6f) / 4f;
        for (int i = 0; i < names.Length; i++)
        {
            string key = (BossFinishDebug.Family == 1 ? "Cave/" : BossFinishDebug.Family == 2 ? "Sky/" : "Wild/") + names[i];
            if (names[i] == "Dragon") key = "Dragon"; else if (names[i] == "Majin") key = "Majin";
            var e = BossDeathTuning.I.For(key);
            if (UiKit.Button(new Rect(x + (i % 4) * (kw + 6f), y + (i / 4) * 32f, kw, 28f), $"{names[i]} ({e.profile})", 10f, BossFinishDebug.Kind == i, false)) BossFinishDebug.Kind = i;
        }
        y += Mathf.CeilToInt(names.Length / 4f) * 32f + 6f;
        var atk = (BossFinalAttack[])System.Enum.GetValues(typeof(BossFinalAttack));
        float aw = (full - (atk.Length - 1) * 6f) / atk.Length;
        for (int i = 0; i < atk.Length; i++) if (UiKit.Button(new Rect(x + i * (aw + 6f), y, aw, 30f), atk[i].ToString(), 11f, BossFinishDebug.Attack == atk[i], false)) BossFinishDebug.Attack = atk[i];
        y += 36f;
        string[] fk = { "実際どおり", "再戦扱い", "初撃破扱い" };
        float hw = (full - 2f * 6f) / 3f;
        for (int i = 0; i < 3; i++) if (UiKit.Button(new Rect(x + i * (hw + 6f), y, hw, 30f), fk[i], 12f, BossFinish.DebugFirstKill == i - 1, false)) BossFinish.DebugFirstKill = i - 1;
        y += 36f;
        if (UiKit.Button(new Rect(x, y, full * 0.6f, 38f), "出して倒す", 15f, true, false) && run) { SetOpen(false); BossFinishDebug.SpawnAndKill(BossFinishDebug.Family, BossFinishDebug.Kind, BossFinishDebug.Attack); }
        var tn = BossDeathTuning.I;
        if (UiKit.Button(new Rect(x + full * 0.6f + 6f, y, full * 0.4f - 6f, 38f), $"BOSS FINISH: {(tn.enabled ? "ON" : "OFF")}", 12f, tn.enabled, false)) tn.enabled = !tn.enabled;
        y += 44f;
        GUI.Label(new Rect(x, y, full, 40f), $"{BossFinishDebug.Status}  /  演出中 {BossFinish.Running}  開始 {BossFinish.Started}  完了 {BossFinish.Completed}  片付けた攻撃 {BossFinish.LastHazardsCleared}", lab);
    }
}
#endif
