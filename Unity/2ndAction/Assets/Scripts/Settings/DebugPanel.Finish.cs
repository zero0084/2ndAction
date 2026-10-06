#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

// DebugPanel: FINISH TEST(2026-10-06)。ランの中で、プレイヤーの前に敵を出して指定の撃破演出で倒す
public partial class DebugPanel
{
    void DrawFinishPage(Rect p)
    {
        float x = p.x + 24f, y = p.y + 64f, full = p.width - 48f;
        var lab = UiKit.Label(12f, TextAnchor.UpperLeft, false, new Color(0.85f, 0.85f, 0.9f));
        var gm = GameManager.Instance;
        bool run = gm != null && gm.HasStarted && !gm.IsGameOver;
        GUI.Label(new Rect(x, y, full, 40f), run ? "ボタンを押すとパネルを閉じて、前に敵を出して倒します(通常の撃破と同じ処理: 報酬/EXP も入ります)"
                                               : "ランを始めてから使ってください(ホームの扉 → 出発 → DEBUG)", lab);
        y += 40f;
        var t = FinishTuning.I;
        float hw = (full - 6f) / 2f;
        if (UiKit.Button(new Rect(x, y, hw, 32f), $"FINISH: {(t.enabled ? "ON" : "OFF(従来の撃破)")}", 13f, t.enabled, false)) t.enabled = !t.enabled;
        string[] ens = { "goblin", "goblin_elite", "harpy", "heavy_ogre" };
        float ew = (hw - 3f * 4f) / 4f;
        for (int i = 0; i < ens.Length; i++)
            if (UiKit.Button(new Rect(x + hw + 6f + i * (ew + 4f), y, ew, 32f), ens[i], 10f, FinishDebug.EnemyId == ens[i], false)) FinishDebug.EnemyId = ens[i];
        y += 40f;
        string[,] b = { { "normal", "通常" }, { "heavy", "HEAVY" }, { "overkill", "OVERKILL" }, { "up", "上(星になる)" }, { "slam", "叩きつけ×4" }, { "aerial", "空中" },
                        { "back", "後ろ" }, { "five", "5体同時" }, { "ten", "10体同時" }, { "flying", "空中の敵" }, { "large", "大型の敵" } };
        float bw = (full - 3f * 6f) / 4f;
        for (int i = 0; i < b.GetLength(0); i++)
        {
            var r = new Rect(x + (i % 4) * (bw + 6f), y + (i / 4) * 40f, bw, 34f);
            if (UiKit.Button(r, b[i, 1], 13f, false, false) && run) { SetOpen(false); FinishDebug.Preset(b[i, 0]); }
        }
        y += 3 * 40f + 8f;
        // 走行速度(画面基準の吹っ飛びの確認)
        GUI.Label(new Rect(x, y, full, 20f), "走行速度(開発用の倍率で上書き)", lab);
        y += 22f;
        float[] kmhs = { 0f, 100f, 200f, 300f };
        float kw = (full - 3f * 6f) / 4f;
        for (int i = 0; i < kmhs.Length; i++)
        {
            string label = kmhs[i] <= 0f ? "通常" : $"{kmhs[i]:0}km/h";
            if (UiKit.Button(new Rect(x + i * (kw + 6f), y, kw, 32f), label, 13f, false, false) && run)
            {
                var pc = PlayerController.Instance;
                PlayerController.DebugSpeedScale = 1f;
                if (kmhs[i] > 0f && pc != null) PlayerController.DebugSpeedScale = Mathf.Max(0.2f, kmhs[i] / GameManager.KmhPerMps / Mathf.Max(0.1f, pc.CurrentAutoRunSpeed));
            }
        }
        y += 40f;
        GUI.Label(new Rect(x, y, full, 40f), $"撃破 {EnemyController.FinishDeaths} / 弾けた {FinishFx.Bursts} / 連続 {FinishFx.StreakCount} / 使用中の絵 {(FinishFx.Instance != null ? FinishFx.Instance.ActiveSprites : 0)}/{(FinishFx.Instance != null ? FinishFx.Instance.PoolSize : 0)} / 足りなかった {FinishFx.PoolExhausted}", lab);
    }
}
#endif
