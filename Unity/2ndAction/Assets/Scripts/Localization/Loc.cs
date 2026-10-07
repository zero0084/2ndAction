using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

// 言語の切り替えと翻訳(2026-10-07)。30言語(中国語は簡体字/繁体字を別に数える)。
//  ・翻訳元は日本語(以前から英語だった UI ラベルは、その英語を元の文として各言語の表に持つ)。表: Resources/Localization/<code>.json
//  ・引くのは描画の入口(UiKit.Button / DrawStyledButton / LocGUI.Label / uGUI の Text)で Loc.Auto(表示する文)。
//    完全一致 → 数値などを差し込んだ文は型({0}..)の一致。結果はキャッシュ(毎フレームの描画でも軽い)。
//  ・訳が無い時: 英語へ(英語も無ければ元の文)。開発版は「欠けている訳」「訳されずに出た日本語」を記録する(LocDebug)。
//  ・初回は端末の言語、対応外は英語。ユーザーが選んだ言語は保存(設定=両方のデータで共有)して以後優先。再起動なしで切り替わる。
//  ・アラビア語/ペルシャ語: 右から左。文字のつながり(語頭/語中/語尾の形)を作ってから並べ替える(ArabicShaper)。
public static class Loc
{
    public struct Lang
    {
        public string code, native, english; public bool rtl;
        public Lang(string c, string n, string e, bool r = false) { code = c; native = n; english = e; rtl = r; }
    }

    public static readonly Lang[] Languages =
    {
        new Lang("ja", "日本語", "Japanese"), new Lang("en", "English", "English"),
        new Lang("zh-Hans", "简体中文", "Chinese (Simplified)"), new Lang("zh-Hant", "繁體中文", "Chinese (Traditional)"),
        new Lang("ko", "한국어", "Korean"), new Lang("id", "Bahasa Indonesia", "Indonesian"), new Lang("ms", "Bahasa Melayu", "Malay"),
        new Lang("vi", "Tiếng Việt", "Vietnamese"), new Lang("th", "ไทย", "Thai"), new Lang("fil", "Filipino", "Filipino"),
        new Lang("my", "မြန်မာ", "Burmese"), new Lang("km", "ខ្មែរ", "Khmer"), new Lang("lo", "ລາວ", "Lao"),
        new Lang("hi", "हिन्दी", "Hindi"), new Lang("bn", "বাংলা", "Bengali"), new Lang("ta", "தமிழ்", "Tamil"),
        new Lang("fr", "Français", "French"), new Lang("de", "Deutsch", "German"), new Lang("es", "Español", "Spanish"),
        new Lang("pt-BR", "Português (Brasil)", "Portuguese (Brazil)"), new Lang("it", "Italiano", "Italian"),
        new Lang("pl", "Polski", "Polish"), new Lang("uk", "Українська", "Ukrainian"), new Lang("ru", "Русский", "Russian"),
        new Lang("tr", "Türkçe", "Turkish"), new Lang("cs", "Čeština", "Czech"), new Lang("ro", "Română", "Romanian"),
        new Lang("ar", "العربية", "Arabic", true), new Lang("fa", "فارسی", "Persian", true), new Lang("sw", "Kiswahili", "Swahili"),
    };

    public const string PrefKey = "Language"; // 設定(SaveKeys の Settings)。無ければ端末の言語
    static string current;
    public static event Action Changed;
    public static int Version { get; private set; } // 切り替えるたびに +1(キャッシュの目印)

    public static string Current { get { if (current == null) Init(); return current; } }
    public static bool IsRtl { get { var l = Find(Current); return l.rtl; } }
    public static bool IsJapanese => Current == "ja";
    public static Lang Find(string code) { foreach (var l in Languages) if (l.code == code) return l; return Languages[1]; }
    public static bool Supported(string code) { foreach (var l in Languages) if (l.code == code) return true; return false; }
    public static bool UserChose => SaveStore.HasKey(PrefKey);

    static void Init()
    {
        string saved = SaveStore.GetString(PrefKey, "");
        current = Supported(saved) ? saved : DetectDevice();
        LoadTables();
    }

