using System.Collections.Generic;
using System.Text;

// 前に書く母音記号の並べ替え(2026-10-10、全体点検)。ヒンディー語/ベンガル語/タミル語/ビルマ語/クメール語。
// Unity の文字の描画(IMGUI / uGUI の Text)は、文字の形を作る処理(シェーピング)をしないので、
// 「子音の左側に書く母音記号」(例: ि ে ெ ေ េ)が子音の右側に出て、読めない並びになっていた。
// → 表示の直前に、その母音記号を子音のまとまりの前へ移す(見た目の順に並べる)。2つに分かれる母音(例: ொ = ெ + ா)は分けて前後へ。
// 限界: 子音の結合文字(合字)や、クメール語の下に付く子音の形は、フォントの置き換え表(GSUB)が要るので直らない(結合の記号が見えたまま)。
//      タミル語は合字が少ないので、ほぼ正しく読める。ラオス語/タイ語は元から見た目の順の文字なので不要。
public static class IndicShaper
{
    public static bool Handles(string lang) => lang == "hi" || lang == "bn" || lang == "ta" || lang == "my" || lang == "km";

    public static string ForDisplay(string s, string lang)
    {
        if (string.IsNullOrEmpty(s) || !Handles(lang)) return s;
        bool any = false;
        foreach (char ch in s) if (ch >= 0x0900 && ch <= 0x0DFF || ch >= 0x1000 && ch <= 0x109F || ch >= 0x1780 && ch <= 0x17FF) { any = true; break; }
        if (!any) return s;
        switch (lang)
        {
            case "hi": return Reorder(s, IsDevaCons, 0x094D, 0x093C, c => c == 0x093F ? "ि" : null, null);
            case "bn": return Reorder(s, IsBengCons, 0x09CD, 0x09BC, BengPre, BengPost);
            case "ta": return Reorder(s, IsTamilCons, -1, -1, TamilPre, TamilPost);
            case "my": return Myanmar(s);
            case "km": return Reorder(s, IsKhmerCons, 0x17D2, -1, KhmerPre, KhmerPost, khmer: true);
        }
        return s;
    }

    static bool IsDevaCons(char c) => c >= 0x0915 && c <= 0x0939 || c >= 0x0958 && c <= 0x095F;
    static bool IsBengCons(char c) => c >= 0x0995 && c <= 0x09B9 || c >= 0x09DC && c <= 0x09DF;
    static bool IsTamilCons(char c) => c >= 0x0B95 && c <= 0x0BB9;
    static bool IsKhmerCons(char c) => c >= 0x1780 && c <= 0x17A2;

    // 前へ移す部分(無ければ null)/ 後ろに残す部分
    static string BengPre(char c) => c == 0x09BF || c == 0x09C7 || c == 0x09C8 ? c.ToString() : c == 0x09CB || c == 0x09CC ? "ে" : null;
    static string BengPost(char c) => c == 0x09CB ? "া" : c == 0x09CC ? "ৗ" : "";
    static string TamilPre(char c) => c == 0x0BC6 || c == 0x0BC7 || c == 0x0BC8 ? c.ToString() : c == 0x0BCA || c == 0x0BCC ? "ெ" : c == 0x0BCB ? "ே" : null;
    static string TamilPost(char c) => c == 0x0BCA || c == 0x0BCB ? "ா" : c == 0x0BCC ? "ௗ" : "";
    static string KhmerPre(char c) => c == 0x17C1 || c == 0x17C2 || c == 0x17C3 ? c.ToString() : c == 0x17C4 ? "េ" : null;
    static string KhmerPost(char c) => c == 0x17C4 ? "ា" : "";

    // 子音のまとまり(子音 [ヌクタ] (ハラント 子音 [ヌクタ])*)の直後に前置の母音記号があれば、まとまりの前へ
    static string Reorder(string s, System.Func<char, bool> isCons, int halant, int nukta, System.Func<char, string> pre, System.Func<char, string> post, bool khmer = false)
    {
        var sb = new StringBuilder(s.Length + 4);
        int i = 0, n = s.Length;
        while (i < n)
        {
            char c = s[i];
            if (!isCons(c)) { sb.Append(c); i++; continue; }
            int start = i;
            int j = i + 1;
            if (nukta >= 0 && j < n && s[j] == nukta) j++;
            // タミル語: 母音記号は最後の子音にだけ付く(プッリ ் は見える記号で、まとまりを作らない)
            while (halant >= 0 && j + 1 < n && s[j] == halant && isCons(s[j + 1])) { j += 2; if (nukta >= 0 && j < n && s[j] == nukta) j++; }
            // クメール語: 子音の後の記号(登録の印など)を越えて母音を探す
            int k = j;
            if (khmer) while (k < n && (s[k] == 0x17C9 || s[k] == 0x17CA || s[k] == 0x17CC)) k++;
            if (k < n)
            {
                string p = pre(s[k]);
                if (p != null)
                {
                    sb.Append(p);
                    sb.Append(s, start, k - start);
                    if (post != null) sb.Append(post(s[k]));
                    i = k + 1;
                    continue;
                }
            }
            sb.Append(s, start, j - start);
            i = j;
        }
        return sb.ToString();
    }

    // ビルマ語: 子音 [ (1039 子音)* ] [中間の記号 103B-103E]* [1031] → [1031][103C] 子音 … [その他の中間の記号]
    static string Myanmar(string s)
    {
        var sb = new StringBuilder(s.Length + 4);
        int i = 0, n = s.Length;
        while (i < n)
        {
            char c = s[i];
            bool cons = c >= 0x1000 && c <= 0x1021 || c == 0x103F;
            if (!cons) { sb.Append(c); i++; continue; }
            int j = i + 1;
            while (j + 1 < n && s[j] == 0x1039) j += 2;
            int medStart = j;
            while (j < n && s[j] >= 0x103B && s[j] <= 0x103E) j++;
            bool e = j < n && s[j] == 0x1031;
            bool ra = false;
            for (int m = medStart; m < j; m++) if (s[m] == 0x103C) ra = true;
            if (!e && !ra) { sb.Append(s, i, j - i); i = j; continue; }
            if (e) sb.Append('ေ');
            if (ra) sb.Append('ြ');
            sb.Append(s, i, medStart - i);
            for (int m = medStart; m < j; m++) if (s[m] != 0x103C) sb.Append(s[m]);
            i = e ? j + 1 : j;
        }
        return sb.ToString();
    }
}
