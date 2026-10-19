using System;
using UnityEngine;

// Unity Gaming Services の環境の選び方(2026-10-14、マスター指定: 開発版と製品版で環境をはっきり分ける)。
//  ・製品版(Development Build でない): 必ず "production"。起動引数では変えられない。
//  ・開発版/Editor: 既定 "development"。-ugsEnv <名前> で別の開発用の環境(例 staging)へ変えられるが、"production" は選べない
//    (指定しても無視して development のまま。ログに残す)。
//  ・選んだ環境が Dashboard に無い/つながらない時は、その環境のまま「使えない」になる。production へ自動で切り替えることはしない
//    (OnlineServices は環境を1回だけ決めて初期化し、作り直さない)。
//  ・ランキングの送信は「今の環境」で作った送信待ちだけを送る(Leaderboard)。開発版 → production の送信は、この層と Leaderboard の両方で止める。
//  ・Auth / Lobby(Sessions)/ Relay / Leaderboards / Cloud Code はすべて UnityServices.InitializeAsync の環境の指定に従う
//    (このゲームはこの1か所でしか初期化しない)。どこまで環境ごとに分かれるかは Docs/UgsEnvironments_2026-10-14 に書いた(本物のサービスでは未確認の点あり)。
public static class UgsEnvironment
{
    public const string Production = "production", Development = "development";

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public const bool IsReleaseBuild = false;
#else
    public const bool IsReleaseBuild = true;
#endif
    public static string BuildKind => IsReleaseBuild ? "release" : "dev";

    static string name, source = "", ignored = "";
    public static string Name { get { if (name == null) Resolve(); return name; } }
    public static string Source { get { if (name == null) Resolve(); return source; } }   // どう決まったか(表示/ログ用)
    public static string Ignored { get { if (name == null) Resolve(); return ignored; } } // 無視した指定(あれば)
    public static bool IsProduction => Name == Production;

    static void Resolve()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        name = Development; source = "dev build default";
        var a = QaArgs.All; // 起動引数 + Android の qa_args.txt
        for (int i = 0; i < a.Length - 1; i++)
        {
            if (a[i] != "-ugsEnv") continue;
            string v = (a[i + 1] ?? "").Trim().ToLowerInvariant();
            if (v == Production) { ignored = "-ugsEnv production (a dev build never uses production)"; continue; }
            if (!ValidName(v)) { ignored = "-ugsEnv '" + a[i + 1] + "' (not a valid environment name)"; continue; }
            name = v; source = "-ugsEnv";
        }
        if (ignored != "") Debug.LogWarning("[ONLINE] ignored " + ignored + " -> environment " + name);
#else
        name = Production; source = "release build (fixed)";
#endif
        Debug.Log($"[ONLINE] UGS Environment = {name} ({BuildKind} build, {source})"); // 2026-10-20: 起動のたびに1回(秘密は含まない)
    }

    // UGS の環境の名前: 英小文字/数字/-/_、1〜30 文字
    static bool ValidName(string v)
    {
        if (string.IsNullOrEmpty(v) || v.Length > 30) return false;
        foreach (char c in v) if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_')) return false;
        return true;
    }

    // ランキングへ送ってよい環境か(製品版 + production、または 開発版 + production 以外)
    public static bool MaySubmit(string entryEnv, out string why)
    {
        why = "";
        if (!IsReleaseBuild && IsProduction) { why = "dev build on production (blocked)"; return false; }
        if (IsReleaseBuild && !IsProduction) { why = "release build not on production"; return false; }
        if (string.IsNullOrEmpty(entryEnv)) { why = "made before the environments were separated (kept, not sent)"; return false; }
        if (entryEnv != Name) { why = "made in the environment '" + entryEnv + "' (not sent to '" + Name + "')"; return false; }
        return true;
    }

    public static string Describe() => $"UGS environment '{Name}' ({BuildKind} build, {Source}){(Ignored != "" ? " ignored " + Ignored : "")}";
}

// 2026-10-20(マスター依頼: development の APK だと分かるように): 開発版だけ、ラン中以外の画面の左下に「DEV BUILD · UGS development」
public class UgsEnvironmentLabel : MonoBehaviour
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindAnyObjectByType<UgsEnvironmentLabel>() != null) return;
        var go = new GameObject("[UgsEnvironmentLabel]");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideInHierarchy;
        go.AddComponent<UgsEnvironmentLabel>();
    }

    GUIStyle style;
    void OnGUI()
    {
        var gm = GameManager.Instance;
        if (gm != null && gm.HasStarted && !gm.IsGameOver) return; // ラン中は出さない(HUD の邪魔をしない)
        GUI.depth = -1000;
        if (style == null) style = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
        float s = Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height) / 720f);
        style.fontSize = Mathf.RoundToInt(12 * s);
        var safe = Screen.safeArea;
        var r = new Rect(safe.xMin + 6 * s, Screen.height - safe.yMin - 22 * s, 250 * s, 18 * s);
        var old = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = UgsEnvironment.IsProduction ? new Color(1f, 0.5f, 0.4f) : new Color(0.55f, 1f, 0.7f);
        GUI.Label(new Rect(r.x + 5 * s, r.y, r.width - 5 * s, r.height), "DEV BUILD · UGS " + UgsEnvironment.Name, style);
        GUI.color = old;
    }
#endif
}
