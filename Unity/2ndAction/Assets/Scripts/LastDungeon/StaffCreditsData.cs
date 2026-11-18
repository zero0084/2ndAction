using System;
using System.Collections.Generic;
using UnityEngine;

// ラストダンジョンのエンドロール(2026-09-30)の中身。Resources/LastDungeon/StaffCredits.asset を編集すれば
// 役職/名前/並び方/壁の文言/最後の問いかけを変えられる(正式な表記は後で差し替える前提の仮の値)。
// 文字はResources/LastDungeon/Glyphs の1文字1枚の絵(A〜Z、0〜9、? ! / & . - ' , : +)。無い文字は空白扱い。
[CreateAssetMenu(menuName = "OneMoreMile/Staff Credits", fileName = "StaffCredits")]
public class StaffCreditsData : ScriptableObject
{
    public enum Layout
    {
        Title,      // 大きな題字(宙に浮く。乗れる)
        Standing,   // 名前の文字が道の上に立っている(乗れる/飛び越える/横切る)
        Arch,       // 名前の文字が頭上に浮いている(文字の隙間の下を走る/跳んで乗る)
        Stairs,     // 名前の文字が階段状に浮いている(文字から文字へ跳び移る)
    }

    [Serializable]
    public class Entry
    {
        [Tooltip("役職(小さめの文字で宙に浮く)。空なら出さない")] public string role = "";
        [Tooltip("役職の下に添える日本語の説明(足場の文字とは別の、ゲーム世界の中の文字)。空なら出さない(2026-10-13 第3弾)")] public string subtitle = "";
        [Tooltip("名前(大きな文字)。複数可。英数字以外(日本語など)を含む名前は、乗れる銘板+刻んだ文字で表記どおりに出す")] public string[] names = new string[0];
        [Tooltip("名前の一覧のテキスト(Resources の中のパス、拡張子なし。1行1名、# で始まる行は無視)。指定した時は names の代わりにこれを使う(2026-11-18 PLAYTESTERS: Resources/Credits/Playtesters.txt)")] public string namesFile = "";
        public Layout layout = Layout.Standing;
        [Tooltip("名前の文字の高さ(m)")] public float nameHeight = 1.7f;
        [Tooltip("攻撃で砕ける(false=揺れて光るだけ)")] public bool breakable;
        [Tooltip("この項目の後、次の項目までの距離(m)")] public float gapAfter = 30f;
    }

    public List<Entry> entries = new List<Entry>();
    [Header("最後の壁")]
    [Tooltip("石板に刻まれた文字。攻撃して壊すと先へ進める")] public string wallText = "THANK YOU FOR PLAYING";
    [Tooltip("石板の耐久(攻撃の回数。攻撃力は関係なし)")] public int wallHp = 6;
    [Header("終わり")]
    public string endText = "END";
    [Header("ONE MORE MILE?")]
    public string question = "ONE MORE MILE?";
    public string yesText = "YES";
    public string noText = "NO";
    [Tooltip("YES/NOの耐久(攻撃の回数。攻撃力は関係なし。最後の一撃を入れた方が答え)")] public int choiceHp = 8;

    // 2026-11-18: 名前の一覧のテキスト(namesFile)を読み込んだ写しを返す(元のアセットは書き換えない)。設定のクレジットは毎フレーム呼ぶので1回だけ作る
    static StaffCreditsData cachedResolved;
    public static StaffCreditsData LoadOrDefault()
    {
        if (cachedResolved != null) return cachedResolved;
        var src = Resources.Load<StaffCreditsData>("LastDungeon/StaffCredits");
        var d = src != null ? Instantiate(src) : CreateDefault();
        d.hideFlags = HideFlags.DontSave;
        foreach (var e in d.entries)
            if (e != null && !string.IsNullOrEmpty(e.namesFile)) e.names = ReadNames(e.namesFile);
        cachedResolved = d;
        return d;
    }
    public static void ReloadForTests() { cachedResolved = null; }

    public static string[] ReadNames(string resourcePath)
    {
        var ta = Resources.Load<TextAsset>(resourcePath);
        if (ta == null) { Debug.LogWarning("[Credits] names file not found: Resources/" + resourcePath); return new string[0]; }
        var list = new List<string>();
        foreach (var raw in ta.text.Split('\n'))
        {
            string s = raw.Trim().Trim('﻿');
            if (s.Length == 0 || s.StartsWith("#")) continue;
            list.Add(s);
        }
        return list.ToArray();
    }

