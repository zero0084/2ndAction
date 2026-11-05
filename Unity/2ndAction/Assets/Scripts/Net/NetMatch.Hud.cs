using System.Collections.Generic;
using UnityEngine;

// マルチの画面(2026-11-05): 参加者の欄 / CO-OP の長押し復活 / VERSUS の死神の予告 / 結果の順位の行。
//  ・参加者の欄: 左の距離の板の下。1行 = 色の印・名前・短い HP の帯(数字)・状態(FRONT/CARD/DOWN/OUT/ESC)。8人でも 1行 22px で収まる
//    HUD の設定: 標準(参加者の欄 ON)= 全員 / シンプル・カスタムで OFF = 自分・FRONT・DOWN の人だけ / 非表示 = 出さない(復活の欄はいつも出す)
//  ・復活の欄: 自分が DOWN(CO-OP)の間だけ画面の中央の少し下。欄そのものが長押しのボタン(キー/パッドは Hold・決定)
//  ・シングルでは何も出さない(Active = マルチの Run の時だけ)
public partial class NetMatch
{
    // ---- 参加者の欄 ----
    public struct ResultLine { public string text; public bool self; }

    // 結果の画面(GameManager.DrawResults)に出す順位の行。シングル/結果がまだ無い時は空
    static readonly List<ResultLine> resultLinesTmp = new List<ResultLine>();
    public static List<ResultLine> ResultLinesForScreen()
    {
        resultLinesTmp.Clear();
        var m = Instance;
        if (m == null || !Active || !m.runOver || m.results.Count == 0) return resultLinesTmp;
        int local = NetCombat.LocalPlayerNumber;
        foreach (var r in m.results)
        {
            string s = $"#{r.Rank}  {NetPlayer.TagOf(r.Pn)}   {r.Distance:N0} m";
            if (Mode == MultiplayerGameMode.Versus)
            {
                if (r.State == PState.Out) s += "   (DISCONNECTED)";
                else if (r.State == PState.Escaped) s += "   ESCAPED";
                else
                {
                    float total = r.RankBonus + r.ReaperBonus;
                    s += $"   MILE +{total * 100f:0.#}%" + (r.ReaperBonus > 0f ? "  REAPER" : "");
                }
            }
            else s += $"   KILL {r.Kills}   BOSS {r.BossLastHits}";
            resultLinesTmp.Add(new ResultLine { text = s, self = r.Pn == local });
        }
        return resultLinesTmp;
    }

    // 確認用(自動テストの撮影/ログ)
    public static Rect LastPartyHudRect { get; private set; }
    public static Rect LastRevivePanelRect { get; private set; }
    public static int LastPartyRowsDrawn { get; private set; }

    static readonly List<Rec> rowsTmp = new List<Rec>();
    float reaperArrivedShownAt = -99f; bool reaperArrivedSeen;

    void OnGUI()
    {
        LastPartyRowsDrawn = 0; LastPartyHudRect = Rect.zero; LastRevivePanelRect = Rect.zero;
        if (!Active || !NetRunLauncher.IsMultiplayerRun) return;
        GameManager gm = GameManager.Instance;
        if (gm == null || !gm.HasStarted || gm.IsGameOver) return;
        GUI.depth = -900;
        int padLayer = PadNav.BeginLayer(PadNav.HudLayer);
        float k = GameManager.HudK;
        int local = NetCombat.LocalPlayerNumber;
        Rec me = Get(local);
        Rec front = FrontAlive();

        // ① 参加者の欄
        bool hidden = HudPrefs.HiddenMode;
        bool all = HudPrefs.Show(HudPrefs.Item.Party);
        bool hideForChoice = GameManager.HudStacked && gm.IsRewardSequenceRunning; // 縦画面の 3択の間は左の列に重なるので出さない
        if (!hidden && !hideForChoice)
        {
            rowsTmp.Clear();
            foreach (var r in recs.Values)
            {
                if (!all && r.Pn != local && r != front && r.State != PState.Down) continue;
                rowsTmp.Add(r);
            }
            rowsTmp.Sort((a, b) => a.Pn.CompareTo(b.Pn));
            if (rowsTmp.Count > 0) DrawPartyHud(rowsTmp, local, front, k, all);
        }

        // ② 自分が DOWN(CO-OP): 長押しで復活(どの HUD の設定でも出す)
        bool downCoop = Mode == MultiplayerGameMode.Coop && me != null && me.State == PState.Down && !runOver;
        if (downCoop) DrawRevivePanel(me, k); // 長押しの数え方は Update(UpdateRules2)

        // ③ VERSUS: 脱落した自分 / 死神の予告 / 乱入
        if (Mode == MultiplayerGameMode.Versus && !runOver)
        {
            if (me != null && me.State == PState.Eliminated) DrawCenterNote("ELIMINATED", $"{me.FinalDistance:N0} m", k, 0.62f, new Color(1f, 0.55f, 0.5f));
            DrawReaperBanner(k);
        }
        PadNav.EndLayer(padLayer);
    }

