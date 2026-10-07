using System.Collections.Generic;
using System.Text;

// アラビア語/ペルシャ語の表示(2026-10-07)。Unity の旧テキスト(IMGUI / uGUI Text)は文字のつながりも右から左の並びも
// やらないので、表示の前にここで作る:
//  ① 文字のつながり: 前後の文字につながるかで、語頭/語中/語尾/独立の形(表示形 U+FE70〜/U+FB50〜)へ置き換え。ラーム+アリフは合字
//  ② 並び: 1行ずつ、右から左の連なり(アラビア文字と、その間の空白/記号)と左から右の連なり(数字/ラテン文字)に分け、
//     連なりの順番を逆にして、右から左の連なりの中の文字も逆にする(数字や英語の語はそのままの向き)。括弧は左右を入れ替える。
// 限界: 自動の折り返し(wordWrap)は左から並べた後に行を切るので、長い文が折り返す時は行の順番が崩れることがある
//       (このゲームの文は短く、説明文には \n を入れて行を分けてある)。
public static class ArabicShaper
{
    // base: (isolated, final, initial, medial)。initial/medial が 0 の文字は右(前の文字)にしかつながらない
    static readonly Dictionary<char, char[]> Forms = new Dictionary<char, char[]>
    {
        { 'ء', new[] { 'ﺀ', 'ﺀ', '\0', '\0' } },
        { 'آ', new[] { 'ﺁ', 'ﺂ', '\0', '\0' } }, { 'أ', new[] { 'ﺃ', 'ﺄ', '\0', '\0' } },
        { 'ؤ', new[] { 'ﺅ', 'ﺆ', '\0', '\0' } }, { 'إ', new[] { 'ﺇ', 'ﺈ', '\0', '\0' } },
        { 'ئ', new[] { 'ﺉ', 'ﺊ', 'ﺋ', 'ﺌ' } }, { 'ا', new[] { 'ﺍ', 'ﺎ', '\0', '\0' } },
        { 'ب', new[] { 'ﺏ', 'ﺐ', 'ﺑ', 'ﺒ' } }, { 'ة', new[] { 'ﺓ', 'ﺔ', '\0', '\0' } },
        { 'ت', new[] { 'ﺕ', 'ﺖ', 'ﺗ', 'ﺘ' } }, { 'ث', new[] { 'ﺙ', 'ﺚ', 'ﺛ', 'ﺜ' } },
        { 'ج', new[] { 'ﺝ', 'ﺞ', 'ﺟ', 'ﺠ' } }, { 'ح', new[] { 'ﺡ', 'ﺢ', 'ﺣ', 'ﺤ' } },
        { 'خ', new[] { 'ﺥ', 'ﺦ', 'ﺧ', 'ﺨ' } }, { 'د', new[] { 'ﺩ', 'ﺪ', '\0', '\0' } },
        { 'ذ', new[] { 'ﺫ', 'ﺬ', '\0', '\0' } }, { 'ر', new[] { 'ﺭ', 'ﺮ', '\0', '\0' } },
        { 'ز', new[] { 'ﺯ', 'ﺰ', '\0', '\0' } }, { 'س', new[] { 'ﺱ', 'ﺲ', 'ﺳ', 'ﺴ' } },
        { 'ش', new[] { 'ﺵ', 'ﺶ', 'ﺷ', 'ﺸ' } }, { 'ص', new[] { 'ﺹ', 'ﺺ', 'ﺻ', 'ﺼ' } },
        { 'ض', new[] { 'ﺽ', 'ﺾ', 'ﺿ', 'ﻀ' } }, { 'ط', new[] { 'ﻁ', 'ﻂ', 'ﻃ', 'ﻄ' } },
        { 'ظ', new[] { 'ﻅ', 'ﻆ', 'ﻇ', 'ﻈ' } }, { 'ع', new[] { 'ﻉ', 'ﻊ', 'ﻋ', 'ﻌ' } },
        { 'غ', new[] { 'ﻍ', 'ﻎ', 'ﻏ', 'ﻐ' } }, { 'ف', new[] { 'ﻑ', 'ﻒ', 'ﻓ', 'ﻔ' } },
        { 'ق', new[] { 'ﻕ', 'ﻖ', 'ﻗ', 'ﻘ' } }, { 'ك', new[] { 'ﻙ', 'ﻚ', 'ﻛ', 'ﻜ' } },
        { 'ل', new[] { 'ﻝ', 'ﻞ', 'ﻟ', 'ﻠ' } }, { 'م', new[] { 'ﻡ', 'ﻢ', 'ﻣ', 'ﻤ' } },
        { 'ن', new[] { 'ﻥ', 'ﻦ', 'ﻧ', 'ﻨ' } }, { 'ه', new[] { 'ﻩ', 'ﻪ', 'ﻫ', 'ﻬ' } },
        { 'و', new[] { 'ﻭ', 'ﻮ', '\0', '\0' } }, { 'ى', new[] { 'ﻯ', 'ﻰ', '\0', '\0' } },
        { 'ي', new[] { 'ﻱ', 'ﻲ', 'ﻳ', 'ﻴ' } },
        // ペルシャ語
        { 'پ', new[] { 'ﭖ', 'ﭗ', 'ﭘ', 'ﭙ' } }, { 'چ', new[] { 'ﭺ', 'ﭻ', 'ﭼ', 'ﭽ' } },
        { 'ژ', new[] { 'ﮊ', 'ﮋ', '\0', '\0' } }, { 'گ', new[] { 'ﮒ', 'ﮓ', 'ﮔ', 'ﮕ' } },
        { 'ک', new[] { 'ﮎ', 'ﮏ', 'ﮐ', 'ﮑ' } }, { 'ی', new[] { 'ﯼ', 'ﯽ', 'ﯾ', 'ﯿ' } },
    };

