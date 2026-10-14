using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// データの引き継ぎの入出力と画面(2026-10-14)。設定の「データ引き継ぎ」から開く。
//  Android: 標準の保存先の選択(書き出し)/ファイルの選択(読み込み)= Plugins/Android/SaveTransferBridge.java
//  PC/Editor: persistentDataPath/SaveTransfer/ へ書き出し、そこにある一番新しいファイルを読み込む(PC 版の正式な選択画面は未実装)
//  読み込み: 選んだファイルを検査 → 確認(保存日時/MILE/カード/進行と、今の値)→「現在の進行を置き換える」でだけ適用。
//           キャンセル/検査で失敗 → 何も変えない。適用の後はシーンを読み直す(画面に残った古い値を書き戻さない)
public class SaveTransferUi : MonoBehaviour
{
    public static SaveTransferUi Instance { get; private set; }
    const string GoName = "[SaveTransfer]";

    enum St { Closed, Busy, Confirm, Message }
    St state = St.Closed;
    string title = "", body = "";
    bool bodyIsError;
    SaveTransfer.Checked pending;
    SaveTransfer.Summary now;
    bool nowHasSuspended;
    float busySince;
    string busyKind = "";

    public static bool IsOpen => Instance != null && Instance.state != St.Closed;
    public static string LastResult { get; private set; } = ""; // 確認用: export-ok / export-cancel / import-confirm / import-cancel / import-reject / import-ok / ...

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        var go = new GameObject(GoName);
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<SaveTransferUi>();
    }

    // ===================================================================== 入口
    public static void StartExport() { if (Instance != null) Instance.DoExport(); }
    public static void StartImport() { if (Instance != null) Instance.DoImport(); }

    void DoExport()
    {
        var c = SaveTransfer.Export();
        if (!c.ok) { ShowMessage("書き出せませんでした", c.error, true); LastResult = "export-error"; return; }
        string name = SaveTransfer.SuggestedFileName();
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            string src = System.IO.Path.Combine(Application.temporaryCachePath, "omm_export.json");
            System.IO.File.WriteAllText(src, c.text, new System.Text.UTF8Encoding(false));
            Busy("export");
            using (var bridge = new AndroidJavaClass("com.zero0084.omm.SaveTransferBridge"))
                bridge.CallStatic("exportFile", Activity(), name, src, GoName, nameof(OnAndroidResult));
        }
        catch (Exception e) { Debug.LogWarning("[SaveTransfer] export picker failed: " + e.Message); ShowMessage("書き出せませんでした", "保存先を選ぶ画面を開けませんでした。", true); LastResult = "export-error"; }
#else
        try
        {
            string dir = PcFolder();
            string path = System.IO.Path.Combine(dir, name);
            if (DevExportPath != "") path = DevExportPath;
            System.IO.File.WriteAllText(path, c.text, new System.Text.UTF8Encoding(false));
            LastExportPath = path;
            ShowMessage("書き出しました", Loc.F("保存先: {0}", path) + "\n" + Loc.T(SuspendedNote()), false);
            LastResult = "export-ok";
        }
        catch (Exception e) { ShowMessage("書き出せませんでした", Loc.T("ファイルを書けませんでした。") + " " + e.GetType().Name, true); LastResult = "export-error"; }
#endif
    }

    void DoImport()
    {
        if (!SaveTransfer.CanRunNow(out string why)) { ShowMessage("読み込めません", why, true); LastResult = "import-error"; return; }
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            string dst = ImportTempPath;
            try { if (System.IO.File.Exists(dst)) System.IO.File.Delete(dst); } catch { }
            Busy("import");
            using (var bridge = new AndroidJavaClass("com.zero0084.omm.SaveTransferBridge"))
                bridge.CallStatic("importFile", Activity(), dst, GoName, nameof(OnAndroidResult));
        }
        catch (Exception e) { Debug.LogWarning("[SaveTransfer] import picker failed: " + e.Message); ShowMessage("読み込めません", "ファイルを選ぶ画面を開けませんでした。", true); LastResult = "import-error"; }
#else
        string path = DevImportPath != "" ? DevImportPath : NewestPcFile();
        if (path == null) { ShowMessage("読み込めません", Loc.F("引き継ぎのファイルがありません。次のフォルダに置いてください: {0}", PcFolder()), true); LastResult = "import-error"; return; }
        string text;
        try { text = System.IO.File.ReadAllText(path, System.Text.Encoding.UTF8); }
        catch (Exception e) { ShowMessage("読み込めません", Loc.T("ファイルを読めませんでした。") + " " + e.GetType().Name, true); LastResult = "import-error"; return; }
        OnFileRead(text, System.IO.Path.GetFileName(path));