    void DrawPartyHud(List<Rec> rows, int local, Rec front, float k, bool all)
    {
        Vector2 a = GameManager.PartyHudAnchorPx;
        float w = GameManager.PartyHudWidthPx;
        float rowH = 22f * k, pad = 5f * k, headH = 18f * k;
        float h = pad * 2f + headH + rows.Count * rowH;
        // 画面の下に収まらない時は行を詰める(8人 + 縦の小さい画面)
        float maxH = Screen.height - StableSafeArea.Rect.y - a.y - 120f * k;
        if (h > maxH && maxH > 0f) { rowH = Mathf.Max(15f * k, (maxH - pad * 2f - headH) / rows.Count); h = pad * 2f + headH + rows.Count * rowH; }
        Rect panel = new Rect(a.x, a.y, w, h);
        LastPartyHudRect = panel; LastPartyRowsDrawn = rows.Count;
        HudStyle.Panel(panel, 0.5f);

        // 見出し: モード・人数(・VERSUS の残り時間)
        var hs = HudStyle.LabelStyle(Mathf.RoundToInt(11f * k));
        string head = (Mode == MultiplayerGameMode.Coop ? "CO-OP " : "VERSUS ") + recs.Count + "P" + (all ? "" : "  · " + rows.Count);
        if (Mode == MultiplayerGameMode.Versus && reaperLeft >= 0f) head += $"   REAPER {Mathf.FloorToInt(reaperLeft / 60f)}:{Mathf.FloorToInt(reaperLeft % 60f):00}";
        else if (Mode == MultiplayerGameMode.Versus && WorldProgress.IsTriggered("death_100k")) head += "   REAPER!";
        HudStyle.Text(new Rect(panel.x + pad + 2f * k, panel.y + pad, w - pad * 2f, headH), head, hs, HudStyle.Label);

        float y = panel.y + pad + headH;
        float chipW = 4f * k, tagW = 46f * k, barW = Mathf.Clamp(w * 0.26f, 40f * k, 70f * k);
        float nameX = panel.x + pad + chipW + 5f * k;
        float tagX = panel.xMax - pad - tagW;
        float barX = tagX - 4f * k - barW;
        float nameW = barX - 4f * k - nameX;
        int fs = Mathf.RoundToInt(Mathf.Min(13f * k, rowH * 0.62f));
        foreach (var r in rows)
        {
            bool self = r.Pn == local;
            bool isFront = front != null && r == front;
            bool faded = r.State == PState.Eliminated || r.State == PState.Out || r.State == PState.Escaped;
            Color keep = GUI.color;
            if (faded) GUI.color = new Color(keep.r, keep.g, keep.b, keep.a * 0.45f);
            // 色の印(識別オーラと同じ色)
            Color pc = ParticipantAura.ColorOf(r.Pn);
            UiKit.Fill(new Rect(panel.x + pad, y + rowH * 0.18f, chipW, rowH * 0.64f), pc);
            if (self) UiKit.Fill(new Rect(panel.x + pad - 2f * k, y + rowH * 0.1f, 1.5f * k, rowH * 0.8f), Color.white);
            // 名前(自分は太字の白、ほかは少し薄い白)
            var ns = HudStyle.LabelStyle(fs);
            ns.fontStyle = self ? FontStyle.Bold : FontStyle.Normal;
            string name = NetPlayer.TagOf(r.Pn);
            HudStyle.Fit(ns, name, nameW, Mathf.RoundToInt(8f * k));
            HudStyle.Text(new Rect(nameX, y, nameW, rowH), name, ns, self ? Color.white : new Color(0.88f, 0.9f, 0.96f));
            // HP の帯(DOWN/脱落は帯の代わりに文字)
            Rect bar = new Rect(barX, y + rowH * 0.3f, barW, rowH * 0.4f);
            int hearts = Mathf.CeilToInt(r.Hp / (float)CombatScale.PlayerHit), maxHearts = Mathf.CeilToInt(r.MaxHp / (float)CombatScale.PlayerHit);
            var vs = HudStyle.ValueStyle(Mathf.RoundToInt(fs * 0.9f), TextAnchor.MiddleCenter);
            if (r.State == PState.Down) { } // 帯の代わりに右の赤い DOWN の札だけ(同じ文字を2回出さない)
            else if (r.State == PState.Eliminated || r.State == PState.Out) HudStyle.Text(new Rect(barX, y, barW, rowH), $"{r.FinalDistance:N0}m", vs, HudStyle.Label);
            else if (r.State == PState.Escaped) HudStyle.Text(new Rect(barX, y, barW, rowH), $"{r.FinalDistance:N0}m", vs, UiTheme.Success);
            else
            {
                float f = r.MaxHp > 0 ? r.Hp / (float)r.MaxHp : 0f;
                Color fc = f > 0.5f ? new Color(0.36f, 0.86f, 0.45f) : f > 0.25f ? new Color(1f, 0.8f, 0.3f) : new Color(1f, 0.38f, 0.36f);
                HudStyle.Bar(bar, f, fc);
                var hn = HudStyle.ValueStyle(Mathf.RoundToInt(fs * 0.78f), TextAnchor.MiddleCenter);
                HudStyle.Text(new Rect(barX, y, barW, rowH), $"{hearts}/{maxHearts}", hn, Color.white);
            }
            // 状態の札
            string tag = null; Color tc = Color.white, tb = Color.clear;
            if (r.State == PState.Down) { tag = "DOWN"; tc = Color.white; tb = new Color(0.75f, 0.15f, 0.15f, 0.9f); }
            else if (r.State == PState.Eliminated) { tag = "OUT"; tc = new Color(1f, 0.7f, 0.7f); }
            else if (r.State == PState.Out) { tag = "LEFT"; tc = HudStyle.Label; }
            else if (r.State == PState.Escaped) { tag = "ESC"; tc = UiTheme.Success; }
            else if (r.Choosing) { tag = "CARD"; tc = new Color(0.1f, 0.12f, 0.2f); tb = new Color(0.55f, 0.85f, 1f, 0.95f); }
            else if (isFront) { tag = "FRONT"; tc = new Color(0.18f, 0.12f, 0.02f); tb = UiTheme.Mile; }
            if (tag != null)
            {
                Rect tr = new Rect(tagX, y + rowH * 0.14f, tagW, rowH * 0.72f);
                if (tb.a > 0f) UiKit.Fill(tr, new Color(tb.r, tb.g, tb.b, tb.a * GUI.color.a));
                var ts = HudStyle.ValueStyle(Mathf.RoundToInt(fs * 0.74f), TextAnchor.MiddleCenter);
                HudStyle.Fit(ts, tag, tagW - 2f * k, Mathf.RoundToInt(7f * k));
                var keepC = ts.normal.textColor; ts.normal.textColor = new Color(tc.r, tc.g, tc.b, tc.a * GUI.color.a);
                LocGUI.Label(tr, tag, ts);
                ts.normal.textColor = keepC;
            }
            // CARD と FRONT が重なる人: 名前の前に小さな金の印で FRONT も分かるように
            if (isFront && r.Choosing) UiKit.Fill(new Rect(nameX - 3f * k, y + rowH * 0.15f, 2f * k, rowH * 0.7f), UiTheme.Mile);
            GUI.color = keep;
            y += rowH;
        }
    }

