using System.Collections.Generic;
using UnityEngine;

// アニメーション強化(2026-10-17、試作: オオカミ): ボスの姿勢ごとのコマ。
// Resources/BossAnim/<key>/ に「姿勢名_番号.png」で置くと読み込む(例: move_00〜move_05、windup_00〜02)。
// 姿勢名: idle(待機の繰り返し) / move(移動の繰り返し) / windup(溜めの時間に合わせて順に) / attack(攻撃の間に順に)
//         hurt(被弾) / break(BREAK・必殺技の後の隙) / ult(必殺技: 0=構え 1=突進など) / defeat(撃破)
//         windupB / attackB(2つ目の技の溜め/攻撃。ボスの技が animVariant = 1 にした時だけ使う。無ければ windup/attack)。2026-10-19 死神三姉妹
//         2026-10-20(死神三姉妹のアニメーション品質): recover / recoverB(攻撃の後の戻り。Recover の始めに順に)、breakloop(BREAK の間の繰り返し。
//         break の後)、rise(BREAK からの立ち直り)。hurt が2枚以上なら被弾/怯みの間に順に。どれも無いボスは今までどおり
// どれも無ければ今までどおりの1枚絵(idle/windup/attack のスプライト)。絵は全部同じ縮尺(待機の絵と同じピクセルの大きさ)で、足元が下端。
public class BossAnimSet
{
    public Sprite[] idle, move, windup, attack, hurt, brk, ult, defeat, windupB, attackB, recover, recoverB, breakLoop, rise;
    // 2026-10-20: 3つ目/4つ目の技(animVariant 2 = C、3 = D)。ムカデの低い突進/背の高い突進
    public Sprite[] windupC, attackC, windupD, attackD;
    // 2026-10-20: 必殺技を外した後の専用の隙(サソリ: 尻尾が地面に刺さって抜けない)。使うかはボスが決める(PoseOverride)
    public Sprite[] exhaust;
    // 2026-10-20: 地面へ潜る(dive_、潜り始め → 尾だけ残る)/ 地面を割って飛び出す(emerge_)。蛇・モグラ・ワームなど潜るボス用。使うかはボスが決める
    public Sprite[] dive, emerge;
    // 2026-10-22: 着地して息を整える隙(land_、グリフォンの急降下の後)。墜落の隙(exhaust_)とは別の絵。使うかはボスが決める
    public Sprite[] land;
    // 2026-10-22: 5つ目の技(animVariant 4 = E、黒騎士の空中斬り)/ 跳んでいる間(fly_、Pose.Fly。無ければ move_)
    public Sprite[] windupE, attackE, fly;
    public float idleFps = 6f, moveFps = 12f;
    // 本物の走りのコマがある時は、格子の変形(偽の脚の動き)を足さない。
    // 2026-10-24: 以前は 0.2 残していたが、絵の脚とは別の周期で下半身が横へ(絵の幅の約3%)揺れて、絵の動きと競ってバタついて見えた
    public float rigLocoScale = 0f;

    public bool HasMove => move != null && move.Length > 0;

    static readonly Dictionary<string, BossAnimSet> cache = new Dictionary<string, BossAnimSet>();

    // 2026-10-25 v1.0 容量監査: 読み込んだボスのコマはランが終わっても static に残り、ラスダン(ボスラッシュ)の後はホームへ戻っても
    // 全部のボスの絵がメモリに残っていた。シーンを読み直した時(ランの終わり → ホーム、もう一度)に手放して、使っていない素材を片付ける。
    // ランの途中は今までどおり(一度読んだボスは残す。もう一度出た時に読み直しの待ちを作らない)
    public static int CachedSets => cache.Count;
    public static int Releases { get; private set; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void HookSceneLoads()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }
    static void OnSceneLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (mode != UnityEngine.SceneManagement.LoadSceneMode.Single || cache.Count == 0) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-noBossCacheClear") >= 0) return; // 比較の試験用
#endif
        int n = cache.Count;
        cache.Clear();
        BossRig.ClearSharedCache();
        WormBoss.ClearPieceCache();
        Resources.UnloadUnusedAssets();
        Releases++;
        Debug.Log($"[BossAnimSet] released {n} boss frame sets on scene load");
    }

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
                recover = Pick(all, "recover_"), recoverB = Pick(all, "recoverB_"), breakLoop = Pick(all, "breakloop_"), rise = Pick(all, "rise_"),
                windupC = Pick(all, "windupC_"), attackC = Pick(all, "attackC_"), windupD = Pick(all, "windupD_"), attackD = Pick(all, "attackD_"), exhaust = Pick(all, "exhaust_"), dive = Pick(all, "dive_"), emerge = Pick(all, "emerge_"), land = Pick(all, "land_"),
                windupE = Pick(all, "windupE_"), attackE = Pick(all, "attackE_"), fly = Pick(all, "fly_"),
            };
            // 2026-10-22 第3段階: 移動のループの再生順(move_seq.txt、move_ の番号を空白区切り)。同じ絵を何枠か続けて表示時間に差をつける
            // (羽ばたきの「下ろしは速く・上げはゆっくり」等)。同じ絵の PNG を複製しないので、テクスチャのメモリは増えない
            var seqText = Resources.Load<TextAsset>("BossAnim/" + key + "/move_seq");
            if (seqText != null && set.move != null)
            {
                var seq = new List<Sprite>();
                foreach (var tok in seqText.text.Replace(',', ' ').Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries))
                    if (int.TryParse(tok, out int i) && i >= 0 && i < set.move.Length) seq.Add(set.move[i]);
                if (seq.Count > 0) set.move = seq.ToArray();
            }
        }
        cache[key] = set;
        return set;
    }

    // 技の番号(animVariant)ごとの溜め/攻撃。その技のコマが無ければ1つ目の技のコマ
    public Sprite[] WindupFor(int v) => (v == 1 ? windupB : v == 2 ? windupC : v == 3 ? windupD : v == 4 ? windupE : null) ?? windup;
    public Sprite[] AttackFor(int v) => (v == 1 ? attackB : v == 2 ? attackC : v == 3 ? attackD : v == 4 ? attackE : null) ?? attack;
    public Sprite[] RecoverFor(int v) => (v == 1 ? recoverB : null) ?? recover;

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
