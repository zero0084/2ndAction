using UnityEngine;

// データの削除の画面(2026-10-19)。設定 →「データとプライバシー」→「データの削除」から開く(AccountDeletion が処理)。
//  消す物/消さない物、プレイヤーID(コピー)、アプリを消した後の申請先(メール)を見せ、削除の前に「取り消せない」ことを確かめる
public class DataDeletionUi : MonoBehaviour
{
    public static bool IsOpen { get; private set; }
    static DataDeletionUi instance;
    float copiedUntil;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (instance != null) return;
        var go = new GameObject("DataDeletionUi");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<DataDeletionUi>();
    }

    public static void Open() { IsOpen = true; AccountDeletion.Reset(); AccountDeletion.Check(); }
    public static void Close() { if (AccountDeletion.Busy) return; IsOpen = false; UiInputGate.LatchUntilRelease(); }

    float S => Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 720f, 1f, 2.6f);

    void OnGUI()
    {
        if (!IsOpen) return;
        GUI.depth = -2300; // 設定(-2000)/広告なしパス(-2200)より手前
        int padLayer = PadNav.BeginLayer(12);
        float s = S;
        Matrix4x4 keep = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float w = Screen.width / s, h = Screen.height / s;
        UiKit.Fill(new Rect(0f, 0f, w, h), new Color(0.02f, 0.03f, 0.08f, 0.72f));
        float pw = Mathf.Min(660f, w - 24f), ph = Mathf.Min(680f, h - 24f);
        var p = new Rect((w - pw) * 0.5f, (h - ph) * 0.5f, pw, ph);
        OrnateUi.DrawPanel(p, 0.96f);
        LocGUI.Label(new Rect(p.x + 24f, p.y + 12f, p.width - 100f, 44f), "データの削除", UiKit.Label(26f, TextAnchor.MiddleLeft, true, UiTheme.GoldText));
        bool busy = AccountDeletion.Busy;
        if (UiIcons.CloseButton(new Rect(p.xMax - 66f, p.y + 12f, 48f, 42f), !busy)) Close();

        float x = p.x + 28f, y = p.y + 64f, full = p.width - 56f;
        var head = new GUIStyle(UiKit.Label(17f, TextAnchor.UpperLeft, true, UiTheme.GoldText)) { wordWrap = true };
        var body = new GUIStyle(UiKit.Label(15f, TextAnchor.UpperLeft, false, UiTheme.Text)) { wordWrap = true };
        var small = new GUIStyle(UiKit.Label(13f, TextAnchor.UpperLeft, false, UiTheme.TextSub)) { wordWrap = true };
        float Line(string text, GUIStyle st, float gap = 4f)
        {
            string t = Loc.Auto(text);
            float hh = st.CalcHeight(new GUIContent(t), full);
            GUI.Label(new Rect(x, y, full, hh), t, st);
            return hh + gap;
        }

        var phase = AccountDeletion.State;
        if (phase == AccountDeletion.Phase.Confirm)
        {
            y += Line("本当に削除しますか?", new GUIStyle(head) { fontSize = 20 }, 8f);
            y += Line("サーバー上のランキングの記録・送った記録の履歴・オンライン用の匿名のプレイヤーIDを削除します。この操作は取り消せません。", new GUIStyle(body) { normal = { textColor = UiTheme.Danger } }, 8f);
            y += Line("この端末のカード・MILE・進行と、広告なしパスの購入は消えません。", body, 8f);
            float bw = (full - 12f) / 2f, by = Mathf.Max(y + 10f, p.yMax - 24f - 52f);
            if (UiKit.Button(new Rect(x, by, bw, 52f), "削除する", 18f, true, false)) AccountDeletion.Run();
            if (UiKit.Button(new Rect(x + bw + 12f, by, bw, 52f), "やめる", 18f, false, false)) AccountDeletion.CancelConfirm();
        }
        else
        {
            y += Line("削除するもの(サーバー上のデータ)", head, 2f);
            y += Line("・ランキングの記録(すべてのマップ)と、掲載中のコードネーム", body, 1f);
            y += Line("・ランキングへ送った記録の履歴", body, 1f);
            y += Line("・オンライン用の匿名のプレイヤーID", body, 8f);
            y += Line("削除しないもの(この端末のデータ)", head, 2f);
            y += Line("・カード・MILE・解放したキャラクター・進行・設定・コードネーム", body, 1f);
            y += Line("・広告なしパス(ストアのアカウントで管理。削除した後も「購入を復元」で使えます)", body, 8f);

            y += Line("あなたのプレイヤーID", head, 2f);
            string idText = phase == AccountDeletion.Phase.Checking ? Loc.T("確認中…")
                : phase == AccountDeletion.Phase.NoAccount ? Loc.T("この端末では、まだオンライン機能(ランキング・ONLINE PLAY)を使っていません")
                : phase == AccountDeletion.Phase.Unavailable ? Loc.T("オンラインのサービスに接続できませんでした")
                : string.IsNullOrEmpty(AccountDeletion.PlayerId) ? "-" : AccountDeletion.PlayerId;
            float idW = full - 130f;
            var idSt = new GUIStyle(body) { wordWrap = true };
            float idH = Mathf.Max(30f, idSt.CalcHeight(new GUIContent(idText), idW));
            GUI.Label(new Rect(x, y, idW, idH), idText, idSt);
            bool hasId = !string.IsNullOrEmpty(AccountDeletion.PlayerId);
            if (hasId && UiKit.Button(new Rect(x + full - 120f, y - 2f, 120f, 38f), Time.unscaledTime < copiedUntil ? "コピーしました" : "IDをコピー", 15f, false, false))
            { PrivacyLinks.Copy(AccountDeletion.PlayerId); copiedUntil = Time.unscaledTime + 2f; }
            y += idH + 8f;
            y += Line($"アプリを削除した後や、ここで削除できない時は、プレイヤーIDを書いて {PrivacyLinks.ContactEmail} へご連絡ください。", small, 2f); // 訳は Loc の型(…{0}…)で当たる
            y += Line("削除した後にランキングへ参加すると、新しいIDで記録します。", small, 6f);

            if (phase == AccountDeletion.Phase.Done || phase == AccountDeletion.Phase.Failed || (phase == AccountDeletion.Phase.Unavailable && !string.IsNullOrEmpty(AccountDeletion.Message)))
            {
                var col = phase == AccountDeletion.Phase.Done ? new Color(0.55f, 1f, 0.65f) : UiTheme.Danger;
                y += Line(AccountDeletion.Message, new GUIStyle(body) { fontStyle = FontStyle.Bold, normal = { textColor = col } }, 2f);
                if (phase == AccountDeletion.Phase.Failed) y += Line("通信を確認してもう一度試すか、上のメールでご連絡ください。", small, 4f);
            }

            float bw = (full - 12f) / 2f, by = Mathf.Max(y + 6f, p.yMax - 24f - 52f);
            bool canDelete = phase == AccountDeletion.Phase.Ready || phase == AccountDeletion.Phase.Failed && hasId;
            string label = phase == AccountDeletion.Phase.Deleting ? Loc.T("削除しています…") : phase == AccountDeletion.Phase.Done ? Loc.T("削除しました") : Loc.T("削除する");
            if (UiKit.Button(new Rect(x, by, bw, 52f), label, 18f, true, false, canDelete) && canDelete) AccountDeletion.AskConfirm();
            if (UiKit.Button(new Rect(x + bw + 12f, by, bw, 52f), "閉じる", 18f, false, false, !busy) && !busy) Close();
        }

        GUI.matrix = keep;
        GUI.Button(new Rect(0f, 0f, Screen.width, Screen.height), GUIContent.none, GUIStyle.none); // 背後へ通さない
        if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp || Event.current.type == EventType.MouseDrag) Event.current.Use();
        PadNav.EndLayer(padLayer);
    }
}
