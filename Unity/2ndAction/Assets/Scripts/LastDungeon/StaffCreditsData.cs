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
        [Tooltip("名前(大きな文字)。複数可")] public string[] names = new string[0];
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

    public static StaffCreditsData LoadOrDefault()
    {
        var d = Resources.Load<StaffCreditsData>("LastDungeon/StaffCredits");
        return d != null ? d : CreateDefault();
    }

    public static StaffCreditsData CreateDefault()
    {
        var d = CreateInstance<StaffCreditsData>();
        d.entries = new List<Entry>
        {
            new Entry { role = "", names = new[] { "ONE MORE MILE" }, layout = Layout.Title, nameHeight = 1.5f, gapAfter = 34f },
            new Entry { role = "PROGRAMMING", names = new[] { "IRENE" }, layout = Layout.Standing, nameHeight = 1.45f, gapAfter = 30f },
            new Entry { role = "GAME DESIGN", names = new[] { "ESTELLE" }, layout = Layout.Arch, nameHeight = 1.3f, gapAfter = 30f },
            new Entry { role = "ART / DESIGN", names = new[] { "ALICIA" }, layout = Layout.Stairs, nameHeight = 1.2f, gapAfter = 32f },
            new Entry { role = "SPECIAL THANKS", names = new[] { "YOU" }, layout = Layout.Standing, nameHeight = 1.5f, breakable = true, gapAfter = 30f },
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
            case '?': return "q";
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
