using UnityEngine;

// マルチプレイのルールの調整値(2026-11-05、クローズドテスト用の仮値。Inspector で直せる)。
//  値は Resources/Balance/MultiplayerRules(このクラスの asset)。無ければこのコードの既定値。
//  ・CO-OP の復活: DOWN した本人が長押し → 長押しが終わった瞬間の「一番前を走っている生存プレイヤー(FRONT)」から HP を 1 もらう(HOST が決める)
//  ・VERSUS の死神の乱入時間(部屋を作る時に HOST が選ぶ)/ 予告 / 順位と死神フェーズの MILE ボーナス
[CreateAssetMenu(menuName = "OneMoreMile/Multiplayer Rules")]
public class MultiplayerRules : ScriptableObject
{
    [Header("CO-OP: 長押しで復活")]
    [Tooltip("復活に必要な長押しの秒数(途中で離すと取り消し。HP は長押しが終わった時だけ動く)")]
    public float reviveHoldSeconds = 1.2f;
    [Tooltip("復活でもらう/渡す HP(ハートの数)")]
    public int reviveHearts = 1;
    [Tooltip("FRONT のプレイヤーの HP がこのハート数以上の時だけ渡せる(渡して 0 にならない)")]
    public int donorMinHearts = 2;
    [Tooltip("復活した直後の無敵の秒数")]
    public float reviveInvulnSeconds = 1.5f;

    [Header("VERSUS: 死神の乱入時間")]
    [Tooltip("部屋を作る時に選べる時間(分)。0 = なし(100,000m の死神だけ)")]
    public int[] reaperMinuteOptions = { 5, 10, 20, 30, 0 };
    [Tooltip("初めて部屋を作る時の時間(分)")]
    public int reaperDefaultMinutes = 10;
    [Tooltip("予告を出す残り秒数(大きい順)")]
    public int[] reaperWarnSeconds = { 60, 30, 10 };
    [Tooltip("予告を出しておく秒数")]
    public float reaperWarnShowSeconds = 3f;

    [Header("VERSUS: MILE ボーナス(試合が終わった時に、脱落までに稼いだ MILE に足す割合)")]
    [Tooltip("参加した全員(切断/途中退出を除く)")]
    public float participationBonus = 0.05f;
    [Tooltip("順位の上の割合に応じて足す最大値(1位 = これ全部、最下位 = 0。上位率 = (人数-順位)/(人数-1))")]
    public float percentileBonus = 0.05f;
    [Tooltip("1位/2位/3位に足す追加(最下位には付かない。2人なら1位だけ)")]
    public float[] topRankBonus = { 0.20f, 0.10f, 0.05f };
    [Tooltip("死神の乱入の時に生き残っていた人に足す REAPER BONUS")]
    public float reaperBonus = 0.10f;
    [Tooltip("ボーナスの合計の上限")]
    public float maxTotalBonus = 0.45f;

    // 順位と人数からボーナスの割合(参加 + 上位率 + 上位の追加)。reaper = 死神のフェーズまで生き残った
    public float RankBonus(int rank, int players)
    {
        if (rank <= 0 || players <= 0) return 0f;
        float p = players <= 1 ? 1f : Mathf.Clamp01((players - rank) / (float)(players - 1));
        float b = participationBonus + percentileBonus * p;
        bool last = players > 1 && rank >= players;
        if (!last && topRankBonus != null && rank - 1 < topRankBonus.Length) b += topRankBonus[rank - 1];
        return b;
    }
    public float TotalBonus(int rank, int players, bool reaper) => Mathf.Min(maxTotalBonus, RankBonus(rank, players) + (reaper ? reaperBonus : 0f));

    // ---- 読み込み ----
    static MultiplayerRules inst; static bool loaded;
    public static MultiplayerRules I
    {
        get
        {
            if (!loaded)
            {
                loaded = true;
                inst = Resources.Load<MultiplayerRules>("Balance/MultiplayerRules");
                if (inst == null) { inst = CreateInstance<MultiplayerRules>(); inst.hideFlags = HideFlags.DontSave; }
            }
            return inst;
        }
    }

    public int ReviveHp => Mathf.Max(1, reviveHearts) * CombatScale.PlayerHit;
    public int DonorMinHp => Mathf.Max(reviveHearts + 1, donorMinHearts) * CombatScale.PlayerHit;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 試験用: -netReaperSeconds 60 で死神の乱入時間を秒で上書き(部屋の設定より優先。HOST だけが使う)
    public static int QaReaperSeconds
    {
        get
        {
            var a = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(a, "-netReaperSeconds");
            return i >= 0 && i + 1 < a.Length && int.TryParse(a[i + 1], out int v) ? v : -1;
        }
    }
#endif
}