    public static StaffCreditsData CreateDefault()
    {
        var d = CreateInstance<StaffCreditsData>();
        d.entries = new List<Entry>
        {
            new Entry { role = "", names = new[] { "ONE MORE MILE" }, layout = Layout.Title, nameHeight = 1.5f, gapAfter = 34f },
            new Entry { role = "CREATED BY", names = new[] { "Majiru Tabibito" }, layout = Layout.Standing, nameHeight = 1.1f, gapAfter = 30f },
            new Entry { role = "AI PROGRAMMING", subtitle = "AI assistant (programming)", names = new[] { "Irene" }, layout = Layout.Standing, nameHeight = 1.45f, gapAfter = 30f },
            new Entry { role = "AI GAME DESIGN", subtitle = "AI assistant (game design)", names = new[] { "Estelle" }, layout = Layout.Arch, nameHeight = 1.3f, gapAfter = 30f },
            new Entry { role = "AI ART / DESIGN", subtitle = "AI assistant (art and design)", names = new[] { "Alicia" }, layout = Layout.Stairs, nameHeight = 1.2f, gapAfter = 32f },
            new Entry { role = "SOUNDFONT", subtitle = "Music soundfont (MuseScore, MIT License)", names = new[] { "MS Basic" }, layout = Layout.Standing, nameHeight = 1.1f, gapAfter = 28f }, // 2026-10-23: MS Basic の謝辞(全文は設定の「ライセンス」)
            new Entry { role = "PLAYTESTERS", namesFile = "Credits/Playtesters", layout = Layout.Standing, nameHeight = 1.0f, gapAfter = 28f }, // 2026-11-18
            new Entry { role = "SPECIAL THANKS", names = new[] { "Pentatuttulu", "Luka" }, layout = Layout.Standing, nameHeight = 1.0f, gapAfter = 26f },
            new Entry { role = "SPECIAL THANKS", names = new[] { "You" }, layout = Layout.Standing, nameHeight = 1.5f, breakable = true, gapAfter = 30f },
        };
        return d;
    }
}

// 巨大文字の絵(1文字1枚。キャップハイト=1ワールド単位、ピボット=下端中央、ベースラインは下端から縁取りぶん上)。
public static class GlyphFont
{
    static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
    public const float SpaceWidth = 0.42f;   // 空白の幅(キャップハイト比)
    public const float Tracking = 0.02f;     // 文字間(キャップハイト比)
    public const float BaselineLift = 0.087f; // ピボットからベースラインまで(キャップハイト比)

    static string FileName(char c)
    {
        switch (c)
        {
            case '?': return "qmark"; // 2026-10-13 第3弾: 以前の "q" は Windows で glyph_Q と同じファイル名になり、Q の絵が ? で上書きされていた
            case '!': return "ex";
            case '/': return "slash";
            case '&': return "amp";
            case '.': return "dot";
            case '-': return "dash";
            case '\'': return "apos";
            case ',': return "comma";
            case ':': return "colon";
            case '+': return "plus";
            default: return char.ToUpperInvariant(c).ToString();
        }
    }

    public static Sprite Get(char c, int crackStage = 0)
    {
        if (c == ' ') return null;
        string key = "glyph_" + FileName(c) + (crackStage > 0 ? "_c" + crackStage : "");
        if (cache.TryGetValue(key, out var s) && s != null) return s;
        s = Resources.Load<Sprite>("LastDungeon/Glyphs/" + key);
        if (s == null && crackStage > 0) return Get(c, crackStage - 1);
        cache[key] = s;
        return s;
    }

    // 足場の文字だけで書けるか(空白以外のすべての文字に絵がある)。書けない名前(日本語など)は銘板+刻んだ文字にする
    public static bool CanRender(string text)
    {
        if (string.IsNullOrEmpty(text)) return true;
        foreach (char c in text) if (c != ' ' && Get(c) == null) return false;
        return true;
    }

    // 文字列の幅(キャップハイト=1とした単位)
    public static float Measure(string text)
    {
        float w = 0f;
        foreach (char c in text)
        {
            var sp = Get(c);
            w += sp != null ? sp.bounds.size.x + Tracking : SpaceWidth;
        }
        return Mathf.Max(0f, w - Tracking);
    }
}
