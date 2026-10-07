using System.Globalization;
using System.Text;
using UnityEngine;

// コードネーム(2026-10-08): ランキングとマルチで共通に使う表示名。
//  ・設定から登録・変更する。未設定でもソロで遊べる(初回起動で入力を求めない)。ランキングへ初めて参加する時に確認する。
//  ・同じ名前を許す。表示名と本人の識別(UGS の PlayerId / マルチの接続番号)は別。名前を変えても記録や本人は変わらない。
//  ・保存するだけではランキングへ投稿しない。
//  ・表示崩れの防止: 前後の空白を除き、連続する空白を1つに、改行/タブ/制御文字/書式文字(ゼロ幅結合子は残す)/タグの < > を取り除く。
//    空白だけの名前は未設定扱い。長さは見た目の文字数(書記素、絵文字の合成も1文字)で MaxLength まで。
public static class Codename
{
    public const string Key = "Profile.CodenameV1";
    public const int MaxLength = 16;
    public const string Unset = "";

    public static string Current => Sanitize(SaveStore.GetString(Key, ""));
    public static bool IsSet => !string.IsNullOrEmpty(Current);
    // 表示用: 未設定なら代わりの名前
    public static string DisplayOr(string fallback) => IsSet ? Current : fallback;

    public static event System.Action Changed;

    // 保存。戻り値: 保存した名前(空 = 未設定に戻した)
    public static string Set(string raw)
    {
        string v = Sanitize(raw);
        if (v == Current) return v;
        SaveStore.SetString(Key, v);
        SaveStore.Save();
        Debug.Log($"[Codename] changed ({LengthOf(v)} chars)");
        Changed?.Invoke();
        if (Leaderboard.Joined) _ = Leaderboard.OnCodenameChanged();
        return v;
    }

    public static int LengthOf(string s) => string.IsNullOrEmpty(s) ? 0 : new StringInfo(s).LengthInTextElements;

    public static string Sanitize(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var sb = new StringBuilder(raw.Length);
        bool space = false;
        foreach (char c in raw)
        {
            var cat = char.GetUnicodeCategory(c);
            bool isSpace = char.IsWhiteSpace(c) || cat == UnicodeCategory.LineSeparator || cat == UnicodeCategory.ParagraphSeparator;
            if (isSpace) { space = true; continue; }
            if (cat == UnicodeCategory.Control) continue;
            if (cat == UnicodeCategory.Format && c != '‍') continue; // 書式文字(向きの上書き等)は除く。絵文字のゼロ幅結合子は残す
            if (c == '<' || c == '>' || c == '|') continue;                 // 装飾タグ(<color> 等)を作らせない。| は LAN の部屋の知らせの区切り
            if (space && sb.Length > 0) sb.Append(' ');
            space = false;
            sb.Append(c);
        }
        string s = sb.ToString();
        if (LengthOf(s) > MaxLength)
        {
            var si = new StringInfo(s);
            s = si.SubstringByTextElements(0, MaxLength).TrimEnd();
        }
        return s;
    }
}
