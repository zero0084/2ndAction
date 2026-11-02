using System;
using UnityEngine;

// 2026-11-02 動作のつなぎ(エステルと相談して描き足した中間のコマ)。
// 走り/待機から次の動作へ、また攻撃の終わりから走りへ切り替わる時に、姿勢が一瞬で飛んで見える所を埋める。
//   入る直前(settle = false): 次の動作の頭に出す。当たるコマ(contactFrame / 技の段の「判定が出た」段)の時刻は動かさず、
//     その手前の構えのコマと時間を分け合う(構えのコマは残りの時間に縮めて出す)。入力への反応・判定・受付の時刻は変えない
//   戻り(settle = true): 動作が終わって走り/構えへ戻った最初に出す。見た目だけ(操作は止めない、次の動作が来たらすぐやめる)
// キー: run / stance(停止戦の構え)/ startprep / attack1〜3 / kit:<技> / lance:<向き> / ult / jumpstart / jump / doublejump /
//       landing / downattack / downattackland / upshot / hurt / recovery / finish / death(敵: idle / move / telegraph / attack / recover / hit)
// from / to は '|' 区切りで複数、"*" は何でも
[Serializable]
public class MotionLink
{
    public string from = "*";
    public string to = "*";
    public bool settle;
    // 空中用(air = true): to の動作を空中で出した時、その動作の絵の代わりに出すコマ(地上の踏ん張りの絵を空中で出さない)。
    // 時間は元の動作のまま(元のコマ番号を割合でこのコマへ写す)。from / settle / contact / resume は使わない
    public bool air;
    public Sprite[] frames;
    public float fps = 14f;
    public int contactFrame = -1;  // 入る直前: 次の動作の、この番号以降のコマは必ず元の時刻で出す(-1 = 段から/無し)
    public int resumeFrame = -1;   // 戻り: 終わった後の走りのコマの番号(-1 = 走りの時計のまま)

    public float Duration => frames != null && frames.Length > 0 ? frames.Length / Mathf.Max(1f, fps) : 0f;

    public static bool Match(string pattern, string key)
    {
        if (string.IsNullOrEmpty(pattern) || pattern == "*") return true;
        if (key == null) return false;
        foreach (var p in pattern.Split('|'))
        {
            if (p == key) return true;
            if (p.EndsWith("*") && key.StartsWith(p.Substring(0, p.Length - 1), StringComparison.Ordinal)) return true;
        }
        return false;
    }
}

// 動作のつなぎの再生(プレイヤーと敵で共通)。毎フレーム「今の動作のキー」を渡し、出す絵を差し替えてもらう
public class MotionLinkPlayer
{
    MotionLink[] links;
    string curKey;
    int curSerial;
    MotionLink active;
    float t;
    bool justStarted;
    public float GroupTime { get; private set; }
    public string CurrentKey => curKey;
    public MotionLink Active => active;
    public bool Leading => active != null && !active.settle;
    public bool Settling => active != null && active.settle;
    // 試験用: 出したつなぎのコマの数(名前ごと)
    public static int Shown;
    public static string LastShown = "";

    public MotionLink[] Links => links;
    // 空中用のコマ(この動作の始まりで空中だった時だけ。途中で着地しても同じ動作の間は空中用のまま)
    public MotionLink FindAir(string key)
    {
        if (links == null || key == null) return null;
        foreach (var l in links) if (l != null && l.air && l.frames != null && l.frames.Length > 0 && MotionLink.Match(l.to, key)) return l;
        return null;
    }
    public void SetLinks(MotionLink[] l) { links = l != null && l.Length > 0 ? l : null; active = null; curKey = null; }
    public void Cancel() { active = null; }

    // 今の動作のキー(と、同じ動作を出し直した時に変わる番号)。動作が替わった時につなぎを選ぶ
    public bool Observe(string key, int serial, float dt)
    {
        justStarted = false;
        if (key == curKey && serial == curSerial)
        {
            GroupTime += dt;
            if (active != null) t += dt;
            return false;
        }
        string prev = curKey;
        curKey = key; curSerial = serial; GroupTime = 0f;
        active = null;
        if (links == null || prev == null || key == null) return true;
        MotionLink best = null; int bestScore = -1;
        foreach (var l in links)
        {
            if (l == null || l.frames == null || l.frames.Length == 0 || l.air) continue;
            if (!MotionLink.Match(l.from, prev) || !MotionLink.Match(l.to, key)) continue;
            // 具体的な指定ほど優先(入る直前 > 戻り)
            int score = (l.from == "*" ? 0 : 2) + (l.to == "*" ? 0 : 2) + (l.settle ? 0 : 1);
            if (score > bestScore) { best = l; bestScore = score; }
        }
        if (best != null) { active = best; t = 0f; justStarted = true; }
        return true;
    }

    // 入る直前: realIndex = その動作の本来のコマ、contact = 当たるコマ(以降は本来の時刻)。
    // clipFps > 0 なら、つなぎの後の構えのコマを「つなぎの終わり〜当たる時刻」に縮めて出す番号を返す(shiftedIndex)
    public Sprite Apply(Sprite pick, Sprite[] clip, int realIndex, int contact, float clipFps, out int shownIndex)
    {
        shownIndex = realIndex;
        if (active == null) return pick;
        if (active.settle)
        {
            if (t >= active.Duration) { pendingResume = active.resumeFrame; active = null; return pick; }
            Count();
            return active.frames[Mathf.Min(active.frames.Length - 1, (int)(t * active.fps))];
        }
        if (contact >= 0 && realIndex >= contact) { active = null; return pick; }
        float lead = active.Duration;
        if (t < lead) { Count(); return active.frames[Mathf.Min(active.frames.Length - 1, (int)(t * active.fps))]; }
        // つなぎの後: 構えのコマを当たる時刻までに縮めて出す(当たる時刻が分からない技は、つなぎの分だけ遅らせて当たる段で追いつく)
        if (clip != null && contact > 0 && clipFps > 0f)
        {
            float tc = contact / clipFps;
            int idx = tc > lead + 0.001f ? Mathf.FloorToInt((GroupTime - lead) / (tc - lead) * contact) : contact - 1;
            shownIndex = Mathf.Clamp(idx, 0, contact - 1);
            return clip[Mathf.Min(shownIndex, clip.Length - 1)];
        }
        return pick;
    }

    // 戻りが終わった時の走りのコマ(-1 = 合わせない)。1回だけ返す
    int pendingResume = -1;
    public int TakeResume() { int r = pendingResume; pendingResume = -1; return r; }

    void Count()
    {
        if (!justStarted) return;
        justStarted = false;
        Shown++;
        LastShown = $"{active.from}>{active.to}{(active.settle ? " settle" : " lead")}";
    }
}
