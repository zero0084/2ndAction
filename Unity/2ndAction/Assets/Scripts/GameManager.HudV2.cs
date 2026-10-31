using UnityEngine;

// ラン中の HUD の刷新(2026-10-31、UI 全体の改善)。独立した四角い板が多く雑然としていた → 役割ごとに3か所へまとめる:
//  ・左上: 走りの情報の板1枚(BEST の小さな行 / 距離(大) / 速度 / 持ち帰り予定 MILE)
//  ・中央上: Lv と EXP の細い帯(ボス戦ではその下にボスの欄 BossHudPanel)
//  ・右上: HP と、その下にカード(RunBuildHud)
//  ・右下: 一時停止(大きさの設定に関係なく押せる大きさのまま)
// 見た目は HudStyle(半透明の濃紺 + 細い金の線)。表示する項目は HudPrefs(標準/シンプル/非表示/カスタム)。
// 位置はすべて設計の単位(短辺 720)で決め、HudK 倍(HUD の大きさの設定込み)で描く。改修前の HUD は -hudLegacy 1(開発版の比較用)。
public partial class GameManager
{
    const float RunRowBestH = 20f, RunRowDistH = 32f, RunRowSpeedH = 24f, RunRowMileH = 24f, RunPadV = 5f;
    const float LvChipW = 320f, LvChipH = 30f, HpPanelHV2 = 46f;

