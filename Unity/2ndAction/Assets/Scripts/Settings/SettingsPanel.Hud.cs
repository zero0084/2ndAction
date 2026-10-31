using UnityEngine;

// 設定「画面情報の表示」(2026-10-31、UI 全体の改善): ラン中の HUD の表示モード / カスタムの項目 / 大きさ / 背景の濃さ。
// 変えたらすぐ反映(HUD は毎フレーム HudPrefs を見る)。ラン中は 一時停止 → 設定 から開けるので、全部隠しても戻せる。
public partial class SettingsPanel
{
    static readonly HudPrefs.Item[] HudItemOrder =
    {
        HudPrefs.Item.Best, HudPrefs.Item.Distance, HudPrefs.Item.Speed, HudPrefs.Item.TakeHome,
        HudPrefs.Item.LevelExp, HudPrefs.Item.Hp, HudPrefs.Item.CharCards, HudPrefs.Item.Deck,
        HudPrefs.Item.BossHp, HudPrefs.Item.BreakGauge, HudPrefs.Item.Combo, HudPrefs.Item.FinalEvo,
        HudPrefs.Item.Guide, HudPrefs.Item.Party,
    };
    public static string HudItemLabel(HudPrefs.Item it)
    {
        switch (it)
        {
            case HudPrefs.Item.Best: return "BEST";
            case HudPrefs.Item.Distance: return "現在の距離";
            case HudPrefs.Item.Speed: return "速度";
            case HudPrefs.Item.TakeHome: return "持ち帰り予定MILE";
            case HudPrefs.Item.LevelExp: return "Lv・EXP";
            case HudPrefs.Item.Hp: return "HP";
            case HudPrefs.Item.CharCards: return "キャラカード";
            case HudPrefs.Item.Deck: return "デッキ";
            case HudPrefs.Item.BossHp: return "ボスのHP";
            case HudPrefs.Item.BreakGauge: return "BREAKゲージ";
            case HudPrefs.Item.Combo: return "コンボ数";
            case HudPrefs.Item.FinalEvo: return "FINAL EVOLUTIONの残り時間";
            case HudPrefs.Item.Guide: return "操作ガイド・補助情報";
            case HudPrefs.Item.Party: return "マルチの参加者";
        }
        return it.ToString();
    }
    static readonly string[] HudModeNames = { "標準", "シンプル", "非表示", "カスタム" };
    static string HudModeNote(HudPrefs.Mode m) =>
        m == HudPrefs.Mode.Simple ? "シンプル: HP・距離・速度・ボスのHPとBREAKだけを表示します"
        : m == HudPrefs.Mode.Hidden ? "非表示: 画面情報を隠します。被弾時とHPが少ない時だけHPを表示します"
        : m == HudPrefs.Mode.Custom ? "カスタム: 表示する項目を選べます"
        : "標準: すべての情報を整理して表示します";

    const float HudChipH = 44f, HudChipGap = 6f;
    int HudChipCols(float w) => w >= 640f ? 3 : 2;
    float MeasureHud(float w = 440f)
    {
        float h = HeadH + 4f + (RowH - 6f) + 26f + RowH * 2f + 46f;
        if (HudPrefs.CurrentMode == HudPrefs.Mode.Custom)
        {
            int rows = Mathf.CeilToInt(HudItemOrder.Length / (float)HudChipCols(w));
            h += rows * (HudChipH + HudChipGap) + 8f;
        }
        return h;
    }