    // 端末の言語(対応外は英語)
    public static string DetectDevice()
    {
        string tag = "";
        try
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var loc = new AndroidJavaClass("java.util.Locale"))
            using (var def = loc.CallStatic<AndroidJavaObject>("getDefault"))
                tag = def.Call<string>("toLanguageTag");
#else
            tag = System.Globalization.CultureInfo.CurrentUICulture.Name;
#endif
        }
        catch { }
        if (string.IsNullOrEmpty(tag)) tag = SystemLanguageTag(Application.systemLanguage);
        return MapTag(tag);
    }

    public static string MapTag(string tag)
    {
        if (string.IsNullOrEmpty(tag)) return "en";
        string t = tag.Replace('_', '-').ToLowerInvariant();
        if (t.StartsWith("zh"))
        {
            if (t.Contains("hant") || t.EndsWith("-tw") || t.EndsWith("-hk") || t.EndsWith("-mo")) return "zh-Hant";
            return "zh-Hans";
        }
        if (t.StartsWith("pt")) return "pt-BR";
        if (t.StartsWith("tl") || t.StartsWith("fil")) return "fil";
        if (t.StartsWith("in-") || t == "in") return "id";
        string b = t.Split('-')[0];
        foreach (var l in Languages) if (l.code == b) return l.code;
        return "en";
    }

    static string SystemLanguageTag(SystemLanguage s)
    {
        switch (s)
        {
            case SystemLanguage.Japanese: return "ja"; case SystemLanguage.ChineseSimplified: return "zh-Hans"; case SystemLanguage.ChineseTraditional: return "zh-Hant";
            case SystemLanguage.Chinese: return "zh"; case SystemLanguage.Korean: return "ko"; case SystemLanguage.Indonesian: return "id"; case SystemLanguage.Vietnamese: return "vi";
            case SystemLanguage.Thai: return "th"; case SystemLanguage.French: return "fr"; case SystemLanguage.German: return "de"; case SystemLanguage.Spanish: return "es";
            case SystemLanguage.Portuguese: return "pt"; case SystemLanguage.Italian: return "it"; case SystemLanguage.Polish: return "pl"; case SystemLanguage.Ukrainian: return "uk";
            case SystemLanguage.Russian: return "ru"; case SystemLanguage.Turkish: return "tr"; case SystemLanguage.Czech: return "cs"; case SystemLanguage.Romanian: return "ro";
            case SystemLanguage.Arabic: return "ar";
        }
        return "en";
    }

    // ユーザーが選んだ(保存する)。表示中の画面にもすぐ反映
    public static void Set(string code, bool save = true)
    {
        if (!Supported(code)) return;
        if (save) { SaveStore.SetString(PrefKey, code); SaveStore.Save(); }
        if (current == code) return;
        current = code;
        LoadTables();
        Debug.Log($"[Loc] language -> {code}");
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------- 表
    [Serializable] class Table { public List<string> k = new List<string>(); public List<string> v = new List<string>(); }
    static Dictionary<string, string> table, english;
    static readonly HashSet<string> knownKeys = new HashSet<string>();
    struct Tpl { public Regex re; public string key; }
    static List<Tpl> templates;
    static readonly Dictionary<string, string> cache = new Dictionary<string, string>();
    static readonly Regex Jp = new Regex(@"[぀-ヿ㐀-鿿]");
    static readonly Regex Ph = new Regex(@"\{(\d+)\}");
    static readonly Dictionary<string, string> Alias = new Dictionary<string, string> { { "BACK", "戻る" } };
    static readonly Regex Kana = new Regex(@"[぀-ヺー-ヿ]");
    static readonly Regex Deco = new Regex(@"^([«»▶◀▲▼←→≡・\s]*)(.*?)([«»▶◀▲▼←→≡・\s]*)$", RegexOptions.Singleline);
    static readonly Regex UiWord = new Regex(@"^[A-Z][A-Z !?'&-]{2,}[A-Z!?]$");

    static Dictionary<string, string> LoadTable(string code)
    {
        var d = new Dictionary<string, string>();
        var ta = Resources.Load<TextAsset>("Localization/" + code);
        if (ta == null) return d;
        try
        {
            var t = JsonUtility.FromJson<Table>(ta.text);
            for (int i = 0; i < t.k.Count && i < t.v.Count; i++) if (!string.IsNullOrEmpty(t.k[i]) && t.v[i] != null) d[t.k[i]] = t.v[i];
        }
        catch (Exception e) { Debug.LogWarning($"[Loc] table {code} broken: {e.Message}"); }
        return d;
    }

    static void LoadTables()
    {
        english = LoadTable("en");
        table = current == "en" ? english : LoadTable(current);
        knownKeys.Clear();
        foreach (var k in english.Keys) knownKeys.Add(k);
        templates = new List<Tpl>();
        foreach (var k in knownKeys)
        {
            if (!Ph.IsMatch(k)) continue;
            string pat = "^" + Ph.Replace(Regex.Escape(k).Replace(@"\{", "{").Replace(@"\}", "}"), "(.*?)") + "$";
            try { templates.Add(new Tpl { re = new Regex(pat, RegexOptions.Singleline | RegexOptions.CultureInvariant), key = k }); } catch { }
        }
        templates.Sort((a, b) => b.key.Length.CompareTo(a.key.Length)); // 長い(具体的な)型を先に
        cache.Clear();
        Version++;
        LocDebug.Reset();
    }

    public static string EnglishOf(string key) { if (current == null) Init(); return english.TryGetValue(key, out var v) ? v : key; }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 自動テスト用: 今の言語の表から1つ消す(英語へのフォールバックの確認)
    public static void DebugDropFromCurrent(string key) { if (current == null) Init(); if (table != english) table.Remove(key); cache.Clear(); }
#endif

    public static int KeyCount { get { if (current == null) Init(); return knownKeys.Count; } }
    public static int TableCount { get { if (current == null) Init(); return table.Count; } }
    public static bool HasTranslation(string code, string key) => LoadTable(code).ContainsKey(key);

    // 元の文(キー)を今の言語へ。無ければ英語、それも無ければ元の文
    public static string T(string key)
    {
        if (string.IsNullOrEmpty(key)) return key;
        if (current == null) Init();
        return Display(Raw(key));
    }

    public static string F(string key, params object[] args)
    {
        string s = Raw(key);
        for (int i = 0; i < args.Length; i++) s = s.Replace("{" + i + "}", args[i] != null ? args[i].ToString() : "");
        return Display(s);
    }

    static string Raw(string key)
    {
        if (current == null) Init();
        if (table.TryGetValue(key, out string v) && !string.IsNullOrEmpty(v)) return v;
        if (current == "ja" && Jp.IsMatch(key)) return key;           // 日本語が元の文
        if (knownKeys.Contains(key) && current != "en") LocDebug.Missing(current, key);
        if (english.TryGetValue(key, out v) && !string.IsNullOrEmpty(v)) return v;
        return key;
    }

    // 表示する文(数値などを差し込み済み)をそのまま渡す。分からない文は手を付けない
    // 言語の名前(その言語自身の名前)は訳さない。右から左の名前は今の言語に関係なく並べ替えて見せる
    static Dictionary<string, string> nativeDisplay;
    public static string NativeName(Lang l) { if (nativeDisplay == null) BuildNative(); return nativeDisplay[l.native]; }
    static void BuildNative() { nativeDisplay = new Dictionary<string, string>(); foreach (var l in Languages) nativeDisplay[l.native] = l.rtl ? ArabicShaper.ForDisplay(l.native) : l.native; }

    public static string Auto(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        if (current == null) Init();
        if (nativeDisplay == null) BuildNative();
        if (nativeDisplay.TryGetValue(s, out string nat)) return nat;
        foreach (var v in nativeDisplay.Values) if (v == s) return s; // 並べ替え済みの名前
        if (cache.TryGetValue(s, out string hit)) return hit;
        string r = Lookup(s);
        if (cache.Count > 6000) cache.Clear();
        cache[s] = r;
        return r;
    }

    static string Lookup(string s)
    {
        if (knownKeys.Contains(s) || table.ContainsKey(s)) return Display(Raw(s));
        // 改行で区切った各行(複数の文を\nでつないだ物)
        if (s.IndexOf('\n') >= 0)
        {
            var parts = s.Split('\n');
            bool any = false;
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0) continue;
                string p = LookupLine(parts[i], out bool ok);
                if (ok) { parts[i] = p; any = true; }
            }
            if (any) return Display(string.Join("\n", parts));
        }
        string one = LookupLine(s, out bool found);
        if (found) return Display(one);
        if (current != "ja" && (current.StartsWith("zh") ? Kana.IsMatch(s) : Jp.IsMatch(s))) LocDebug.Untranslated(current, s); // 中国語は漢字が同じなので仮名だけで判定
        else if (current != "en" && UiWord.IsMatch(s)) LocDebug.Unknown(s); // 表に無い英語の UI ラベル(開発版で集めて訳を足す)
        return current == "ja" ? s : Display(s);
    }

    static string LookupLine(string s, out bool found)
    {
        found = true;
        if (knownKeys.Contains(s) || table.ContainsKey(s)) return Raw(s);
        // 前後の空白は表のキーに含めていない(行の頭/終わりの空白はそのまま残して中だけ訳す)
        string trimmed = s.Trim(' ');
        if (trimmed.Length > 0 && trimmed.Length != s.Length)
        {
            string inner = LookupLine(trimmed, out bool ok);
            if (ok) { int lead = s.Length - s.TrimStart(' ').Length, trail = s.Length - s.TrimEnd(' ').Length; return new string(' ', lead) + inner + new string(' ', trail); }
        }
        // 同じ意味の別表記(シーンに焼き込んだ英語のボタン等)は、訳のある元の文を使う
        if (Alias.TryGetValue(s, out string al)) return LookupLine(al, out found);
        // 飾りの記号(« ▶ など)や全体を囲む括弧を外して、中身だけ訳す
        var dm = Deco.Match(s);
        if (dm.Success && (dm.Groups[1].Length > 0 || dm.Groups[3].Length > 0) && dm.Groups[2].Length > 0)
        {
            string core = LookupLine(dm.Groups[2].Value, out bool okc);
            if (okc) return dm.Groups[1].Value + core + dm.Groups[3].Value;
        }
        if (s.Length > 2 && ((s[0] == '(' && s[s.Length - 1] == ')') || (s[0] == '（' && s[s.Length - 1] == '）')))
        {
            string core = LookupLine(s.Substring(1, s.Length - 2), out bool okp);
            if (okp) return "(" + core + ")";
        }
        foreach (var t in templates)
        {
            var m = t.re.Match(s);
            if (!m.Success) continue;
            string r = Raw(t.key);
            for (int g = 1; g < m.Groups.Count; g++) r = r.Replace("{" + (g - 1) + "}", Auto(m.Groups[g].Value)); // 差し込まれた語も訳す(カード名など)
            return r;
        }
        found = false;
        return s;
    }

    // 右から左の言語は表示用に並べ替える
    static string Display(string s) => IsRtl ? ArabicShaper.ForDisplay(s) : s;
}

