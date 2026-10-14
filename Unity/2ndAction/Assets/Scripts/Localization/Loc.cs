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
    static void BuildNative() { nativeDisplay = new Dictionary<string, string>(); foreach (var l in Languages) nativeDisplay[l.native] = l.rtl ? ArabicShaper.ForDisplay(l.native) : IndicShaper.Handles(l.code) ? IndicShaper.ForDisplay(l.native, l.code) : l.native; }

    public static string Auto(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        if (current == null) Init();
        if (nativeDisplay == null) BuildNative();
        if (nativeDisplay.TryGetValue(s, out string nat)) return nat;
        foreach (var v in nativeDisplay.Values) if (v == s) return s; // 並べ替え済みの名前
        if (cache.TryGetValue(s, out string hit)) return hit;
        string r = Lookup(s);
        if (current != "ja" && (current.StartsWith("zh") ? Kana.IsMatch(r) : Jp.IsMatch(r))) LocDebug.ShownJapanese(current, s, r); // 2026-10-14: 画面に出る文に日本語が残った(一部だけ訳された物も)
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
        // 表に無い文(数値/英語/すでに表示用に並べ替えた文をつないだ物)は、並べ替えを二重に掛けない(右から左の言語は今までどおり)
        return current == "ja" || IndicShaper.Handles(current) ? s : Display(s);
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
            bool leftJp = false;
            for (int g = 1; g < m.Groups.Count; g++)
            {
                string part = Auto(m.Groups[g].Value); // 差し込まれた語も訳す(カード名など)
                // 2026-10-14: 差し込みの部分が訳されないまま日本語で残る時は、この型を使わない
                //  (「0回分」が「{0}分」に当たって「0回 min」と意味の違う文になっていた。訳が無いと分かる方がよい)
                if (current != "ja" && (current.StartsWith("zh") ? Kana.IsMatch(part) : Jp.IsMatch(part))) { leftJp = true; break; }
                r = r.Replace("{" + (g - 1) + "}", part);
            }
            if (leftJp) continue;
            return r;
        }
        found = false;
        return s;
    }

    // 右から左の言語は表示用に並べ替える
    // 2026-10-10: ヒンディー語/ベンガル語/タミル語/ビルマ語/クメール語は、前に書く母音記号を見た目の順へ(IndicShaper)
    static string Display(string s) => IsRtl ? ArabicShaper.ForDisplay(s) : IndicShaper.Handles(current) ? IndicShaper.ForDisplay(s, current) : s;
}

// 開発版: 欠けている訳 / 訳されずに出た日本語 を集める(DEBUG のページとテストが読む)
public static class LocDebug
{
    public static readonly HashSet<string> MissingKeys = new HashSet<string>();
    public static readonly HashSet<string> UntranslatedShown = new HashSet<string>();
    public static readonly HashSet<string> UnknownLabels = new HashSet<string>();
    public static readonly HashSet<string> JapaneseShown = new HashSet<string>(); // 2026-10-14: 表示する文に日本語が残った物(元の文 → 出た文)
    public static void Reset() { MissingKeys.Clear(); UntranslatedShown.Clear(); UnknownLabels.Clear(); JapaneseShown.Clear(); }
    public static void ShownJapanese(string lang, string src, string shown) { if (JapaneseShown.Count < 500) JapaneseShown.Add(src == shown ? src : src + "  =>  " + shown); }
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
// 2026-10-13(文字の見切れの見直し): 日本語も含め、入らない時だけ次の順で収める(入る時は何もしない)。
//  1) 枠が画面の外へはみ出していたら、見える範囲を使える幅とする(縦画面で「高さ×k」の枠が画面より広い等)
//  2) 1行の文: 元の大きさの 80% まで縮める → まだ入らなければ 90% の大きさで折り返す(枠の上下へ少し広げてよい、3行まで)
//     → それでも入らなければ折り返したまま 60% まで縮める。文字が画面の外へ出る位置なら内側へ寄せる
//  3) 折り返す文: 高さに入るよう縮める(60% まで)
//  開発版は「収まらなかった/縮めた/折り返した」表示を記録する(TextFit。-qaTextFit の報告に出す)
public static class LocGUI
{
    public static void Label(Rect r, string text) => GUI.Label(r, Loc.Auto(text));
    static readonly GUIContent tmp = new GUIContent();
    const float Margin = 6f;

