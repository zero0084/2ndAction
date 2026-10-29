using UnityEngine;

// 予兆の先読み(2026-10-29): 速さが高いキャラほど、敵の攻撃の予兆を早く見られる(スローモーションではない)。
//  ・敵の行動の速さ・攻撃の時刻・当たり判定は全員共通。変えるのは「その画面で予兆を出し始める時刻」だけ
//  ・先読みの長さ = 能力としての速さ(PlayerController.AbilityRunSpeed。ボス戦で止まっていても変わらない)から決める。上限あり
//    (BossBattleTuning: foresightStartKmh / foresightFullKmh / foresightMaxLead)
//  ・出すのはボスが実際に決めた攻撃だけ: 攻撃の技を始めて(=決めて)から予告の溜めに入るまでの、もともとある間
//    (接近/間合い取り/待ち/高さ合わせ/複数体の順番待ち)の最後の Lead 秒に、予定の範囲を「先読みの印」で出す。
//    待ち時間は延ばさない(先読みのために攻撃を遅らせない)。溜めに入れば今までどおりの予兆に切り替わる
//  ・先読みの印は形でも区別する: 四隅のかぎ括弧 + 点線の枠(中は塗らない)。今までの予兆は縞の塗り + 点滅
public static class TelegraphLead
{
    public static bool Enabled = true; // 開発用の比較(-leadOff 1)
    public static float DebugForceKmh = -1f; // 開発用: この速さとして計算(<0 なら実際の能力の速さ)。開発版は起動引数 -leadKmh 150 でも
    static bool argsRead;
    static void ReadArgs()
    {
        argsRead = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < a.Length; i++)
        {
            if (a[i] == "-leadKmh" && float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float k)) DebugForceKmh = k;
            if (a[i] == "-leadOff" && a[i + 1] == "1") Enabled = false;
        }
#endif
    }

    public static float ForKmh(float kmh)
    {
        if (!Enabled) return 0f;
        var tn = BossBattleTuning.I;
        float a = tn.foresightStartKmh, b = Mathf.Max(a + 1f, tn.foresightFullKmh);
        return Mathf.Max(0f, tn.foresightMaxLead) * Mathf.Clamp01((kmh - a) / (b - a));
    }

    // この端末のプレイヤーの先読み(秒)
    public static float Local
    {
        get
        {
            if (!argsRead) ReadArgs();
            if (DebugForceKmh >= 0f) return ForKmh(DebugForceKmh);
            var pc = PlayerController.Instance;
            return pc != null ? ForKmh(GameManager.SpeedKmh(pc.AbilityRunSpeed)) : 0f;
        }
    }
    public static float MaxLead => Mathf.Max(0f, BossBattleTuning.I.foresightMaxLead);
    public static int EarlyShown, EarlyPromoted, EarlyCancelled; // 確認用: 出した/予兆へ切り替わった/攻撃が来ずに消した
}

// JOIN の端末の先読みの印(2026-10-29): HOST から届いた範囲と「予兆が始まる共通時計の時刻」から、この端末の先読みの長さだけ前に出す。
// 予兆が始まる時刻(+少しの余裕)で消す。中止の知らせ/ボスが消えた時も消す。古い知らせは使わない(届いた時に時刻を過ぎていたら出さない)
public class RemoteForesight : MonoBehaviour
{
    class Entry { public int bossId; public Transform owner; public Vector4[] rects; public float facing; public double baseStart; public SpriteRenderer[] srs; }
    static RemoteForesight inst;
    readonly System.Collections.Generic.Dictionary<int, Entry> entries = new System.Collections.Generic.Dictionary<int, Entry>();
    public static int Shown, Expired, Cancelled;

    static RemoteForesight I
    {
        get
        {
            if (inst == null) { var go = new GameObject("RemoteForesight"); inst = go.AddComponent<RemoteForesight>(); }
            return inst;
        }
    }

    public static void Show(int bossId, Transform owner, Vector4[] rects, float facing, double baseStart)
    {
        var me = I;
        if (me.entries.TryGetValue(bossId, out var old)) me.Remove(old);
        me.entries[bossId] = new Entry { bossId = bossId, owner = owner, rects = rects, facing = facing, baseStart = baseStart };
    }
    public static void Cancel(int bossId)
    {
        if (inst == null || !inst.entries.TryGetValue(bossId, out var e)) return;
        if (e.srs != null) Cancelled++;
        inst.Remove(e);
    }

    void Remove(Entry e)
    {
        if (e.srs != null) foreach (var sr in e.srs) if (sr != null) Destroy(sr.gameObject);
        entries.Remove(e.bossId);
    }

    readonly System.Collections.Generic.List<Entry> tmp = new System.Collections.Generic.List<Entry>();
    void Update()
    {
        double now = NetRunLauncher.NetNow;
        float lead = TelegraphLead.Local;
        tmp.Clear(); tmp.AddRange(entries.Values);
        foreach (var e in tmp)
        {
            if (e.owner == null || now >= e.baseStart + 0.25) { if (e.srs != null && e.owner != null) Expired++; Remove(e); continue; }
            if (e.srs == null && now >= e.baseStart - lead && now < e.baseStart)
            {
                e.srs = new SpriteRenderer[e.rects.Length];
                for (int i = 0; i < e.rects.Length; i++)
                {
                    var go = new GameObject("TelegraphEarlyRemote");
                    go.transform.SetParent(e.owner, false);
                    go.transform.localPosition = new Vector3(e.rects[i].x * Mathf.Sign(e.facing), e.rects[i].y, 0f);
                    go.transform.localScale = new Vector3(e.rects[i].z, e.rects[i].w, 1f);
                    var sr = go.AddComponent<SpriteRenderer>();
                    sr.sprite = BossFx.WarnCorners();
                    sr.sortingOrder = RenderOrder.Boss;
                    e.srs[i] = sr;
                }
                Shown++;
            }
            if (e.srs != null)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 7f);
                foreach (var sr in e.srs) if (sr != null) sr.color = new Color(1f, 0.78f, 0.25f, Mathf.Lerp(0.55f, 0.9f, pulse));
            }
        }
    }
}