    // 自分が DOWN の間: 長押しの欄(欄全体がボタン)。FRONT と HP の移り方、出来ない時は理由
    void DrawRevivePanel(Rec me, float k)
    {
        float w = Mathf.Min(360f * k, StableSafeArea.Rect.width - 32f * k), h = 128f * k;
        Rect safe = StableSafeArea.Rect;
        float cx = safe.x + safe.width * 0.5f;
        float top = Screen.height - (safe.y + safe.height);
        float cy = top + safe.height * (Screen.height > Screen.width ? 0.42f : 0.68f); // 横 = 走っている線より下 / 縦 = 空の所(倒れた自分の場所より上)
        Rect r = new Rect(cx - w * 0.5f, cy - h * 0.5f, w, h);
        LastRevivePanelRect = r;
        bool ok = CanRevive(me, out Rec donor, out ReviveBlock block, out _);
        // HOST に断られた直後は、その理由を少しの間出す
        if (ok && Time.realtimeSinceStartup - lastReviveBlockAt < 2f) { ok = false; block = lastReviveBlock; }
        HudStyle.Panel(r, 0.78f);
        float pad = 10f * k;
        var title = HudStyle.ValueStyle(Mathf.RoundToInt(22f * k), TextAnchor.MiddleCenter);
        HudStyle.Text(new Rect(r.x, r.y + pad * 0.5f, w, 30f * k), ok ? "REVIVE" : "REVIVE UNAVAILABLE", title, ok ? UiTheme.Mile : new Color(1f, 0.5f, 0.45f));
        var line = HudStyle.LabelStyle(Mathf.RoundToInt(14f * k), TextAnchor.MiddleCenter);
        string l1, l2;
        if (ok && donor != null)
        {
            int gift = MultiplayerRules.I.ReviveHp, ph = CombatScale.PlayerHit;
            l1 = $"FRONT: {NetPlayer.TagOf(donor.Pn)}   HP {Mathf.CeilToInt(donor.Hp / (float)ph)} → {Mathf.CeilToInt((donor.Hp - gift) / (float)ph)}";
            l2 = LocPendingReview.Pend(LocPendingReview.ReviveHoldHint);
        }
        else
        {
            Rec f = FrontAlive();
            l1 = f != null ? $"FRONT: {NetPlayer.TagOf(f.Pn)}   HP {Mathf.CeilToInt(f.Hp / (float)CombatScale.PlayerHit)}" : "";
            l2 = block == ReviveBlock.NoAlive ? Loc.Reviewed(LocPendingReview.ReviveNoRunner, "NO PLAYER IS RUNNING")
               : block == ReviveBlock.FrontHpLow ? Loc.Reviewed(LocPendingReview.ReviveFrontHpLow, "FRONT PLAYER HP IS TOO LOW") : "";
        }
        HudStyle.Fit(line, l1, w - pad * 2f, Mathf.RoundToInt(9f * k));
        HudStyle.Text(new Rect(r.x + pad, r.y + 38f * k, w - pad * 2f, 22f * k), l1, line, Color.white);
        if (!string.IsNullOrEmpty(l2))
        {
            var l2s = HudStyle.LabelStyle(Mathf.RoundToInt(13f * k), TextAnchor.MiddleCenter);
            HudStyle.Fit(l2s, l2, w - pad * 2f, Mathf.RoundToInt(9f * k));
            HudStyle.Text(new Rect(r.x + pad, r.y + 62f * k, w - pad * 2f, 22f * k), l2, l2s, ok ? HudStyle.Label : new Color(1f, 0.75f, 0.7f));
        }
        // 長押しの進み
        Rect bar = new Rect(r.x + pad * 2f, r.yMax - pad - 16f * k, w - pad * 4f, 14f * k);
        HudStyle.Bar(bar, ok ? ReviveHoldProgress : 0f, ok ? UiTheme.Mile : new Color(0.4f, 0.4f, 0.45f));
        // 入力(欄の上を押し続ける/Hold・決定のキー)は Update で数える。ここは下の画面へ押した判定を渡さないだけ
        Event e = Event.current;
        if ((e.type == EventType.MouseDown || e.type == EventType.MouseUp) && r.Contains(e.mousePosition)) e.Use();
    }