    // 今の GUI.matrix で見た画面の範囲(GUI 座標)
    static Rect GuiScreen()
    {
        var inv = GUI.matrix.inverse;
        Vector3 a = inv.MultiplyPoint3x4(Vector3.zero), b = inv.MultiplyPoint3x4(new Vector3(Screen.width, Screen.height, 0f));
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    public static void Label(Rect r, string text, GUIStyle style)
    {
        if (TextFit.Legacy) { LegacyLabel(r, text, style); return; }
        string s = Loc.Auto(text);
        if (style == null || string.IsNullOrEmpty(s) || style.fontSize <= 0 || Event.current == null || Event.current.type != EventType.Repaint) { GUI.Label(r, s, style); return; }
        int keep = style.fontSize;
        bool keepWrap = style.wordWrap;
        var keepClip = style.clipping;
        tmp.text = s;
        Rect scr = GuiScreen();
        float left = Mathf.Max(r.x, scr.x + Margin), right = Mathf.Min(r.xMax, scr.xMax - Margin);
        float avail = right - left;
        if (avail < 8f) { GUI.Label(r, s, style); return; }
        Rect dr = r;
        if (!keepWrap)
        {
            float w = style.CalcSize(tmp).x;
            if (w > avail)
            {
                int fit = Mathf.FloorToInt(keep * avail / w);
                bool oneWord = s.IndexOf(' ') < 0 && s.IndexOf((char)10) < 0 && !HasCjk(s); // 1語の英字(JACKPOT!! 等)は折り返さず縮める
                if (fit >= Mathf.RoundToInt(keep * 0.8f) || oneWord)
                {
                    fit = Mathf.Max(fit, Mathf.RoundToInt(keep * 0.6f));
                    style.fontSize = fit;
                    TextFit.Note(s, r, keep, style.fontSize, false, false);
                }
                else
                {
                    // 折り返す(9割の大きさ。枠の上下へ広げて3行まで)
                    style.wordWrap = true; style.clipping = TextClipping.Overflow;
                    style.fontSize = Mathf.RoundToInt(keep * 0.9f);
                    float hh = style.CalcHeight(tmp, avail);
                    float maxH = Mathf.Max(r.height, keep * 1.25f * 3f);
                    while (hh > maxH && style.fontSize > Mathf.RoundToInt(keep * 0.6f)) { style.fontSize--; hh = style.CalcHeight(tmp, avail); }
                    if (WidestWord(style, s) > avail) { style.fontSize = Mathf.Max(Mathf.RoundToInt(keep * 0.6f), Mathf.FloorToInt(style.fontSize * avail / WidestWord(style, s))); hh = style.CalcHeight(tmp, avail); }
                    bool still = hh > maxH * 1.05f || WidestWord(style, s) > avail + 1f;
                    float cy = r.center.y;
                    dr = new Rect(left, cy - Mathf.Max(r.height, hh) * 0.5f, avail, Mathf.Max(r.height, hh));
                    TextFit.Note(s, r, keep, style.fontSize, true, still);
                }
            }
            if (!style.wordWrap)
            {
                // 文字の範囲が画面の外に出る位置なら内側へ寄せる
                float tw = style.CalcSize(tmp).x;
                float tx;
                switch (style.alignment)
                {
                    case TextAnchor.UpperLeft: case TextAnchor.MiddleLeft: case TextAnchor.LowerLeft: tx = r.x + style.padding.left; break;
                    case TextAnchor.UpperRight: case TextAnchor.MiddleRight: case TextAnchor.LowerRight: tx = r.xMax - style.padding.right - tw; break;
                    default: tx = r.center.x - tw * 0.5f; break;
                }
                float shift = 0f;
                if (tx < scr.x + Margin) shift = scr.x + Margin - tx;
                else if (tx + tw > scr.xMax - Margin) shift = scr.xMax - Margin - (tx + tw);
                if (Mathf.Abs(shift) > 0.5f && tw <= scr.width - Margin * 2f) { dr.x += shift; TextFit.NoteShift(s, r, shift); }
            }
        }
        else
        {
            float aw = Mathf.Min(r.width, avail);
            float h = style.CalcHeight(tmp, aw);
            if (aw < r.width) dr = new Rect(left, r.y, aw, r.height);
            // 1行で収まっている文(枠の高さが文字の高さより少し低いだけ)は今まで通り描く。本当に2行以上へ折り返す時だけ手を入れる
            if (h > r.height && r.height > 4f && h > LineHeight(style) * 1.4f)
            {
                // 1行ぶんの高さの枠(見出し/お知らせ): 9割の大きさで上下へ広げて3行まで。それ以外(文章の枠)は高さに入るよう縮める
                bool oneLine = r.height < keep * 1.9f;
                // 見出し(1語の英字、または短い英字の見出し): 折り返すより先に、幅に合わせて縮める(80%まで。1語なら60%まで)
                bool oneWordW = s.IndexOf(' ') < 0 && !HasCjk(s);
                if (oneLine && !HasCjk(s))
                {
                    bool keepW = style.wordWrap; style.wordWrap = false;
                    float w1 = style.CalcSize(tmp).x;
                    style.wordWrap = keepW;
                    int fitW = Mathf.FloorToInt(keep * aw / Mathf.Max(1f, w1));
                    if (fitW >= Mathf.RoundToInt(keep * 0.8f) || oneWordW)
                    {
                        style.fontSize = Mathf.Max(fitW, Mathf.RoundToInt(keep * 0.6f));
                        style.wordWrap = false;
                        TextFit.Note(s, r, keep, style.fontSize, false, false);
                        GUI.Label(dr, s, style);
                        style.fontSize = keep; style.wordWrap = keepWrap; style.clipping = keepClip;
                        return;
                    }
                }
                if (oneLine)
                {
                    style.fontSize = Mathf.RoundToInt(keep * 0.9f);
                    float maxH = keep * 1.25f * 3f;
                    float hh = style.CalcHeight(tmp, aw);
                    while (hh > maxH && style.fontSize > Mathf.RoundToInt(keep * 0.6f)) { style.fontSize--; hh = style.CalcHeight(tmp, aw); }
                    style.clipping = TextClipping.Overflow;
                    dr = new Rect(dr.x, r.center.y - hh * 0.5f, dr.width, hh);
                    TextFit.Note(s, r, keep, style.fontSize, true, hh > maxH * 1.05f);
                }
                else
                {
                    style.fontSize = Mathf.Max(Mathf.RoundToInt(keep * 0.6f), Mathf.FloorToInt(keep * Mathf.Sqrt(r.height / h)));
                    TextFit.Note(s, r, keep, style.fontSize, false, style.CalcHeight(tmp, aw) > r.height * 1.05f);
                }
            }
        }
        GUI.Label(dr, s, style);
        style.fontSize = keep; style.wordWrap = keepWrap; style.clipping = keepClip;
    }

    // 修正前の収め方(比較の撮影用。日本語は何もしない)
    static void LegacyLabel(Rect r, string text, GUIStyle style)
    {
        string s = Loc.Auto(text);
        if (style == null || string.IsNullOrEmpty(s) || Loc.IsJapanese || style.fontSize <= 0 || Event.current == null || Event.current.type != EventType.Repaint) { GUI.Label(r, s, style); return; }
        int keep = style.fontSize;
        tmp.text = s;
        if (!style.wordWrap) { float w = style.CalcSize(tmp).x; if (w > r.width && r.width > 4f) style.fontSize = Mathf.Max(Mathf.RoundToInt(keep * 0.6f), Mathf.FloorToInt(keep * r.width / w)); }
        else { float h = style.CalcHeight(tmp, r.width); if (h > r.height && r.height > 4f) style.fontSize = Mathf.Max(Mathf.RoundToInt(keep * 0.6f), Mathf.FloorToInt(keep * Mathf.Sqrt(r.height / h))); }
        GUI.Label(r, s, style);
        style.fontSize = keep;
    }

    static readonly GUIContent one = new GUIContent("Ag");
    static float LineHeight(GUIStyle st) { bool ww = st.wordWrap; st.wordWrap = false; float h = st.CalcSize(one).y; st.wordWrap = ww; return h; }
    static bool HasCjk(string s) { foreach (char ch in s) if (ch >= 0x2E80) return true; return false; }

    // いちばん長い「切れない塊」(空白で区切った語。日本語/中国語などは1文字ずつ切れるので数えない)の幅
    static readonly GUIContent wtmp = new GUIContent();
    static float WidestWord(GUIStyle st, string s)
    {
        float best = 0f;
        foreach (var part in s.Split(' ', (char)10))
        {
            if (part.Length == 0) continue;
            bool cjk = false;
            foreach (char ch in part) if (ch >= 0x2E80) { cjk = true; break; }
            if (cjk) continue;
            wtmp.text = part;
            bool ww = st.wordWrap; st.wordWrap = false;
            best = Mathf.Max(best, st.CalcSize(wtmp).x);
            st.wordWrap = ww;
        }
        return best;
    }

    public static void Label(Rect r, GUIContent c) => GUI.Label(r, c);
    public static void Label(Rect r, GUIContent c, GUIStyle style) => GUI.Label(r, c, style);
    public static void Label(Rect r, Texture t) => GUI.Label(r, t);
    public static void Label(Rect r, Texture t, GUIStyle style) => GUI.Label(r, t, style);
}
