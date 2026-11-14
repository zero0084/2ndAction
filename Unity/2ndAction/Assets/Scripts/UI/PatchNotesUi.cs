using System.Collections.Generic;
using UnityEngine;

// お知らせ(更新内容)の画面(2026-11-14)。タイトル画面の左上の「お知らせ」から開く。
//  ・中身はアプリに同梱(下の Notes。オフラインでも読める)。新しい版を先頭に: バージョン・更新日・主な変更点・(必要なら)既知の不具合
//  ・まだ読んでいない版がある時だけ、ボタンに NEW。開いたら「一番新しい版まで読んだ」を保存(設定の扱い。データの切り替えで消えない)
//  ・長い文はスクロール(UiScroll: 指/ホイール/パッド)。閉じる(×/閉じる/戻る/Esc)とタイトルへ
//  ・開いている間と、閉じた指が離れるまでは、後ろの「ゲーム開始」へ届かない(TitleScreen が Open と UiInputGate.Latched を見る)
//  ・文章は利用者向け(内部の試験名・実装の言葉を並べない)。原文は日本語、訳は Loc(未訳の言語は英語 → 日本語の順に出る)
public class PatchNotesUi : MonoBehaviour
{
    public const string ReadKey = "PatchNotesReadV1"; // 読んだ一番新しい版(int)
    public static bool IsOpen { get; private set; }
    public static float ClosedAt { get; private set; } = -99f; // 閉じた時刻(実時間)。閉じた入力(戻るキー等)を後ろの開始に数えない
    public static int Opens { get; private set; } // 確認用

    public struct Note
    {
        public int code; public string date; public string[] changes; public string[] known;
    }

    // 新しい順。code は Android の versionCode(Windows でも同じ番号で数える)
    public static readonly Note[] Notes =
    {
        new Note { code = 451, date = "2026/11/14", changes = new[] {
            LocNotes.N451a, LocNotes.N451b, LocNotes.N451c, LocNotes.N451d, LocNotes.N451e } },
        new Note { code = 450, date = "2026/11/08", changes = new[] {
            LocNotes.N450a, LocNotes.N450b, LocNotes.N450c, LocNotes.N450d, LocNotes.N450e } },
    };
    public static int Latest => Notes.Length > 0 ? Notes[0].code : 0;
    public static bool HasUnread => SaveStore.GetInt(ReadKey, 0) < Latest;

