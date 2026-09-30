using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ラストダンジョン 100,000m: 死神三姉妹戦の進行(2026-09-30)。
//  登場 → 前半: 長女 → 次女 → 三女 を1人ずつ(それぞれの特徴を見せる。HP0で退く)
//       → 後半: 三人同時(最終フェーズ) → 全員撃破 → 勝利(RESULTへは行かず、エンドロールへ走り続ける)
// ボス戦の間は既存の仕組み(BossManager.IsBossPhase)で距離を止め、雑魚/障害物を止め、死神の曲を流す。
public class ReaperFinaleBattle : MonoBehaviour
{
    public enum Stage { Intro, Solo, Interlude, Group, Victory, Done }
    public Stage Current { get; private set; } = Stage.Intro;
    public int SoloIndex { get; private set; } = -1;
    public int GroupDefeated { get; private set; }
    public string Banner { get; private set; } = "";
    public float BannerTime { get; private set; } = -99f;
    public readonly List<ReaperSisterBoss> Active = new List<ReaperSisterBoss>();
    public System.Action Finished;
    public static ReaperFinaleBattle Instance { get; private set; }

    [Tooltip("前半(1人ずつ)のHP")] public int soloHp = 280;
    [Tooltip("後半(三人同時)の1人あたりのHP")] public int groupHp = 200;
    static readonly ReaperSister[] Order = { ReaperSister.Eldest, ReaperSister.Second, ReaperSister.Youngest };

    Transform player;
    bool soloDone;

    public static ReaperFinaleBattle Begin(Transform player)
    {
        var go = new GameObject("[ReaperFinaleBattle]");
        var b = go.AddComponent<ReaperFinaleBattle>();
        b.player = player;
        Instance = b;
        b.StartCoroutine(b.Run());
        return b;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    int Hp(int baseHp)
    {
        float m = GameManager.Instance != null ? GameManager.Instance.BossHpMultiplier : 1f;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (DebugHpOverride > 0) return DebugHpOverride;
#endif
        return Mathf.Max(1, Mathf.RoundToInt(baseHp * Mathf.Max(0.01f, m)));
    }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public static int DebugHpOverride; // 自動テスト用
#endif

    void Say(string text) { Banner = text; BannerTime = Time.time; Debug.Log("[LastDungeon][Finale] " + text); }

    static string NameOf(ReaperSister s)
    {
        var d = Resources.Load<ReaperSisterData>("Reapers/" + s + "Data");
        return d != null && !string.IsNullOrEmpty(d.displayName) ? d.displayName : s.ToString();
    }

    IEnumerator Run()
    {
        var bm = BossManager.Instance;
        if (bm != null) bm.BeginScriptedBossPhase(BossBgmTier.Death, "*/Death");
        Current = Stage.Intro;
        Say("死神三姉妹");
        yield return new WaitForSeconds(2.4f);

        // ---- 前半: 1人ずつ ----
        for (int i = 0; i < Order.Length; i++)
        {
            SoloIndex = i;
            Current = Stage.Solo;
            Say(NameOf(Order[i]));
            soloDone = false;
            var b = ReaperSisterBoss.Create(Order[i], player, 0, Hp(soloHp), 8f);
            b.retreatOnLethal = true;
            b.Retreated = _ => soloDone = true;
            Active.Add(b);
            while (!soloDone && b != null) yield return null;
            Active.Remove(b);
            if (b != null) Destroy(b.gameObject, 0.2f);
            Current = Stage.Interlude;
            yield return new WaitForSeconds(1.2f);
        }

        // ---- 後半: 三人同時 ----
        Current = Stage.Group;
        Say("三姉妹");
        yield return new WaitForSeconds(1.0f);
        GroupDefeated = 0;
        for (int i = 0; i < Order.Length; i++)
        {
            var b = ReaperSisterBoss.Create(Order[i], player, i, Hp(groupHp), 7f);
            b.aggression = 1.25f;
            b.DefeatOverride = w => { GroupDefeated++; Debug.Log($"[LastDungeon][Finale] {w.bossName} defeated ({GroupDefeated}/3)"); };
            Active.Add(b);
            yield return new WaitForSeconds(0.5f);
        }
        while (GroupDefeated < Order.Length) yield return null;

        // ---- 勝利: RESULTへは行かない ----
        Current = Stage.Victory;
        if (bm != null) bm.MarkScriptedBossDefeated();
        if (GameManager.Instance != null) GameManager.Instance.DropPendingChoicesForFinale();
        GameManager.BlockExpGain = true;
        Say("");
        var cf = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        if (cf != null) cf.Shake(0.2f, 0.6f);
        yield return new WaitForSeconds(1.6f);
        if (bm != null) bm.EndScriptedBossPhase();
        Active.Clear();
        Current = Stage.Done;
        Finished?.Invoke();
    }
}