    static bool IsMark(char c) => (c >= 'ً' && c <= 'ٟ') || c == 'ٰ';
    static bool JoinsBoth(char c) => c == 'ـ' || (Forms.TryGetValue(c, out var f) && f[2] != '\0');
    static bool JoinsRight(char c) => c == 'ـ' || Forms.ContainsKey(c);
    public static bool IsRtlChar(char c) => (c >= '֐' && c <= 'ࣿ') || (c >= 'יִ' && c <= '﷿') || (c >= 'ﹰ' && c <= '﻿');

    public static string Shape(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (!Forms.ContainsKey(c)) { if (c != '‌') sb.Append(c); continue; }
            char prev = Neighbor(s, i, -1), next = Neighbor(s, i, +1);
            // ラーム + アリフ類の合字
            if (c == 'ل' && (next == 'آ' || next == 'أ' || next == 'إ' || next == 'ا'))
            {
                bool joinPrev = JoinsBoth(prev);
                char lig = next == 'آ' ? (joinPrev ? 'ﻶ' : 'ﻵ') : next == 'أ' ? (joinPrev ? 'ﻸ' : 'ﻷ') : next == 'إ' ? (joinPrev ? 'ﻺ' : 'ﻹ') : (joinPrev ? 'ﻼ' : 'ﻻ');
                sb.Append(lig);
                i = SkipTo(s, i, next);
                continue;
            }
            var f = Forms[c];
            bool p = JoinsBoth(prev);                 // 前の文字が次(この文字)へつながる
            bool n = f[2] != '\0' && JoinsRight(next); // この文字が次へつながれる + 次の文字が前へつながる
            char o = p && n ? f[3] : p ? f[1] : n ? f[2] : f[0];
            sb.Append(o == '\0' ? f[0] : o);
        }
        return sb.ToString();
    }

    static char Neighbor(string s, int i, int dir)
    {
        for (int j = i + dir; j >= 0 && j < s.Length; j += dir)
        {
            if (IsMark(s[j])) continue;
            if (s[j] == '‌') return ' ';
            return s[j];
        }
        return ' ';
    }
    static int SkipTo(string s, int i, char target) { for (int j = i + 1; j < s.Length; j++) if (s[j] == target) return j; return i; }

    // 表示用: 形を作って、1行ずつ右から左へ並べ替える
    public static string ForDisplay(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        // 形を作る前の文字(U+0600〜06FF / 0750〜077F)がある時だけ。作り済み(表示形だけ)の文は二度並べ替えない
        bool any = false;
        foreach (char c in s) if ((c >= '؀' && c <= 'ۿ') || (c >= 'ݐ' && c <= 'ݿ')) { any = true; break; }
        if (!any) return s;
        var lines = Shape(s).Split('\n');
        for (int i = 0; i < lines.Length; i++) lines[i] = ReorderLine(lines[i]);
        return string.Join("\n", lines);
    }

    static bool IsLtrStrong(char c) => char.IsLetterOrDigit(c) && !IsRtlChar(c);

    static string ReorderLine(string line)
    {
        // 連なりに分ける: 左から右(数字/ラテン文字と、その間の . , : % + - / 空白)と、それ以外(右から左)
        var runs = new List<(bool ltr, string text)>();
        int i = 0;
        while (i < line.Length)
        {
            bool ltr = IsLtrStrong(line[i]);
            int j = i + 1;
            if (ltr)
            {
                while (j < line.Length && (IsLtrStrong(line[j]) || (IsLtrJoiner(line[j]) && j + 1 < line.Length && IsLtrStrong(line[j + 1])))) j++;
            }
            else
            {
                while (j < line.Length && !IsLtrStrong(line[j])) j++;
            }
            runs.Add((ltr, line.Substring(i, j - i)));
            i = j;
        }
        var sb = new StringBuilder(line.Length);
        for (int r = runs.Count - 1; r >= 0; r--)
        {
            if (runs[r].ltr) { sb.Append(runs[r].text); continue; }
            string t = runs[r].text;
            for (int k = t.Length - 1; k >= 0; k--) sb.Append(Mirror(t[k]));
        }
        return sb.ToString();
    }

    static bool IsLtrJoiner(char c) => c == '.' || c == ',' || c == ':' || c == '%' || c == '+' || c == '-' || c == '/' || c == ' ' || c == '×' || c == '\'';
    static char Mirror(char c)
    {
        switch (c) { case '(': return ')'; case ')': return '('; case '[': return ']'; case ']': return '['; case '{': return '}'; case '}': return '{'; case '<': return '>'; case '>': return '<'; case '«': return '»'; case '»': return '«'; }
        return c;
    }
}