// 開発版: 欠けている訳 / 訳されずに出た日本語 を集める(DEBUG のページとテストが読む)
public static class LocDebug
{
    public static readonly HashSet<string> MissingKeys = new HashSet<string>();
    public static readonly HashSet<string> UntranslatedShown = new HashSet<string>();
    public static readonly HashSet<string> UnknownLabels = new HashSet<string>();
    public static void Reset() { MissingKeys.Clear(); UntranslatedShown.Clear(); UnknownLabels.Clear(); }
    public static void Unknown(string s) { if (UnknownLabels.Count < 500) UnknownLabels.Add(s); }
    public static void Missing(string lang, string key)
    {
        if (MissingKeys.Add(key) && MissingKeys.Count <= 30) Debug.LogWarning($"[Loc] missing {lang}: {Short(key)}");
    }
    public static void Untranslated(string lang, string s)
    {
        if (UntranslatedShown.Add(s) && UntranslatedShown.Count <= 30 && Debug.isDebugBuild) Debug.Log($"[Loc] untranslated text shown ({lang}): {Short(s)}");
    }
    static string Short(string s) => (s.Length > 60 ? s.Substring(0, 60) + "…" : s).Replace("\n", "\\n");

    public static void WriteReport(string path)
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# language {Loc.Current} keys {Loc.KeyCount} table {Loc.TableCount}");
            sb.AppendLine($"## missing translations ({MissingKeys.Count})");
            foreach (var k in MissingKeys) sb.AppendLine(k.Replace("\n", "\\n"));
            sb.AppendLine($"## untranslated text shown ({UntranslatedShown.Count})");
            foreach (var k in UntranslatedShown) sb.AppendLine(k.Replace("\n", "\\n"));
            sb.AppendLine($"## English labels without a translation entry ({UnknownLabels.Count})");
            foreach (var k in UnknownLabels) sb.AppendLine(k);
            System.IO.File.WriteAllText(path, sb.ToString());
        }
        catch (Exception e) { Debug.LogWarning("[Loc] report: " + e.Message); }
    }
}

