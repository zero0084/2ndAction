using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 言語ごとの文字の形(フォント)(2026-10-07)。ゲームは文字のフォントを同梱せず、OS のフォントで描く(Unity の旧テキストの代替フォント)。
// Windows ではビルマ語/クメール語/ラオ語の文字が代替フォントで見つからず □ や空白になったので、その言語の時だけ
// OS に入っている専用のフォント(Myanmar Text / Khmer UI / Lao UI など)を IMGUI と uGUI の両方に使う。
// Android は端末のフォント(Noto)の代替に任せる(実機での確認は未)。
public class LocFonts : MonoBehaviour
{
    static readonly Dictionary<string, string[]> OsFonts = new Dictionary<string, string[]>
    {
        { "my", new[] { "Myanmar Text", "Noto Sans Myanmar", "Padauk" } },
        { "km", new[] { "Khmer UI", "Leelawadee UI", "Noto Sans Khmer" } },
        { "lo", new[] { "Lao UI", "Leelawadee UI", "Noto Sans Lao" } },
    };
    static Font current; static string currentLang;
    static readonly Dictionary<Text, Font> originals = new Dictionary<Text, Font>();
    static Font builtinSkinFont; static bool skinFontKnown;
    public static string ActiveFontName => current != null ? current.name : "";

    // 2026-10-09(依頼G): 言語の一覧で、選ぶ前の言語の名前も □ にならないように、その言語用のフォント(無ければ null = 既定)
    static readonly Dictionary<string, Font> perLang = new Dictionary<string, Font>();
    public static Font FontFor(string lang)
    {
        if (string.IsNullOrEmpty(lang) || Application.platform == RuntimePlatform.Android) return null;
        if (perLang.TryGetValue(lang, out var f)) return f;
        f = null;
        if (OsFonts.TryGetValue(lang, out var names))
        {
            var installed = new HashSet<string>(Font.GetOSInstalledFontNames());
            foreach (var n in names) if (installed.Contains(n)) { f = Font.CreateDynamicFontFromOSFont(n, 32); break; }
        }
        perLang[lang] = f;
        return f;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("[LocFonts]");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideInHierarchy;
        go.AddComponent<LocFonts>();
        Loc.Changed += () => { Refresh(); ApplyUgui(); };
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => { originals.Clear(); ApplyUgui(); };
        Refresh();
    }

    static void Refresh()
    {
        string lang = Loc.Current;
        if (lang == currentLang) return;
        currentLang = lang;
        current = null;
        if (Application.platform == RuntimePlatform.Android) return; // 端末のフォントの代替に任せる
        if (!OsFonts.TryGetValue(lang, out var names)) return;
        var installed = new HashSet<string>(Font.GetOSInstalledFontNames());
        foreach (var n in names)
            if (installed.Contains(n)) { current = Font.CreateDynamicFontFromOSFont(n, 32); break; }
        Debug.Log($"[LocFonts] {lang}: {(current != null ? current.name : "OS fallback")}");
    }

    static void ApplyUgui()
    {
        foreach (var t in Resources.FindObjectsOfTypeAll<Text>())
        {
            if (t == null || !t.gameObject.scene.IsValid()) continue;
            if (!originals.ContainsKey(t)) originals[t] = t.font;
            var want = current != null ? current : originals[t];
            if (t.font != want) t.font = want;
        }
    }

    void OnGUI()
    {
        // IMGUI: 既定のスキンのフォントを差し替える(作り直す GUIStyle は font 未指定ならスキンのフォントを使う)
        GUI.depth = 10000;
        if (!skinFontKnown) { builtinSkinFont = GUI.skin.font; skinFontKnown = true; }
        var want = current != null ? current : builtinSkinFont;
        if (GUI.skin.font != want) GUI.skin.font = want;
    }

    float nextScan;
    void Update()
    {
        // 後から作られる uGUI の文(カード等)にも当てる(その言語の時だけ、軽く1秒ごと)
        if (current == null || Time.unscaledTime < nextScan) return;
        nextScan = Time.unscaledTime + 1f;
        ApplyUgui();
    }
}
