using UnityEngine;

// 広告なしパスの画面(2026-10-10、依頼I)。設定 →「広告なしパス」から開く。Steam/PC(Mode=None)では出ない。
//  ・特典3つ / 買い切り / 制限 / ストアの現地通貨の価格(コードに価格を書かない) / 購入 / 購入を復元 / 権利の復元と育成セーブの復元は別、を出す
//  ・購入はこの画面のボタンだけ(DEBUG の画面からは本物の購入を呼ばない)
public class MonetizationUi : MonoBehaviour
{
    public static bool Open { get; private set; }
    public static void OpenPass() { if (Monetization.Mode == MonetizationMode.None) return; AdManager.Ensure(); Open = true; }
    public static void Close() { Open = false; UiInputGate.LatchUntilRelease(); }

    float S => Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 720f, 1f, 2.6f);

    void OnGUI()
    {
        if (!Open || AdManager.Showing) return;
        GUI.depth = -2200; // 設定(-2000)/DEBUG(-2100)より手前
        int padLayer = PadNav.BeginLayer(11);
        float s = S;
        Matrix4x4 keep = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float w = Screen.width / s, h = Screen.height / s;
        UiKit.Fill(new Rect(0f, 0f, w, h), new Color(0.02f, 0.03f, 0.08f, 0.7f));
        float pw = Mathf.Min(620f, w - 24f), ph = Mathf.Min(640f, h - 24f);
        var p = new Rect((w - pw) * 0.5f, (h - ph) * 0.5f, pw, ph);
        OrnateUi.DrawPanel(p, 0.96f);
        LocGUI.Label(new Rect(p.x + 24f, p.y + 12f, p.width - 100f, 44f), "広告なしパス(買い切り)", UiKit.Label(26f, TextAnchor.MiddleLeft, true, new Color(1f, 0.86f, 0.45f)));
        if (UiKit.Button(new Rect(p.xMax - 66f, p.y + 12f, 48f, 42f), "×", 24f, false, false)) Close();

        float x = p.x + 28f, y = p.y + 66f, full = p.width - 56f;
        var body = new GUIStyle(UiKit.Label(16f, TextAnchor.UpperLeft, false, Color.white)) { wordWrap = true };
        var small = new GUIStyle(UiKit.Label(13f, TextAnchor.UpperLeft, false, new Color(0.78f, 0.82f, 0.9f))) { wordWrap = true };
        float Line(string text, GUIStyle st, float gap = 6f)
        {
            string t = Loc.Auto(text);
            float hh = st.CalcHeight(new GUIContent(t), full);
            GUI.Label(new Rect(x, y, full, hh), t, st);
            return hh + gap;
        }
        if (Monetization.Mode == MonetizationMode.Mock)
            y += Line("模擬(開発版のみ): 本物の購入ではありません。本物の購入の権利とは別に保存します", new GUIStyle(small) { normal = { textColor = new Color(1f, 0.6f, 0.5f) } });
        y += Line("特典", new GUIStyle(body) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.85f, 0.5f) } }, 2f);
        y += Line("1. ゲームオーバー後の広告が出なくなります", body);
        y += Line("2. 脱出に成功したランの MILE 2倍を、広告を見ずに受け取れます(1ランにつき1回)", body);
        y += Line("3. 1日3回、カードを無料で引けます(広告で引く回数と共通。日本時間 0時に回復)", body);
        y += 4f;
        y += Line("買い切りです(1回の購入。定期的な支払いはありません)", body);
        y += Line("・MILE 2倍の額や、1日に引ける回数は広告で受け取る場合と同じです(増えません)", small, 3f);
        y += Line("・購入の権利はストアのアカウントに付きます。機種変更/再インストールの後は「購入を復元」で戻せます。育成データ(カード/MILE 等)の復元とは別です", small, 3f);
        y += Line("・オフラインでも、最後に確認した状態のまま使えます", small, 8f);

        bool owned = NoAdsPass.Owned;
        string price = NoAdsPass.PriceText;
        if (owned) y += Line("有効です", new GUIStyle(body) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.55f, 1f, 0.65f) } });
        else if (NoAdsPass.PendingPayment) y += Line("お支払いの完了待ちです。完了すると自動で有効になります", new GUIStyle(body) { normal = { textColor = new Color(1f, 0.85f, 0.5f) } });

        float bh = 52f, bw = (full - 12f) / 2f;
        float by = Mathf.Max(y + 6f, p.yMax - 24f - bh - 34f);
        bool busy = NoAdsPass.Busy;
        string buyLabel = owned ? Loc.T("購入済み") : string.IsNullOrEmpty(price) ? Loc.T("価格を取得できません") : Loc.F("購入する {0}", price);
        bool canBuy = !owned && !busy && !string.IsNullOrEmpty(price) && !NoAdsPass.PendingPayment;
        if (UiKit.Button(new Rect(x, by, bw, bh), buyLabel, 18f, true, false, canBuy) && canBuy) NoAdsPass.Purchase(null);
        if (UiKit.Button(new Rect(x + bw + 12f, by, bw, bh), busy ? Loc.T("確認中…") : Loc.T("購入を復元"), 18f, false, false, !busy) && !busy) NoAdsPass.Check("restore button");
        if (!string.IsNullOrEmpty(NoAdsPass.LastNote)) GUI.Label(new Rect(x, by + bh + 4f, full, 30f), Loc.Auto(NoAdsPass.LastNote), new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });

        GUI.matrix = keep;
        GUI.Button(new Rect(0f, 0f, Screen.width, Screen.height), GUIContent.none, GUIStyle.none); // 背後へ通さない
        if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp || Event.current.type == EventType.MouseDrag) Event.current.Use();
        PadNav.EndLayer(padLayer);
    }
}