// IMGUI のラベル(文字列の物だけ訳す。GUIContent/画像はそのまま)
public static class LocGUI
{
    public static void Label(Rect r, string text) => GUI.Label(r, Loc.Auto(text));
    // 折り返さないラベルは、訳が長くて入らなければ文字を小さくして収める(元の60%まで)。折り返すラベルは高さに収まるよう縮める
    static readonly GUIContent tmp = new GUIContent();
    public static void Label(Rect r, string text, GUIStyle style)
    {
        string s = Loc.Auto(text);
        if (style == null || string.IsNullOrEmpty(s) || Loc.IsJapanese || style.fontSize <= 0 || Event.current == null || Event.current.type != EventType.Repaint) { GUI.Label(r, s, style); return; }
        int keep = style.fontSize;
        tmp.text = s;
        if (!style.wordWrap)
        {
            float w = style.CalcSize(tmp).x;
            if (w > r.width && r.width > 4f) style.fontSize = Mathf.Max(Mathf.RoundToInt(keep * 0.6f), Mathf.FloorToInt(keep * r.width / w));
        }
        else
        {
            float h = style.CalcHeight(tmp, r.width);
            if (h > r.height && r.height > 4f) style.fontSize = Mathf.Max(Mathf.RoundToInt(keep * 0.6f), Mathf.FloorToInt(keep * Mathf.Sqrt(r.height / h)));
        }
        GUI.Label(r, s, style);
        style.fontSize = keep;
    }
    public static void Label(Rect r, GUIContent c) => GUI.Label(r, c);
    public static void Label(Rect r, GUIContent c, GUIStyle style) => GUI.Label(r, c, style);
    public static void Label(Rect r, Texture t) => GUI.Label(r, t);
    public static void Label(Rect r, Texture t, GUIStyle style) => GUI.Label(r, t, style);
}
