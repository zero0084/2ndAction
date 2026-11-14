using UnityEngine;

// ガチャの「提供割合」(2026-11-15、マスター依頼)。ガチャの札の左(横画面)/ ガチャのボタンの列の上の帯(縦画面)の「提供割合」から開く。
//  ・中身は抽選と同じ関数(GameManager.GachaOddsNow → GachaFinalOdds / GachaShareOdds / GachaRarityOdds)をそのまま表にする。別の計算は持たない。
//  ・表の数字はすべて「1回あたり」の割合: 共通の行 = 共通の割合 × 共通のレア度の割合、マップの行も同じ。合計の行 = 2行の和。
//  ・マップに対象のカードが無い時は、表の代わりに「このマップで引けるカードがまだありません」と条件(分かる時)。
//  ・閉じる: 「閉じる」/ 戻る(GameManager.DoBack)/ 外側のタップでは閉じない(読み途中で消えないように)。ラン中は開かない。
public class GachaOddsPanel : MonoBehaviour
{
    public static bool IsOpen { get; private set; }
    public static int Opened { get; private set; } // 確認用
    static GachaOddsPanel inst;
    Vector2 scroll;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (inst != null) return;
        var go = new GameObject("GachaOddsPanel");
        DontDestroyOnLoad(go);
        inst = go.AddComponent<GachaOddsPanel>();
    }

    public static void Open()
    {
        var gm = GameManager.Instance;
        if (gm == null || gm.HasStarted) return;
        IsOpen = true; Opened++;
        if (inst != null) inst.scroll = Vector2.zero;
        Debug.Log("[Gacha] odds panel opened");
    }
    public static void Close() { if (!IsOpen) return; IsOpen = false; UiInputGate.LatchUntilRelease(); }

    void Update()
    {
        var gm = GameManager.Instance;
        if (IsOpen && (gm == null || gm.HasStarted)) IsOpen = false;
    }

    static string Pct(float v) => (v <= 0.000001f ? 0f : v * 100f).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%";

    void OnGUI()
    {
        if (!IsOpen) return;
        var gm = GameManager.Instance;
        if (gm == null) return;
        GUI.depth = -2060; // 達成の知らせ(ChallengeToast -2050)より手前(表の上に重ならない)
        int padLayer = PadNav.BeginLayer(72);
        float s = UiScalePref.Menu(Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 720f, 0.8f, 2.6f));
        Matrix4x4 keep = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float w = Screen.width / s, h = Screen.height / s;
        UiKit.Fill(new Rect(0f, 0f, w, h), new Color(0.02f, 0.03f, 0.08f, 0.66f));
        float pw = Mathf.Min(760f, w - 24f);
        float cw = pw - 48f;
        var cat = gm.GachaCategory;
        float[] total = gm.GachaOddsNow(out float[] commonPart, out float[] mapPart, out int mapCount, out int commonCount);
        float[] share = GameManager.GachaShareOdds(mapCount, commonCount);
        string mapName = Loc.T(CardMapCategories.LabelSrc(cat));

        var body = new GUIStyle(UiKit.Label(17f, TextAnchor.UpperLeft, false, UiTheme.Text)) { wordWrap = true };
        float btnH = 52f;
        // エステル: 対象マップ → 表 → 注記 → 詳しい抽選の方法 の順
        string mapLine = Loc.F(LocGacha.OddsMapIs, Loc.AutoPlain(CardMapCategories.LabelSrc(cat))); // 差し込む名前は並べ替えの前
        float mapLineH = body.CalcHeight(new GUIContent(mapLine), cw);
        string how = Loc.T(LocGacha.OddsHow);
        float howH = body.CalcHeight(new GUIContent(how), cw);
        // 表: 分類 | 割合 | ★1..★5(縦画面でも収まるように列の幅は残りを等分)
        float nameW = Mathf.Min(170f, cw * 0.27f), shareW = Mathf.Min(80f, cw * 0.13f), starW = (cw - nameW - shareW) / 5f, rowH = 44f;
        float tableH = mapCount > 0 ? rowH * 4f + 8f : 0f;
        var noteSt = new GUIStyle(UiKit.Label(15f, TextAnchor.UpperLeft, false, UiTheme.TextSub)) { wordWrap = true };
        string foot = mapCount > 0 ? Loc.T(LocGacha.OddsPerPull) : "";
        float footH = foot.Length > 0 ? noteSt.CalcHeight(new GUIContent(foot), cw) : 0f;
        float contentH = mapLineH + 10f + (mapCount > 0 ? tableH : 90f) + 8f + footH + 10f + howH + 6f;
        // 中身に合わせた高さ(画面に入らない時は中身をスクロール)
        float ph = Mathf.Min(h - 24f, 16f + 46f + contentH + 12f + btnH + 20f);
        var p = new Rect((w - pw) * 0.5f, (h - ph) * 0.5f, pw, ph);
        OrnateUi.DrawPanel(p, 0.97f);
        float x = p.x + 24f, y = p.y + 16f;
        LocGUI.Label(new Rect(x, y, cw, 40f), LocGacha.Odds, UiKit.Label(26f, TextAnchor.MiddleCenter, true, UiTheme.GoldText));
        y += 46f;
        float viewH = p.yMax - 20f - btnH - 12f - y;
        scroll = GUI.BeginScrollView(new Rect(x - 4f, y, cw + 8f + (contentH > viewH ? 16f : 0f), viewH), scroll, new Rect(0f, 0f, cw, contentH), false, false);
        float yy = 0f;
        GUI.Label(new Rect(0f, yy, cw, mapLineH), LocGUI.RtlFix(mapLine, cw, body), body); // 右から左の言語は元の順で行に分けてから並べ替える
        yy += mapLineH + 10f;
        if (mapCount > 0)
        {
            var head = new GUIStyle(UiKit.Label(15f, TextAnchor.MiddleCenter, true, UiTheme.GoldText)); // 複製(共有の見た目を後の呼び出しが変えるため)
            var cell = new GUIStyle(UiKit.Label(17f, TextAnchor.MiddleCenter, false, UiTheme.Text));
            var nameSt = new GUIStyle(UiKit.Label(17f, TextAnchor.MiddleLeft, true, UiTheme.Text));
            var sub = new GUIStyle(UiKit.Label(12f, TextAnchor.MiddleLeft, false, UiTheme.TextSub));
            UiKit.Fill(new Rect(0f, yy, cw, rowH * 0.8f), new Color(0.1f, 0.12f, 0.22f, 0.9f));
            LocGUI.Label(new Rect(6f, yy, nameW, rowH * 0.8f), LocGacha.OddsShare, new GUIStyle(head) { alignment = TextAnchor.MiddleLeft });
            LocGUI.Label(new Rect(nameW, yy, shareW, rowH * 0.8f), LocGacha.OddsTotal, head); // 分類の割合の列(エステル: 「合計」)
            for (int i = 0; i < 5; i++) GUI.Label(new Rect(nameW + shareW + i * starW, yy, starW, rowH * 0.8f), "★" + (i + 1), head);
            yy += rowH * 0.8f + 2f;
            void Row(string label, string count, float sh, float[] v, bool bold)
            {
                UiKit.Fill(new Rect(0f, yy, cw, rowH), bold ? new Color(0.2f, 0.17f, 0.08f, 0.85f) : new Color(0.06f, 0.07f, 0.14f, 0.85f));
                GUI.Label(new Rect(6f, yy + (count != null ? 2f : 0f), nameW - 6f, count != null ? rowH * 0.6f : rowH), label, nameSt);
                if (count != null) GUI.Label(new Rect(6f, yy + rowH * 0.58f, nameW - 6f, rowH * 0.4f), count, sub);
                var cs = new GUIStyle(cell) { fontStyle = bold ? FontStyle.Bold : FontStyle.Normal };
                if (bold) cs.normal.textColor = UiTheme.GoldText;
                GUI.Label(new Rect(nameW, yy, shareW, rowH), Pct(sh), cs);
                var zero = new GUIStyle(cs); zero.normal.textColor = new Color(cs.normal.textColor.r, cs.normal.textColor.g, cs.normal.textColor.b, 0.38f); // 出ない星は薄い 0%
                for (int i = 0; i < 5; i++) GUI.Label(new Rect(nameW + shareW + i * starW, yy, starW, rowH), Pct(v[i]), v[i] <= 0.000001f ? zero : cs);
                yy += rowH + 2f;
            }
            Row(Loc.T(CardMapCategories.LabelSrc(CardMapCategory.Common)), Loc.F(LocGacha.OddsCards, commonCount), share[0], commonPart, false);
            Row(mapName, Loc.F(LocGacha.OddsCards, mapCount), share[1], mapPart, false);
            Row(Loc.T(LocGacha.OddsTotal), null, share[0] + share[1], total, true);
            yy += 6f;
        }
        else
        {
            string hint = gm.GachaMapUnlockHint(cat);
            var warn = new GUIStyle(UiKit.Label(18f, TextAnchor.MiddleCenter, true, new Color(1f, 0.74f, 0.6f))) { wordWrap = true };
            UiKit.Fill(new Rect(0f, yy, cw, 80f), new Color(0.06f, 0.07f, 0.14f, 0.85f));
            string t1 = LocGUI.RtlFix(Loc.T(LocGacha.NoCardsMap), cw - 16f, warn) + (string.IsNullOrEmpty(hint) ? "" : "\n" + LocGUI.RtlFix(Loc.F(LocGacha.Condition, hint), cw - 16f, warn));
            GUI.Label(new Rect(8f, yy, cw - 16f, 80f), t1, warn);
            yy += 90f;
        }
        if (foot.Length > 0) { GUI.Label(new Rect(0f, yy, cw, footH), LocGUI.RtlFix(foot, cw, noteSt), noteSt); yy += footH + 10f; }
        var howSt = new GUIStyle(body) { normal = { textColor = UiTheme.TextSub } };
        GUI.Label(new Rect(0f, yy, cw, howH), LocGUI.RtlFix(how, cw, howSt), howSt);
        GUI.EndScrollView();
        if (UiKit.Button(new Rect(p.center.x - 120f, p.yMax - 20f - btnH, 240f, btnH), "閉じる", 20f, true, true)) Close();
        GUI.matrix = keep;
        GUI.Button(new Rect(0f, 0f, Screen.width, Screen.height), GUIContent.none, GUIStyle.none); // 背後へ通さない
        if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp || Event.current.type == EventType.MouseDrag) Event.current.Use();
        PadNav.EndLayer(padLayer);
    }
}
