using System.Collections.Generic;
using UnityEngine;

// アニメーション強化(2026-10-17、試作: オオカミ): ボスの姿勢ごとのコマ。
// Resources/BossAnim/<key>/ に「姿勢名_番号.png」で置くと読み込む(例: move_00〜move_05、windup_00〜02)。
// 姿勢名: idle(待機の繰り返し) / move(移動の繰り返し) / windup(溜めの時間に合わせて順に) / attack(攻撃の間に順に)
//         hurt(被弾) / break(BREAK・必殺技の後の隙) / ult(必殺技: 0=構え 1=突進など) / defeat(撃破)
//         windupB / attackB(2つ目の技の溜め/攻撃。ボスの技が animVariant = 1 にした時だけ使う。無ければ windup/attack)。2026-10-19 死神三姉妹
// どれも無ければ今までどおりの1枚絵(idle/windup/attack のスプライト)。絵は全部同じ縮尺(待機の絵と同じピクセルの大きさ)で、足元が下端。
public class BossAnimSet
{
    public Sprite[] idle, move, windup, attack, hurt, brk, ult, defeat, windupB, attackB;
    public float idleFps = 6f, moveFps = 12f;
    // 本物の走りのコマがある時は、格子の変形(偽の脚の動き)を弱める
    public float rigLocoScale = 0.2f;

    public bool HasMove => move != null && move.Length > 0;

    static readonly Dictionary<string, BossAnimSet> cache = new Dictionary<string, BossAnimSet>();

    public static BossAnimSet Load(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (cache.TryGetValue(key, out var hit)) return hit;
        var all = Resources.LoadAll<Sprite>("BossAnim/" + key);
        BossAnimSet set = null;
        if (all != null && all.Length > 0)
        {
            set = new BossAnimSet
            {
                idle = Pick(all, "idle_"), move = Pick(all, "move_"), windup = Pick(all, "windup_"), attack = Pick(all, "attack_"),
                hurt = Pick(all, "hurt_"), brk = Pick(all, "break_"), ult = Pick(all, "ult_"), defeat = Pick(all, "defeat_"),
                windupB = Pick(all, "windupB_"), attackB = Pick(all, "attackB_"),
            };
        }
        cache[key] = set;
        return set;
    }

    static Sprite[] Pick(Sprite[] all, string prefix)
    {
        var list = new List<Sprite>();
        foreach (var s in all) if (s != null && s.name.StartsWith(prefix)) list.Add(s);
        if (list.Count == 0) return null;
        list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return list.ToArray();
    }

    public static Sprite Loop(Sprite[] f, float fps, float time) => f[Mathf.FloorToInt(time * fps) % f.Length];
    // 0→1 の進みに合わせて順に(最後で止まる)
    public static Sprite Progress(Sprite[] f, float p) => f[Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(p) * f.Length), 0, f.Length - 1)];
}