    // 一時停止のボタンとメニューは HUD の大きさの設定を掛けない(押せる大きさを保つ)
    static bool hudScaleOff;
    public static float HudKBase => Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 720f, 1f, 1.8f);
    static float RunHudScale => hudScaleOff || Instance == null || !Instance.HasStarted ? 1f : HudPrefs.ScaleFactor;

    void HudBox(Rect r, float legacyAlpha)
    {
        if (HudPrefs.Legacy) UiBackdrop.Draw(r, legacyAlpha);
        else HudStyle.Panel(r);
    }

    // ---- 左上の走りの板
    bool RunRowBest => HudPrefs.Show(HudPrefs.Item.Best) && !ArenaMode.Active;
    bool RunRowDist => HudPrefs.Show(HudPrefs.Item.Distance);
    bool RunRowSpeed => HudPrefs.Show(HudPrefs.Item.Speed);
    bool RunRowMile => HudPrefs.Show(HudPrefs.Item.TakeHome) && ShowTakeHomeHud;
    float RunPanelHeightV
    {
        get
        {
            float h = (RunRowBest ? RunRowBestH : 0f) + (RunRowDist ? RunRowDistH : 0f) + (RunRowSpeed ? RunRowSpeedH : 0f) + (RunRowMile ? RunRowMileH : 0f);
            return h <= 0f ? 0f : h + RunPadV * 2f + ((RunRowBest || RunRowDist) && (RunRowSpeed || RunRowMile) ? 3f : 0f);
        }
    }
    float runPanelW = -1f; float runPanelWScreen = -1f; string runPanelWLang;
    float RunPanelWidthV
    {
        get
        {
            float vw = VW;
            if (runPanelWScreen != vw || runPanelWLang != Loc.Current)
            {
                var vs = HudStyle.ValueStyle(HudStyle.BigSize);
                float vwid = vs.CalcSize(new GUIContent(FormatDistanceExact(9999999.99))).x;
                var ls = HudStyle.LabelStyle();
                float lw = Mathf.Max(ls.CalcSize(new GUIContent(Loc.Auto("DISTANCE"))).x, ls.CalcSize(new GUIContent(Loc.Auto("SPEED"))).x);
                runPanelW = Mathf.Clamp(vwid + Mathf.Min(lw, 70f) + 30f, 200f, Mathf.Max(200f, vw * 0.5f - LvChipW * 0.5f - HudStyle.Margin - 12f));
                runPanelWScreen = vw; runPanelWLang = Loc.Current;
            }
            return runPanelW;
        }
    }
    // 縦画面の1段目(Lv と HP)の高さ
    float HudRow1HV
    {
        get
        {
            bool lv = HudPrefs.Show(HudPrefs.Item.LevelExp) && !ArenaMode.Active, hp = HpVisibleNow;
            return hp ? HpPanelHV2 : lv ? LvChipH : 0f;
        }
    }
    float RunTopV => VSafeTop + HudStyle.Margin + (HudStacked && HudRow1HV > 0f ? HudRow1HV + HudStyle.Gap : 0f);
    Rect GetRunPanelRect() => HudReal(VSafeLeft + HudStyle.Margin, RunTopV, RunPanelWidthV, RunPanelHeightV);
    // 左の列の下端(実際の画素)。板が無い時はその場所の上端
    float HudLeftBottomV2 => GetRunPanelRect().yMax;

    // ---- 中央上の Lv/EXP と、右上の HP
    Rect GetLvChipRect() => HudStacked
        ? HudReal(VSafeLeft + HudStyle.Margin, VSafeTop + HudStyle.Margin, Mathf.Max(120f, VW - VSafeLeft - VSafeRight - HudStyle.Margin * 2f - (HpVisibleNow ? HeartsPanelWidth + HudStyle.Gap : 0f)), LvChipH)
        : HudReal(VW / 2f - LvChipW / 2f, VSafeTop + HudStyle.Margin, LvChipW, LvChipH);
    Rect GetHpRectV2() => HudReal(VW - VSafeRight - HudStyle.Margin - HeartsPanelWidth, VSafeTop + HudStyle.Margin, HeartsPanelWidth, HpPanelHV2);

    // 非表示モードの HP: 被弾の直後と、残りが少ない間だけ短く見せる(どのくらい危ないかが全く分からなくならないように)
    float hpPeekUntil;
    public void NoteHudDamage() { hpPeekUntil = Time.unscaledTime + 2.5f; }
    bool HpLow => maxLives > 0 && Lives <= maxLives / 4;
    bool HpVisibleNow => HudPrefs.Show(HudPrefs.Item.Hp) || (HudPrefs.HiddenMode && (Time.unscaledTime < hpPeekUntil || HpLow));
    float HpPeekAlpha => HudPrefs.Show(HudPrefs.Item.Hp) ? 1f : HpLow ? 0.85f : Mathf.Clamp01((hpPeekUntil - Time.unscaledTime) / 0.5f);

    // カード(RunBuildHud)の上端と、縦画面で左の列を避ける境(実際の画素)
    public static float RunCardsTopPx
    {
        get
        {
            var gm = Instance;
            if (gm == null) return 0f;
            float k = HudK;
            if (HudPrefs.Legacy) return gm.SafeTop() + (UiMargin + HudPanelHeight + 10f) * k;
            return gm.HpVisibleNow && HudPrefs.Show(HudPrefs.Item.Hp) ? gm.GetHpRectV2().yMax + HudStyle.Gap * k : gm.SafeTop() + HudStyle.Margin * k;
        }
    }
    public static float RunCardsRightPx => Instance == null ? Screen.width : Screen.width - Instance.SafeRight() - (HudPrefs.Legacy ? UiMargin : HudStyle.Margin) * HudK;
    public static float RunCardsLeftLimitPx
    {
        get
        {
            var gm = Instance;
            if (gm == null) return 0f;
            if (HudPrefs.Legacy) return StableSafeArea.Rect.x + (UiMargin + 168f + 10f + 54f) * HudK;
            return gm.RunPanelHeightV > 0f ? gm.GetRunPanelRect().xMax + 10f * HudK : StableSafeArea.Rect.x + HudStyle.Margin * HudK;
        }
    }

    // ボスの欄(BossHudPanel)を置く場所(実際の画素)。横 = 中央上の Lv の下 / 縦 = 画面の下(地面の上。必殺技と一時停止の間)
    public static Rect BossPanelSlotPx(float heightPx)
    {
        var gm = Instance;
        if (gm == null) return default;
        float k = HudK;
        if (!HudStacked)
        {
            float top = HudPrefs.Show(HudPrefs.Item.LevelExp) && !ArenaMode.Active ? gm.GetLvChipRect().yMax + 6f * k : gm.SafeTop() + HudStyle.Margin * k;
            float left = gm.RunPanelHeightV > 0f ? gm.GetRunPanelRect().xMax : gm.SafeLeft();
            float right = RunBuildHud.Instance != null && RunBuildHud.Instance.IsVisible ? Mathf.Min(RunBuildHud.Instance.LastGridRect.xMin - 30f * k, gm.GetHpRectV2().xMin) : gm.GetHpRectV2().xMin;
            float w = Mathf.Min(440f * k, Mathf.Min(Screen.width * 0.5f - left, right - Screen.width * 0.5f) * 2f - 24f * k);
            w = Mathf.Max(260f * k, w);
            return new Rect(Screen.width * 0.5f - w * 0.5f, top, w, heightPx);
        }
        float kb = HudKBase;
        float l = gm.SafeLeft() + HudStyle.Margin * k, r = Screen.width - gm.SafeRight() - (UiMargin + 52f + 12f) * kb;
        if (UltimateArt.Instance != null && gm.UltimateHudVisible) l = Mathf.Max(l, UltimateArt.ButtonRect().xMax + 10f);
        float bottom = Screen.height - gm.SafeBottom() - UiMargin * kb;
        return new Rect(l, bottom - heightPx, Mathf.Max(200f, r - l), heightPx);
    }

    // ---- 描く(BeginHud の中)
    void DrawHudV2()
    {
        DrawRunPanel();
        if (HudPrefs.Show(HudPrefs.Item.FinalEvo)) DrawFinalEvoHud();
        if (!ArenaMode.Active && HudPrefs.Show(HudPrefs.Item.LevelExp)) DrawLvChip();
        if (HpVisibleNow)
        {
            Color keep = GUI.color;
            GUI.color = new Color(keep.r, keep.g, keep.b, keep.a * HpPeekAlpha);
            DrawHeartsPanel();
            GUI.color = keep;
        }
    }

    void DrawRunPanel()
    {
        var pc = PlayerController.Instance;
        float kmh = pc != null ? SpeedKmh(pc.AbilityRunSpeed) : 0f;
        TrackSpeedStep(kmh);
        Rect r = V(GetRunPanelRect());
        if (r.height > 0f)
        {
            HudStyle.Panel(r);
            float x = r.x + 10f, w = r.width - 20f, y = r.y + RunPadV;
            float labelW = Mathf.Min(70f, w * 0.34f);
            if (RunRowBest)
            {
                var ls = HudStyle.LabelStyle(11); HudStyle.Fit(ls, "BEST", labelW);
                Color gl = HudStyle.Gold; gl.a = 0.85f;
                HudStyle.Text(new Rect(x, y, labelW, RunRowBestH), "BEST", ls, gl);
                var vs = HudStyle.ValueStyle(14); string v = FormatDistanceExact(BestDisplayValue);
                HudStyle.Fit(vs, v, w - labelW);
                HudStyle.Text(new Rect(x + labelW, y, w - labelW, RunRowBestH), v, vs, HudStyle.Gold);
                y += RunRowBestH;
            }
            if (RunRowDist)
            {
                var ls = HudStyle.LabelStyle(); HudStyle.Fit(ls, "DISTANCE", labelW);
                HudStyle.Text(new Rect(x, y, labelW, RunRowDistH), "DISTANCE", ls, HudStyle.Label);
                double d = SprintActive && SprintRunner.Instance != null ? SprintRunner.Instance.DistanceNow : MaxDistanceExact; // 疾走中(リングの3択)は疾走の距離
                string v = FormatDistanceExact(d);
                float fl = DistanceFlashIntensity;
                var vs = HudStyle.ValueStyle(fl > 0f ? Mathf.RoundToInt(HudStyle.BigSize * Mathf.Lerp(1f, 1.2f, fl)) : HudStyle.BigSize);
                HudStyle.Fit(vs, v, w - labelW + 4f);
                HudStyle.Text(new Rect(x + labelW - 4f, y, w - labelW + 4f, RunRowDistH), v, vs, fl > 0f ? Color.Lerp(HudStyle.Value, new Color(1f, 0.85f, 0.4f), fl) : HudStyle.Value);
                y += RunRowDistH;
            }
            if ((RunRowBest || RunRowDist) && (RunRowSpeed || RunRowMile)) { HudStyle.Divider(x, y + 1f, w); y += 3f; }
            if (RunRowSpeed)
            {
                var ls = HudStyle.LabelStyle(); HudStyle.Fit(ls, "SPEED", labelW);
                HudStyle.Text(new Rect(x, y, labelW, RunRowSpeedH), "SPEED", ls, HudStyle.Label);
                var vs = HudStyle.ValueStyle(); string v = kmh.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " km/h";
                HudStyle.Text(new Rect(x + labelW, y, w - labelW, RunRowSpeedH), v, vs, pc != null && pc.SpeedRatio >= 1.01f ? HudStyle.Gold : HudStyle.Value);
                y += RunRowSpeedH;
            }
            if (RunRowMile)
            {
                const float icon = 15f;
                DrawMileMedal(new Rect(x, y + (RunRowMileH - icon) * 0.5f, icon, icon));
                string labelKey = Loc.IsJapanese ? "持ち帰り予定" : "持ち帰り予定MILE";
                string v = FormatMile(TakeHomeMile) + " MILE";
                var vs = HudStyle.ValueStyle(16);
                float vw = vs.CalcSize(new GUIContent(v)).x;
                var ls = HudStyle.LabelStyle(11); HudStyle.Fit(ls, labelKey, w - icon - 6f - vw - 6f, 8);
                HudStyle.Text(new Rect(x + icon + 5f, y, w - icon - 5f - vw, RunRowMileH), labelKey, ls, HudStyle.Label);
                HudStyle.Text(new Rect(x, y, w, RunRowMileH), v, vs, HudStyle.Gold);
            }
        }
        if (HudPrefs.Show(HudPrefs.Item.Guide)) DrawSpeedUpNotice(r.height > 0f ? r.yMax : V(GetRunPanelRect()).y, V(GetRunPanelRect()).x);
    }

    // 速度の段(20km/h ごと)が上がった瞬間を覚える(表示しない設定でも数え続ける = 表示に戻した時に古い通知が出ない)
    void TrackSpeedStep(float kmh)
    {
        int step = Mathf.FloorToInt(kmh / Mathf.Max(1f, speedNoticeStepKmh) + 0.0001f);
        if (Event.current.type != EventType.Repaint) return;
        if (speedHudStep >= 0 && step > speedHudStep) { speedUpShownAt = Time.unscaledTime; speedUpShownKmh = kmh; }
        speedHudStep = step;
    }
    void DrawSpeedUpNotice(float y, float x)
    {
        float since = Time.unscaledTime - speedUpShownAt;
        if (since < 0f || since >= SpeedUpNoticeSeconds) return;
        float a = since < 0.2f ? since / 0.2f : Mathf.Clamp01((SpeedUpNoticeSeconds - since) / 0.5f);
        var st = HudStyle.ValueStyle(15, TextAnchor.MiddleLeft);
        Color c = HudStyle.Gold; c.a = a;
        HudStyle.Text(new Rect(x + 4f, y + 3f, 240f, 22f), "SPEED UP! " + speedUpShownKmh.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " km/h", st, c);
    }

    void DrawLvChip()
    {
        Rect r = V(GetLevelExpPanelRect());
        HudStyle.Panel(r);
        var ls = HudStyle.ValueStyle(17, TextAnchor.MiddleLeft);
        string lv = $"Lv.{Level}";
        float lw = Mathf.Max(52f, ls.CalcSize(new GUIContent(lv)).x + 4f);
        HudStyle.Text(new Rect(r.x + 10f, r.y, lw, r.height), lv, ls, HudStyle.Gold);
        float frac = ExpToNext > 0f ? Mathf.Clamp01(Exp / ExpToNext) : 0f;
        float flash = expBarFlashDuration > 0f ? Mathf.Clamp01(expBarFlashTimer / expBarFlashDuration) : 0f;
        Color fill = Color.Lerp(new Color(0.35f, 0.75f, 1f), new Color(1f, 0.85f, 0.4f), flash);
        float bx = r.x + 14f + lw;
        HudStyle.Bar(new Rect(bx, r.y + r.height * 0.5f - 5f, r.xMax - 12f - bx, 10f), frac, fill);
    }
}