    void DrawCenterNote(string head, string sub, float k, float yFrac, Color c)
    {
        Rect safe = StableSafeArea.Rect;
        float w = Mathf.Min(320f * k, safe.width - 32f * k), h = 64f * k;
        float top = Screen.height - (safe.y + safe.height);
        Rect r = new Rect(safe.x + (safe.width - w) * 0.5f, top + safe.height * yFrac - h * 0.5f, w, h);
        HudStyle.Panel(r, 0.6f);
        HudStyle.Text(new Rect(r.x, r.y + 4f * k, w, 30f * k), head, HudStyle.ValueStyle(Mathf.RoundToInt(20f * k), TextAnchor.MiddleCenter), c);
        HudStyle.Text(new Rect(r.x, r.y + 32f * k, w, 26f * k), sub, HudStyle.LabelStyle(Mathf.RoundToInt(15f * k), TextAnchor.MiddleCenter), Color.white);
    }

    // 死神の予告(60/30/10 秒の時に数秒だけ)と、乱入した瞬間の知らせ(数秒)
    void DrawReaperBanner(float k)
    {
        float now = Time.realtimeSinceStartup;
        bool arrived = WorldProgress.IsTriggered("death_100k");
        if (arrived && !reaperArrivedSeen) { reaperArrivedSeen = true; reaperArrivedShownAt = now; Log("REAPER ARRIVED banner"); }
        float show = MultiplayerRules.I.reaperWarnShowSeconds;
        string head = null, sub = null;
        if (arrived && now - reaperArrivedShownAt < show + 1f)
        {
            head = "REAPER!";
            sub = Loc.Reviewed(LocPendingReview.ReaperSuddenDeath, "SUDDEN DEATH");
        }
        else if (!arrived && now - warnShownAt < show && reaperLeft >= 0f)
        {
            head = "REAPER APPROACHING";
            int sec = Mathf.CeilToInt(reaperLeft);
            sub = Loc.Ready(LocPendingReview.ReaperInSeconds) ? Loc.F(LocPendingReview.ReaperInSeconds, sec) : $"{sec}s";
        }
        if (head == null) return;
        Rect safe = StableSafeArea.Rect;
        float top = Screen.height - (safe.y + safe.height);
        // 左の参加者の欄と重ならない幅(左右対称に避ける)・ボスの名前の帯(BossBattle)とカードの下
        //  縦画面(左の列が画面の半分近い)は横に避けず、参加者の欄の下に全幅で出す
        bool stacked = GameManager.HudStacked;
        float avoid = !stacked && LastPartyHudRect.width > 0f ? LastPartyHudRect.xMax - safe.x + 8f * k : 16f * k;
        float w = Mathf.Min(460f * k, safe.width - 2f * avoid), h = 70f * k;
        float y = Mathf.Max(top + safe.height * 0.24f, GameManager.RunCardsTopPx);
        if (stacked && LastPartyHudRect.height > 0f) y = Mathf.Max(y, LastPartyHudRect.yMax + 8f * k);
        if (BossBattleHud.BannerVisible) y = Mathf.Max(y, BossBattleHud.BannerBottomPx + 6f * k);
        Rect r = new Rect(safe.x + (safe.width - w) * 0.5f, y, w, h);
        float pulse = 0.75f + 0.25f * Mathf.Sin(now * 8f);
        HudStyle.Panel(r, 0.7f);
        HudStyle.Text(new Rect(r.x, r.y + 4f * k, w, 34f * k), head, HudStyle.ValueStyle(Mathf.RoundToInt(24f * k), TextAnchor.MiddleCenter), new Color(1f, 0.35f + 0.2f * pulse, 0.35f, 1f));
        var ss = HudStyle.LabelStyle(Mathf.RoundToInt(15f * k), TextAnchor.MiddleCenter);
        HudStyle.Fit(ss, sub, w - 20f * k, Mathf.RoundToInt(9f * k));
        HudStyle.Text(new Rect(r.x + 10f * k, r.y + 38f * k, w - 20f * k, 26f * k), sub, ss, Color.white);
    }
}
