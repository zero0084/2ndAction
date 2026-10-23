using System.Collections.Generic;
using UnityEngine;

// 同時に攻撃できる雑魚の上限(2026-10-24、通常マップの 90〜95km の雑魚の大群)。
//  画面に敵が多くても、予兆〜攻撃を同時に始められるのは MaxAttackers 体まで(残りは少し待って、空いたら攻撃する)。
//  攻撃を始めた敵は SlotSeconds(攻撃の種類ごとに予兆+攻撃のおおよその長さ)だけ枠を使う(終わりの処理を探さなくても必ず空く)。
//  体当たり(重い敵/追う敵の接触)と、すでに飛んでいる弾は数えない(止めると見た目が不自然になる)。
//  大群の区間だけ有効(Active)。ほかの区間の敵の動きは変えない。
public static class ZakoAttackLimiter
{
    public static int MaxAttackers = 3;
    public static bool ForceActive; // 開発用(-zakoLimitAlways 1): 大群の区間の外でも効かせる
    static readonly Dictionary<Component, float> holders = new Dictionary<Component, float>();
    static readonly List<Component> expired = new List<Component>();
    public static int Granted, Denied, MaxHeld; // 確認用
    static int activeFrame = -1; static bool activeCache;

    public static bool Active
    {
        get
        {
            if (activeFrame == Time.frameCount) return activeCache;
            activeFrame = Time.frameCount;
            activeCache = ForceActive || BossManager.ZakoRushNow;
            if (!activeCache && holders.Count > 0) holders.Clear();
            return activeCache;
        }
    }

    public static int Held { get { Prune(); return holders.Count; } }

    // 攻撃を始めてよいか(よければ枠を取る)。区間の外では常に true
    public static bool TryBegin(Component who, float seconds)
    {
        if (who == null || !Active) return true;
        Prune();
        float until = Time.time + Mathf.Max(0.2f, seconds);
        if (holders.ContainsKey(who)) { holders[who] = Mathf.Max(holders[who], until); return true; }
        if (holders.Count >= Mathf.Max(1, MaxAttackers)) { Denied++; return false; }
        holders[who] = until;
        Granted++;
        MaxHeld = Mathf.Max(MaxHeld, holders.Count);
        return true;
    }

    public static void Release(Component who) { if (who != null) holders.Remove(who); }

    static void Prune()
    {
        expired.Clear();
        foreach (var kv in holders) if (kv.Key == null || kv.Value <= Time.time || !kv.Key.gameObject.activeInHierarchy) expired.Add(kv.Key);
        foreach (var k in expired) holders.Remove(k);
    }

    public static void ResetStats() { Granted = Denied = MaxHeld = 0; holders.Clear(); }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void DevArgs()
    {
        var a = QaArgs.All;
        for (int i = 0; i < a.Length - 1; i++)
        {
            if (a[i] == "-zakoLimit" && int.TryParse(a[i + 1], out int n)) MaxAttackers = n;
            if (a[i] == "-zakoLimitAlways") ForceActive = a[i + 1] == "1";
        }
    }
#endif
}
