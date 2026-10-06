#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

// DebugPanel: COMBO TEST(2026-10-06)
public partial class DebugPanel
{
    void DrawComboPage(Rect p)
    {
        float x = p.x + 24f, y = p.y + 64f, full = p.width - 48f;
        var lab = UiKit.Label(12f, TextAnchor.UpperLeft, false, new Color(0.85f, 0.85f, 0.9f));
        GUI.Label(new Rect(x, y, full, 30f), "シーンを読み直して DEBUG RUN(保存/記録しない)で開始。「A を付ける」「B を付ける」で COMBO が成立します", lab);
        y += 28f;
        var chars = EndgameDebug.UltCharacters;
        float cw = (full - 5f * 6f) / 6f;
        for (int i = 0; i < chars.Length; i++)
        {
            int col = i % 6, row = i / 6;
            if (UiKit.Button(new Rect(x + col * (cw + 6f), y + row * 30f, cw, 26f), EndgameDebug.UltCharLabel(chars[i]), 10f, EndgameDebug.SelectedComboChar == chars[i], false)) EndgameDebug.SelectedComboChar = chars[i];
        }
        y += 2 * 30f + 2f;
        string[] stages = { "wasteland_road", "natural_cave", "sky_corridor" };
        float sw = (full - 2f * 6f) / 3f;
        for (int i = 0; i < 3; i++) if (UiKit.Button(new Rect(x + i * (sw + 6f), y, sw, 26f), EndgameDebug.StageLabel(stages[i]), 11f, EndgameDebug.SelectedComboStage == stages[i], false)) EndgameDebug.SelectedComboStage = stages[i];
        y += 32f;
        // COMBO(8つずつのページ)
        var list = ComboTuning.I.combos;
        int pages = Mathf.Max(1, (list.Count + 7) / 8);
        EndgameDebug.ComboPage = Mathf.Clamp(EndgameDebug.ComboPage, 0, pages - 1);
        float kw = (full - 3f * 6f - 2f * 40f) / 4f;
        if (UiKit.Button(new Rect(x, y, 36f, 56f), "◀", 14f, false, false)) EndgameDebug.ComboPage = (EndgameDebug.ComboPage + pages - 1) % pages;
        if (UiKit.Button(new Rect(x + full - 36f, y, 36f, 56f), "▶", 14f, false, false)) EndgameDebug.ComboPage = (EndgameDebug.ComboPage + 1) % pages;
        for (int k = 0; k < 8; k++)
        {
            int i = EndgameDebug.ComboPage * 8 + k;
            if (i >= list.Count) break;
            var c = list[i];
            if (UiKit.Button(new Rect(x + 40f + (k % 4) * (kw + 6f), y + (k / 4) * 30f, kw, 26f), c.displayName, 9f, EndgameDebug.SelectedCombo == c.id, false)) EndgameDebug.SelectedCombo = c.id;
        }
        y += 62f;
        var sel = ComboTuning.I.For(EndgameDebug.SelectedCombo);
        string a = sel != null ? sel.abilities[0] : "?", b = sel != null && sel.abilities.Count > 1 ? sel.abilities[1] : "?";
        GUI.Label(new Rect(x, y, full, 34f), sel != null ? $"{sel.displayName}: {CardName(a)} + {CardName(b)}  — {sel.description}" : "", lab);
        y += 36f;
        // 構成カードの Lv / AWAKENED / FE / 敵 / ボス
        float bw = (full - 5f * 6f) / 6f;
        if (UiKit.Button(new Rect(x, y, bw, 28f), $"A Lv{EndgameDebug.ComboLvA}", 11f, false, false)) EndgameDebug.ComboLvA = EndgameDebug.ComboLvA % 9 + 1;
        if (UiKit.Button(new Rect(x + (bw + 6f), y, bw, 28f), $"B Lv{EndgameDebug.ComboLvB}", 11f, false, false)) EndgameDebug.ComboLvB = EndgameDebug.ComboLvB % 9 + 1;
        if (UiKit.Button(new Rect(x + 2f * (bw + 6f), y, bw, 28f), $"AWAKENED {(EndgameDebug.ComboAwakened ? "ON" : "OFF")}", 10f, EndgameDebug.ComboAwakened, false)) EndgameDebug.ComboAwakened = !EndgameDebug.ComboAwakened;
        if (UiKit.Button(new Rect(x + 3f * (bw + 6f), y, bw, 28f), $"FE {(EndgameDebug.ComboFe ? "ON" : "OFF")}", 10f, EndgameDebug.ComboFe, false)) EndgameDebug.ComboFe = !EndgameDebug.ComboFe;
        if (UiKit.Button(new Rect(x + 4f * (bw + 6f), y, bw, 28f), $"敵 {(EndgameDebug.ComboEnemies ? "あり" : "なし")}", 10f, EndgameDebug.ComboEnemies, false)) EndgameDebug.ComboEnemies = !EndgameDebug.ComboEnemies;
        if (UiKit.Button(new Rect(x + 5f * (bw + 6f), y, bw, 28f), $"ボス {(EndgameDebug.ComboBoss ? "あり" : "なし")}", 10f, EndgameDebug.ComboBoss, false)) EndgameDebug.ComboBoss = !EndgameDebug.ComboBoss;
        y += 34f;
        bool busy = EndgameDebug.Instance != null && EndgameDebug.Instance.Launching;
        if (UiKit.Button(new Rect(x, y, full, 36f), "開始(DEBUG RUN)", 15f, true, false) && !busy) EndgameDebug.LaunchCombo();
        y += 42f;
        var gm = GameManager.Instance;
        bool run = gm != null && gm.HasStarted;
        float qw = (full - 4f * 6f) / 5f;
        if (UiKit.Button(new Rect(x, y, qw, 32f), "A を付ける", 11f, false, false) && run) EndgameDebug.ComboGive(a, EndgameDebug.ComboLvA);
        if (UiKit.Button(new Rect(x + (qw + 6f), y, qw, 32f), "B を付ける", 11f, false, false) && run) EndgameDebug.ComboGive(b, EndgameDebug.ComboLvB);
        if (UiKit.Button(new Rect(x + 2f * (qw + 6f), y, qw, 32f), "敵を6体", 11f, false, false) && run) EndgameDebug.ComboSpawnEnemies(6);
        if (UiKit.Button(new Rect(x + 3f * (qw + 6f), y, qw, 32f), "FE ACTIVE(A)", 11f, false, false) && run) { FinalEvolution.DebugMakeReady(a); FinalEvolution.Activate(a); }
        if (UiKit.Button(new Rect(x + 4f * (qw + 6f), y, qw, 32f), "FE 終了(A)", 11f, false, false) && run) FinalEvolution.DebugEnd(a);
        y += 38f;
        if (run && ComboSystem.Instance != null)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var ac in ComboSystem.Instance.ActiveCombos)
                sb.Append($"{ac.def.displayName} Lv{ac.level:0.0} x{ac.factor:0.00}{(ac.enhanced ? " ENHANCED" : "")}{(ac.awakened ? " ★" : "")} 発生{ac.procs}  ");
            GUI.Label(new Rect(x, y, full, 60f), sb.Length > 0 ? sb.ToString() : $"成立中の COMBO なし({CardName(a)} Lv{gm.GetAbilityRunStack(a)} / {CardName(b)} Lv{gm.GetAbilityRunStack(b)})", lab);
        }
    }

    static string CardName(string id) { var c = CardDatabase.FindBaseById(id); return c != null ? c.cardName : id; }
}
#endif