    static PatchNotesUi instance;
    readonly UiScroll scroll = new UiScroll();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        if (instance != null) return;
        var go = new GameObject("[PatchNotesUi]");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PatchNotesUi>();
    }

    public static void Open()
    {
        IsOpen = true; Opens++;
        Debug.Log("[Notes] open");
        if (instance != null) instance.scroll.Reset();
        if (HasUnread) { SaveStore.SetInt(ReadKey, Latest); SaveStore.Save(); }
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Decide);
        UiInputGate.LatchUntilRelease(); // 開いた指で中のボタンを押さない
    }
    public static void Close()
    {
        if (!IsOpen) return;
        IsOpen = false; ClosedAt = Time.realtimeSinceStartup;
        Debug.Log("[Notes] close");
        UiInputGate.LatchUntilRelease(); // 閉じた指を後ろ(タイトルの開始)へ通さない
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Cancel);
    }

    const float Bullet = 22f;
    static float S => UiScalePref.Menu(Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 720f, 1f, 2.6f));

    void OnGUI()
    {
        if (!IsOpen) return;
        GUI.depth = -31993; // タイトル(-31990)より手前、終了の確認(-31995)より後ろ
        int padLayer = PadNav.BeginLayer(31);
        float s = S;
        Matrix4x4 keep = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float w = Screen.width / s, h = Screen.height / s;
        UiKit.Fill(new Rect(0f, 0f, w, h), new Color(0.02f, 0.03f, 0.08f, 0.78f));
        Rect safe = StableSafeArea.Rect;
        float sl = safe.x / s, sr = (Screen.width - safe.xMax) / s, st = (Screen.height - safe.yMax) / s, sb = safe.y / s;
        float pw = Mathf.Min(760f, w - sl - sr - 24f), ph = Mathf.Min(700f, h - st - sb - 24f);
        var p = new Rect(sl + (w - sl - sr - pw) * 0.5f, st + (h - st - sb - ph) * 0.5f, pw, ph);
        OrnateUi.DrawPanel(p, 0.96f);
        LocGUI.Label(new Rect(p.x + 24f, p.y + 12f, p.width - 100f, 44f), LocNotes.Title, UiKit.Label(28f, TextAnchor.MiddleLeft, true, UiTheme.GoldText));
        if (UiIcons.CloseButton(new Rect(p.xMax - 66f, p.y + 12f, 48f, 42f), true)) Close();

        float x = p.x + 26f, full = p.width - 52f;
        float by = p.yMax - 22f - 50f;
        var view = new Rect(x, p.y + 66f, full, by - 12f - (p.y + 66f));
        var head = new GUIStyle(UiKit.Label(22f, TextAnchor.UpperLeft, true, UiTheme.GoldText)) { wordWrap = true };
        var sub = new GUIStyle(UiKit.Label(17f, TextAnchor.UpperLeft, true, UiTheme.TextSub)) { wordWrap = true };
        var body = new GUIStyle(UiKit.Label(19f, TextAnchor.UpperLeft, false, UiTheme.Text)) { wordWrap = true };
        float cw = view.width - 14f;
        // 中身の高さ(先に測る)
        var lines = new List<(string text, GUIStyle st, float indent, float gapAfter)>();
        foreach (var n in Notes)
        {
            lines.Add((Loc.F(LocNotes.VersionLine, n.code.ToString(), n.date), head, 0f, 6f));
            lines.Add((Loc.T(LocNotes.Changes), sub, 0f, 2f));
            foreach (var c in n.changes) lines.Add((Loc.T(c), body, Bullet, 9f));
            if (n.known != null && n.known.Length > 0)
            {
                lines.Add((Loc.T(LocNotes.Known), sub, 0f, 2f));
                foreach (var c in n.known) lines.Add((Loc.T(c), body, Bullet, 9f));
            }
            lines[lines.Count - 1] = (lines[lines.Count - 1].text, lines[lines.Count - 1].st, lines[lines.Count - 1].indent, 22f);
        }
        float contentH = 4f;
        var hs = new float[lines.Count];
        for (int i = 0; i < lines.Count; i++) { hs[i] = lines[i].st.CalcHeight(new GUIContent(LocGUI.RtlFix(Loc.Auto(lines[i].text), cw - lines[i].indent, lines[i].st)), cw - lines[i].indent); contentH += hs[i] + lines[i].gapAfter; }
        scroll.Begin(view, contentH, "patchnotes");
        float yy = 2f;
        for (int i = 0; i < lines.Count; i++)
        {
            // 箇条書き: 「・」は左の余白に置き、折り返した行の頭を本文の始まりにそろえる(エステル)
            if (lines[i].indent >= Bullet) GUI.Label(new Rect(lines[i].indent - 18f, yy, 18f, 26f), "・", lines[i].st);
            LocGUI.Label(new Rect(lines[i].indent, yy, cw - lines[i].indent, hs[i]), lines[i].text, lines[i].st);
            yy += hs[i] + lines[i].gapAfter;
        }
        scroll.End(view, contentH);

        if (UiKit.Button(new Rect(x + full - 200f, by, 200f, 50f), "閉じる", 19f, false, false)) Close();

        GUI.matrix = keep;
        GUI.Button(new Rect(0f, 0f, Screen.width, Screen.height), GUIContent.none, GUIStyle.none); // 背後へ通さない
        if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp || Event.current.type == EventType.MouseDrag) Event.current.Use();
        PadNav.EndLayer(padLayer);
    }
}

// お知らせの文言(2026-11-14)。訳は Tools/loc/keys_extra42.tsv(エステルの確認の後に反映)
public static class LocNotes
{
    public const string Title = "お知らせ";                         // ID 11010
    public const string VersionLine = "バージョン {0}（{1}）";          // ID 11011
    public const string Changes = "主な変更点";                       // ID 11012
    public const string Known = "既知の不具合";                       // ID 11013
    // 451 の文はエステルの差し替え案(2026-11-14)
    public const string N451a = "ガチャでカードの分類を選べるようになりました。選んだ分類のカードだけが出ます。"; // ID 11014
    public const string N451b = "タイトル画面の「お知らせ」から、更新内容を確認できるようになりました。"; // ID 11015
    public const string N451c = "新しいバージョンがあるときに、更新をご案内するようになりました。更新しなくても、そのまま遊べます。"; // ID 11016
    public const string N451d = "カード合成の説明と結果を、スクロールして最後まで読めるようにしました。"; // ID 11017
    public const string N451e = "タイトル画面の文字を大きく、見やすくしました。"; // ID 11018
    public const string N450a = "プロフィール、走る理由、常設チャレンジ、バッヂ、定型メッセージを追加しました。"; // ID 11019
    public const string N450b = "荒野街道の 10km 以降に、城下町の景色を追加しました。"; // ID 11020
    public const string N450c = "カードの分類（共通・荒野・洞窟・天空・ラスダン）を見直しました。"; // ID 11021
    public const string N450d = "95km から先の連戦の難しさを調整しました。"; // ID 11022
    public const string N450e = "中断したプレイを再開すると、撃破や BREAK の報酬が二重に加算されることがある不具合を修正しました。"; // ID 11023
    public const string ButtonNew = "NEW";
    public static readonly string[] All = { Title, VersionLine, Changes, Known, N451a, N451b, N451c, N451d, N451e, N450a, N450b, N450c, N450d, N450e };
}