#endif
    }

    static string ImportTempPath => System.IO.Path.Combine(Application.temporaryCachePath, "omm_import.json");

#if UNITY_ANDROID && !UNITY_EDITOR
    static AndroidJavaObject Activity()
    {
        using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer")) return up.GetStatic<AndroidJavaObject>("currentActivity");
    }
#endif

    // Android の選択画面の結果(UnitySendMessage)
    public void OnAndroidResult(string msg)
    {
        Debug.Log("[SaveTransfer] picker result: " + msg);
        var p = (msg ?? "").Split(new[] { '|' }, 4);
        string kind = p.Length > 0 ? p[0] : "", status = p.Length > 1 ? p[1] : "";
        if (state != St.Busy || kind != busyKind) { Debug.Log("[SaveTransfer] result without a pending request - ignored"); return; } // 例: 選んでいる間にアプリが作り直された
        if (kind == "export")
        {
            if (status == "ok") { ShowMessage("書き出しました", Loc.F("ファイル: {0}", p.Length > 2 ? p[2] : "") + "\n" + Loc.T(SuspendedNote()), false); LastResult = "export-ok"; }
            else if (status == "cancel") { Close(); LastResult = "export-cancel"; }
            else { ShowMessage("書き出せませんでした", "選んだ保存先に書けませんでした。別の保存先を選んでください。", true); LastResult = "export-error"; }
            return;
        }
        if (status == "cancel") { Close(); LastResult = "import-cancel"; return; }
        if (status != "ok")
        {
            ShowMessage("読み込めません", p.Length > 2 && p[2] == "toolarge" ? "ファイルが大きすぎます。引き継ぎのファイルではありません(今のデータは変わっていません)。" : "ファイルを読めませんでした(今のデータは変わっていません)。", true);
            LastResult = "import-error";
            return;
        }
        string text = null;
        try { text = System.IO.File.ReadAllText(ImportTempPath, System.Text.Encoding.UTF8); } catch (Exception e) { Debug.LogWarning("[SaveTransfer] temp read failed: " + e.Message); }
        try { System.IO.File.Delete(ImportTempPath); } catch { }
        OnFileRead(text, p.Length > 2 ? p[2] : "");
    }

    void OnFileRead(string text, string fileName)
    {
        var c = SaveTransfer.Check(text);
        if (!c.ok)
        {
            Debug.LogWarning("[SaveTransfer] import rejected: " + c.detail);
            ShowMessage("読み込めません", c.error, true);
            LastResult = "import-reject";
            LastRejectDetail = c.detail;
            return;
        }
        pending = c;
        now = SaveTransfer.CurrentSummary();
        nowHasSuspended = RunCheckpoint.HasActiveRun;
        state = St.Confirm;
        LastResult = "import-confirm";
        Debug.Log($"[SaveTransfer] import file '{fileName}' passed the check ({c.data.itemCount} items) - waiting for the confirmation");
    }

    // 確認の画面の「置き換える」(QA からも)
    public static bool ConfirmReplace()
    {
        if (Instance == null || Instance.state != St.Confirm || Instance.pending == null) return false;
        var c = Instance.pending;
        Instance.pending = null;
        if (!SaveTransfer.Apply(c, out string err)) { Instance.ShowMessage("読み込めませんでした", err, true); LastResult = "import-error"; return false; }
        LastResult = "import-ok";
        Instance.state = St.Closed;
        SettingsPanel.CloseStatic();
        NoticeQueue.Toast(Loc.T("データの引き継ぎが完了しました"));
        LoadingScreen.ReloadActive("save import");
        return true;
    }

    public static void CancelConfirm()
    {
        if (Instance == null || Instance.state != St.Confirm) return;
        Instance.pending = null;
        Instance.Close();
        LastResult = "import-cancel";
        Debug.Log("[SaveTransfer] import cancelled by the player - nothing changed");
    }

    static string SuspendedNote() => RunCheckpoint.HasActiveRun ? "中断中のランは引き継ぎに含まれません。" : "別の端末やパソコン、外部ストレージなどにも保管しておくと、端末の故障に備えられます。";

    // ===================================================================== 状態
    void Busy(string kind) { state = St.Busy; busyKind = kind; busySince = Time.unscaledTime; }
    void ShowMessage(string t, string b, bool err) { state = St.Message; title = t; body = b; bodyIsError = err; }
    void Close() { state = St.Closed; pending = null; UiInputGate.LatchUntilRelease(); }

    // ===================================================================== PC
    public static string DevImportPath = "", DevExportPath = "";
    public static void DevResetResult() { LastResult = ""; LastRejectDetail = ""; }
    public static string LastExportPath { get; private set; } = "";
    public static string LastRejectDetail { get; private set; } = "";
    public static string PcFolder()
    {
        string root = Application.persistentDataPath;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (FileSaveStore.Active) root = FileSaveStore.Folder;
#endif
        string d = System.IO.Path.Combine(root, "SaveTransfer");
        System.IO.Directory.CreateDirectory(d);
        return d;
    }
    static string NewestPcFile()
    {
        try
        {
            string best = null; DateTime bt = DateTime.MinValue;
            foreach (var f in System.IO.Directory.GetFiles(PcFolder(), "*.json"))
            {
                var t = System.IO.File.GetLastWriteTimeUtc(f);
                if (t > bt) { bt = t; best = f; }
            }
            return best;
        }
        catch { return null; }
    }

    // ===================================================================== 描画
    void OnGUI()
    {
        if (state == St.Closed) return;
        GUI.depth = -2250; // 設定(-2000)・広告なしパス(-2200)より手前(2026-10-14: 同じ深さを避ける)
        int padLayer = PadNav.BeginLayer(12);
        float s = Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 720f, 1f, 2.6f);
        Matrix4x4 keep = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float w = Screen.width / s, h = Screen.height / s;
        UiKit.Fill(new Rect(0, 0, w, h), new Color(0.01f, 0.02f, 0.06f, 0.7f));
        float pw = Mathf.Min(640f, w - 24f), ph = state == St.Confirm ? Mathf.Min(h - 24f, 620f) : Mathf.Min(h - 24f, 340f);
        var panel = new Rect((w - pw) * 0.5f, (h - ph) * 0.5f, pw, ph);
        OrnateUi.DrawPanel(panel, 0.97f);
        float x = panel.x + 24f, y = panel.y + 16f, cw = pw - 48f;
        var wrap = new GUIStyle(UiKit.Label(17f, TextAnchor.UpperLeft, false, Color.white)) { wordWrap = true };
        if (state == St.Busy)
        {
            LocGUI.Label(new Rect(x, y, cw, 40f), busyKind == "export" ? "書き出し" : "読み込み", UiKit.Label(24f, TextAnchor.MiddleLeft, true, new Color(1f, 0.86f, 0.45f)));
            y += 52f;
            LocGUI.Label(new Rect(x, y, cw, 120f), busyKind == "export" ? "保存先を選んでください…" : "引き継ぎのファイルを選んでください…", wrap);
            // 選ぶ画面から戻らなかった時のために、少し待ったら閉じられる
            if (Time.unscaledTime - busySince > 2f && UiKit.Button(new Rect(panel.center.x - 100f, panel.yMax - 70f, 200f, 52f), "閉じる", 20f, false))
            { Close(); LastResult = busyKind + "-abandoned"; }
        }
        else if (state == St.Message)
        {
            LocGUI.Label(new Rect(x, y, cw, 40f), title, UiKit.Label(24f, TextAnchor.MiddleLeft, true, bodyIsError ? new Color(1f, 0.6f, 0.5f) : new Color(1f, 0.86f, 0.45f)));
            y += 52f;
            GUI.Label(new Rect(x, y, cw, panel.yMax - 90f - y), Loc.Auto(body), wrap);
            if (UiKit.Button(new Rect(panel.center.x - 100f, panel.yMax - 70f, 200f, 52f), "OK", 20f, true)) Close();
        }
        else if (state == St.Confirm && pending != null)
        {
            var f = pending.data; var fs = f.summary;
            LocGUI.Label(new Rect(x, y, cw, 40f), "引き継ぎファイルの読み込み", UiKit.Label(24f, TextAnchor.MiddleLeft, true, new Color(1f, 0.86f, 0.45f)));
            y += 46f;
            var small = new GUIStyle(UiKit.Label(15f, TextAnchor.MiddleLeft, false, new Color(0.8f, 0.85f, 0.95f)));
            GUI.Label(new Rect(x, y, cw, 24f), Loc.F("保存日時: {0}", SavedAt(f)) + "   " + Loc.F("アプリ: {0}", f.appVersion), small);
            y += 30f;
            // 表: 項目 / ファイル / 今の端末
            float c1 = cw * 0.34f, c2 = cw * 0.33f;
            var head = UiKit.Label(15f, TextAnchor.MiddleLeft, true, new Color(1f, 0.82f, 0.45f));
            LocGUI.Label(new Rect(x + c1, y, c2, 24f), "ファイル", head);
            LocGUI.Label(new Rect(x + c1 + c2, y, c2, 24f), "今の端末", head);
            y += 26f;
            y = Row(x, y, c1, c2, "所持MILE", fs.mile.ToString("N0"), now.mile.ToString("N0"));
            y = Row(x, y, c1, c2, "カード", Loc.F("{0}種類 / {1}枚", fs.cardKinds, fs.cardTotal), Loc.F("{0}種類 / {1}枚", now.cardKinds, now.cardTotal));
            y = Row(x, y, c1, c2, "デッキ", Loc.F("{0}枚", fs.deckCount), Loc.F("{0}枚", now.deckCount));
            y = Row(x, y, c1, c2, "解放したマップ", MapCount(fs), MapCount(now));
            y = Row(x, y, c1, c2, "解放したキャラ", CharCount(fs), CharCount(now));
            y = Row(x, y, c1, c2, "最高距離", Meters(fs.bestDistance), Meters(now.bestDistance));
            y = Row(x, y, c1, c2, "累計距離", Meters(fs.lifetime), Meters(now.lifetime));
            y = Row(x, y, c1, c2, "LAST CORRIDOR", fs.finalDungeon != 0 ? Loc.T("解放済み") : "-", now.finalDungeon != 0 ? Loc.T("解放済み") : "-");
            y += 8f;
            var warn = new GUIStyle(UiKit.Label(16f, TextAnchor.UpperLeft, true, new Color(1f, 0.65f, 0.5f))) { wordWrap = true };
            string msg = Loc.T("今の進行(MILE・カード・デッキ・解放・記録)は、このファイルの内容に置き換わります。合算はされません。");
            if (nowHasSuspended) msg += "\n" + Loc.T("この端末の中断中のランは破棄されます。");
            msg += "\n" + Loc.T("設定・広告なしパス・ランキングの登録は引き継がれません。");
            float mh = warn.CalcHeight(new GUIContent(msg), cw);
            GUI.Label(new Rect(x, y, cw, mh), msg, warn);
            float bw = (cw - 12f) * 0.5f, by = panel.yMax - 70f;
            if (UiKit.Button(new Rect(x, by, bw, 54f), "現在の進行を置き換える", 18f, true)) ConfirmReplace();
            if (UiKit.Button(new Rect(x + bw + 12f, by, bw, 54f), "キャンセル", 19f, false)) CancelConfirm();
        }
        // 後ろへ通さない
        GUI.Button(new Rect(0f, 0f, w, h), GUIContent.none, GUIStyle.none);
        if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp || Event.current.type == EventType.MouseDrag) Event.current.Use();
        GUI.matrix = keep;
        PadNav.EndLayer(padLayer);
    }

    static float Row(float x, float y, float c1, float c2, string label, string a, string b)
    {
        LocGUI.Label(new Rect(x, y, c1, 28f), label, UiKit.Label(16f, TextAnchor.MiddleLeft, false, new Color(0.85f, 0.88f, 0.95f)));
        GUI.Label(new Rect(x + c1, y, c2, 28f), a, UiKit.Label(17f, TextAnchor.MiddleLeft, true, Color.white));
        GUI.Label(new Rect(x + c1 + c2, y, c2, 28f), b, UiKit.Label(16f, TextAnchor.MiddleLeft, false, new Color(0.7f, 0.74f, 0.82f)));
        return y + 30f;
    }

    static string SavedAt(SaveTransfer.FileData f)
    {
        if (DateTime.TryParse(f.savedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime t))
            return t.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        return f.savedAtLocal;
    }
    // 荒野街道と黒剣士は最初から(一覧に入っていなくても数える)
    static string MapCount(SaveTransfer.Summary s)
    {
        var set = new HashSet<string>(s.stages.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) { UnlockRules.Wasteland };
        set.Remove(UnlockRules.Arena);
        return set.Count.ToString(CultureInfo.InvariantCulture);
    }
    static string CharCount(SaveTransfer.Summary s)
    {
        var set = new HashSet<string>(s.chars.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) { UnlockRules.StartCharacter };
        return $"{set.Count}/{CharacterDatabase.AllCharacters.Count}";
    }
    static string Meters(string v) => double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d.ToString("N0", CultureInfo.InvariantCulture) + "m" : v;
}
