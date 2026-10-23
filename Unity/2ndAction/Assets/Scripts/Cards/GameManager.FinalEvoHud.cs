using System.Collections.Generic;
using UnityEngine;

// FINAL EVOLUTION の残り時間(2026-10-24、通常マップの終盤の作り直し): 左の列(BEST/距離/速度/持ち帰り予定)のすぐ下に小さく。
//  「FINAL EVOLUTION」の金の見出し + 能力ごとに「カード名  0:59」(実際にランが進んでいる時間の残り。最大3行)。
//  残り10秒からは軽く点滅して色を強める(大きな警告はしない)。カード選択の間 / 縦画面のカード選択の間は出さない。
public partial class GameManager
{
    static readonly List<KeyValuePair<string, float>> feHudRows = new List<KeyValuePair<string, float>>();
    public const float FeHudBlinkSeconds = 10f;
    public static int FeHudDrawn; public static string FeHudSkip = ""; // 確認用

    void DrawFinalEvoHud()
    {
        if (!HasStarted || IsGameOver || ArenaMode.Active || FinalEvolution.UnifiedSeconds <= 0f) { FeHudSkip = "state"; return; }
        if (levelUpPending || (HudStacked && IsRewardSequenceRunning)) { FeHudSkip = "choice"; return; }
        FinalEvolution.ActiveRemaining(feHudRows);
        if (feHudRows.Count == 0) { FeHudSkip = "none active"; return; }
        Color keepColor = GUI.color;
        GUI.color = Color.white; // 前に描いた物の色(透明度)を持ち込まない
        if (Event.current.type == EventType.Repaint) { FeHudDrawn++; FeHudSkip = ""; }
        Rect top = V(ShowTakeHomeHud ? GetMilePanelRect() : GetSpeedPanelRect());
        int rows = Mathf.Min(3, feHudRows.Count);
        const float headH = 18f, rowH = 22f, pad = 8f;
        var r = new Rect(top.x, top.yMax + HudPanelGap, top.width, headH + rowH * rows + 6f);
        UiBackdrop.Draw(r, 0.8f);
        var head = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = HudGoldColor } };
        GUI.Label(new Rect(r.x + pad, r.y + 2f, r.width - pad * 2f, headH), "FINAL EVOLUTION", head);
        for (int i = 0; i < rows; i++)
        {
            string id = feHudRows[i].Key;
            float rem = Mathf.Max(0f, feHudRows[i].Value);
            bool late = rem <= FeHudBlinkSeconds;
            float blink = late ? 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4f)) : 1f;
            Color vc = late ? new Color(1f, 0.55f, 0.35f, blink) : new Color(1f, 0.9f, 0.6f, 1f);
            float y = r.y + headH + 2f + rowH * i;
            int sec = Mathf.CeilToInt(rem);
            string time = $"{sec / 60:00}:{sec % 60:00}";
            var vs = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, normal = { textColor = vc } };
            float tw = vs.CalcSize(new GUIContent(time)).x + 2f;
            GUI.Label(new Rect(r.x + pad, y, r.width - pad * 2f, rowH), time, vs);
            string name = CardDatabase.FindBaseById(id)?.cardName ?? id;
            var ns = FitStyle(new GUIStyle(GUI.skin.label) { fontSize = HudLabelFontSize, alignment = TextAnchor.MiddleLeft, normal = { textColor = late ? new Color(1f, 0.85f, 0.75f, blink) : HudLabelColor } },
                name, r.width - pad * 2f - tw - 6f);
            LocGUI.Label(new Rect(r.x + pad, y, r.width - pad * 2f - tw - 6f, rowH), name, ns);
        }
        GUI.color = keepColor;
    }
}
