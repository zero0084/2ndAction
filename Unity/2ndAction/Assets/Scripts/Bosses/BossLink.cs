using System.Collections.Generic;
using UnityEngine;

// ボス戦のつなぎのコマ(2026-11-02): 表示する絵が A → B へ変わる瞬間に、間のコマを短く挟む。見た目だけで、技の時刻・攻撃判定・予兆の長さは変えない
// Resources/BossPose/<key>/links.txt(取り込み設定は BossPoseImporter: 今の絵と同じ)(1行 = 1つ、# はコメント):  種類 <TAB> A <TAB> B <TAB> コマ(,区切り) <TAB> 秒
//   settle: A → B に変わった直後の「秒」の間、コマを順に出す(B の時間を借りる: 攻撃の後の戻り・立ち上がりなど)
//   lead:   A を出している溜めの残りが「秒」以下になったら、コマを順に出す(溜めの時間を借りる: 攻撃の瞬間は今のまま)。B は使わない
// A/B は絵の名前(* = 何でも、idle_* = idle_ で始まる絵)。コマは同じフォルダの絵の名前(待機の絵と同じキャンバス・大きさ・ピボット)
public class BossLinkSet
{
    public class Link { public bool lead; public string from, to; public Sprite[] frames; public float seconds; }
    public readonly List<Link> links = new List<Link>();
    static readonly Dictionary<string, BossLinkSet> cache = new Dictionary<string, BossLinkSet>();
    public static bool DevDisabled; // 開発版の比較用(-legacyMotion): つなぎのコマを使わない
    public static void ClearCache() => cache.Clear(); // シーンを読み直した時(BossAnimSet.OnSceneLoaded。その時ボスは残っていない)

    public static BossLinkSet Load(string key)
    {
        if (string.IsNullOrEmpty(key) || DevDisabled) return null;
        if (cache.TryGetValue(key, out var set)) return set;
        set = null;
        var txt = Resources.Load<TextAsset>("BossPose/" + key + "/links");
        if (txt != null)
        {
            set = new BossLinkSet();
            foreach (var raw in txt.text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var c = line.Split('\t');
                if (c.Length < 5) continue;
                var names = c[3].Split(',');
                var fr = new List<Sprite>();
                foreach (var n in names) { var s = Resources.Load<Sprite>("BossPose/" + key + "/" + n.Trim()); if (s != null) fr.Add(s); }
                if (fr.Count == 0) continue;
                set.links.Add(new Link { lead = c[0] == "lead", from = c[1], to = c[2], frames = fr.ToArray(), seconds = Mathf.Max(0.02f, float.Parse(c[4], System.Globalization.CultureInfo.InvariantCulture)) });
            }
            if (set.links.Count == 0) set = null;
        }
        cache[key] = set;
        return set;
    }

    static bool Match(string pat, Sprite s) => pat == "*" || (s != null && (s.name == pat || (pat.EndsWith("*") && s.name.StartsWith(pat.Substring(0, pat.Length - 1)))));
    public Link FindSettle(Sprite a, Sprite b) { foreach (var l in links) if (!l.lead && Match(l.from, a) && Match(l.to, b)) return l; return null; }
    public Link FindLead(Sprite a) { foreach (var l in links) if (l.lead && Match(l.from, a)) return l; return null; }
}

// 1体ぶんの再生。毎フレーム、本来の絵(picked)を渡すと、出す絵を返す
public class BossLinkPlayer
{
    readonly BossLinkSet set;
    Sprite lastPicked;
    BossLinkSet.Link settle;
    float settleT;
    public static int Shown; // 確認用: つなぎのコマを出したフレーム数

    public BossLinkPlayer(BossLinkSet set) { this.set = set; }

    // windupRemain: 溜めの残りの秒(溜めでなければ負)
    public Sprite Filter(Sprite picked, float windupRemain, float dt)
    {
        if (set == null || picked == null) return picked;
        if (picked != lastPicked)
        {
            settle = lastPicked != null ? set.FindSettle(lastPicked, picked) : null;
            settleT = 0f;
            lastPicked = picked;
        }
        if (settle != null)
        {
            int i = Mathf.FloorToInt(settleT / settle.seconds * settle.frames.Length);
            settleT += dt;
            if (i < settle.frames.Length) { Shown++; return settle.frames[i]; }
            settle = null;
        }
        if (windupRemain >= 0f)
        {
            var l = set.FindLead(picked);
            if (l != null && windupRemain <= l.seconds)
            {
                int i = Mathf.Clamp(Mathf.FloorToInt((l.seconds - windupRemain) / l.seconds * l.frames.Length), 0, l.frames.Length - 1);
                Shown++;
                return l.frames[i];
            }
        }
        return picked;
    }
}