    static GUIStyle hudFitStyle;
    static float HudFit(string text, float width, float size)
    {
        if (hudFitStyle == null) hudFitStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, wordWrap = false };
        hudFitStyle.fontSize = Mathf.RoundToInt(size);
        float tw = hudFitStyle.CalcSize(new GUIContent(Loc.Auto(text))).x;
        return tw > width && tw > 1f ? Mathf.Max(10f, Mathf.Floor(size * width / tw)) : size;
    }

    float DrawHud(float x, float y, float w, bool interactive)
    {
        y = Head(x, y, w, "画面情報の表示");
        var mode = HudPrefs.CurrentMode;
        // 4つのモード(全幅に並べる。長い言語では文字を小さく)
        {
            float gap = 6f, bw = (w - gap * 3f) / 4f, fs = 17f;
            foreach (var n in HudModeNames) fs = Mathf.Min(fs, HudFit(n, bw - 10f, 17f));
            int c = UiKit.Choice(new Rect(x, y, w, RowH - 14f), (int)mode, HudModeNames, fs);
            if (interactive && c != (int)mode) { HudPrefs.SetMode((HudPrefs.Mode)c); mode = (HudPrefs.Mode)c; }
            y += RowH - 6f;
        }
        y = Note(x, y, w, HudModeNote(mode));
        if (mode == HudPrefs.Mode.Custom)
        {
            int cols = HudChipCols(w);
            float cw = (w - HudChipGap * (cols - 1)) / cols;
            y += 4f;
            for (int i = 0; i < HudItemOrder.Length; i++)
            {
                var it = HudItemOrder[i];
                var r = new Rect(x + (i % cols) * (cw + HudChipGap), y + (i / cols) * (HudChipH + HudChipGap), cw, HudChipH);
                bool on = HudPrefs.CustomOn(it);
                // ON は金の縁+金の文字、OFF は暗く。左端に四角の印(ON = 金で塗る / OFF = 枠だけ。字形に頼らない)
                float fs = HudFit(HudItemLabel(it), cw - 26f - 14f, 16f); // 項目ごとに幅へ合わせる(一番長い名前に全部をそろえると小さくなりすぎた)
                if (UiKit.Button(new Rect(r.x + 26f, r.y, r.width - 26f, r.height), HudItemLabel(it), fs, on, false) && interactive) HudPrefs.SetCustom(it, !on);
                var box = new Rect(r.x + 4f, r.y + r.height * 0.5f - 9f, 18f, 18f);
                Color edge = on ? UiTheme.GoldText : new Color(0.55f, 0.58f, 0.66f);
                UiKit.Fill(new Rect(box.x, box.y, box.width, 2f), edge); UiKit.Fill(new Rect(box.x, box.yMax - 2f, box.width, 2f), edge);
                UiKit.Fill(new Rect(box.x, box.y, 2f, box.height), edge); UiKit.Fill(new Rect(box.xMax - 2f, box.y, 2f, box.height), edge);
                if (on) UiKit.Fill(new Rect(box.x + 4f, box.y + 4f, box.width - 8f, box.height - 8f), edge);
                if (GUI.Button(box, GUIContent.none, GUIStyle.none) && interactive) HudPrefs.SetCustom(it, !on);
            }
            y += Mathf.CeilToInt(HudItemOrder.Length / (float)cols) * (HudChipH + HudChipGap) + 4f;
        }
        // 大きさ / 背景の濃さ
        {
            RowLabel(x, y, "HUDの大きさ");
            int cur = HudPrefs.ScaleIndex;
            string[] opts = { "小", "標準", "大" };
            int c = UiKit.Choice(new Rect(x + LabelW, y + 7f, w - LabelW, RowH - 14f), cur, opts, 17f);
            if (interactive && c != cur) HudPrefs.SetScale(c);
            y += RowH;
            RowLabel(x, y, "HUDの背景");
            cur = HudPrefs.OpacityIndex;
            string[] opts2 = { "通常", "薄め", "最小" };
            float fs2 = 17f; foreach (var n in opts2) fs2 = Mathf.Min(fs2, HudFit(n, (w - LabelW) / 3f - 14f, 17f));
            c = UiKit.Choice(new Rect(x + LabelW, y + 7f, w - LabelW, RowH - 14f), cur, opts2, fs2);
            if (interactive && c != cur) HudPrefs.SetOpacity(c);
            y += RowH;
        }
        return Note(x, y, w, "一時停止ボタンはどのモードでも表示されます。ラン中も一時停止メニューから設定を変更できます");
    }
}
