using UnityEngine;

// マルチ Phase 3.1(2026-10-02): ボスが「走っているプレイヤー群」から永久に離れないようにする(HOSTのボスAIだけ)。
//
// 問題: ボスは狙っている相手(最も近い人)と並走する。全員同じ速さで走るので、カード選択などで一度止まった人は
//       ボスと戦っている先頭の人に二度と追いつけなかった。
//
// 方針(瞬間移動ではなく、速さと狙いで直す):
//  1) 置き去り防止: 狙っている相手より後ろに、走っている(カード選択中でない)ALIVEの人が LeashStart 以上離れていたら、
//     ボスの並走速度を少しずつ落とす(最大 MaxSlow)。先頭の人はボスを追い越していき、ボスは後ろの人へ近づく。
//     ボスが狙いの相手より FallBehindSwitch 以上後ろになったら、狙いを最後尾の人へ切り替える。
//     → VERSUSでは「先に進んだ人が先にボスと戦う」は残る(後ろの人が十分近ければ何も変えない)。
//       変わるのは「離れた人が物理的に二度と追いつけない」状態だけ。
//  2) 戻り方: 狙いの相手の間合いの外にいる時、従来は瞬間的に間合いへ戻していた(相手の切り替わりで数十mの瞬間移動)。
//     マルチでは、ボス自身が前へ進む速さを残したまま、間合いへ速度で戻る(後ろの人を待つように減速して見える)。
//  3) 位置の補正は、誰の画面にも映っていない時に限り、狙いの相手の画面のすぐ外まで(FarSnap以上離れた時だけ)。
//
// カード選択中の人は「待つ相手」に数えない(選択中は止まっているだけ。選び終えて走り出すと 1) が働く)。
// DOWN/脱落した人も数えない(倒れた人のためにボスを永遠に止めない)。シングル/JOINでは何もしない。
public static class BossLeash
{
    public const float LeashStart = 16f;        // ボスがこれ以上、最後尾の走っている人より前にいたら減速を始める(m。画面の半分ほど)
    public const float LeashRamp = 50f;         // ここまで離れると最大の減速
    public const float MaxSlow = 0.2f;          // 最大で並走速度の20%減(先頭の人はしばらくボスと戦える)
    public const float FallBehindSwitch = 6f;   // 狙いの相手よりこれだけ後ろになったら、最後尾の人へ狙いを替える
    public const float FarSnap = 70f;           // 間合いからこれ以上外れていて、誰にも見えていない時だけ位置を直す
    public const float FrontFightTime = 6f;     // 減速が続いてこの秒数たったら(先頭の人は十分戦った)、後ろの人へ狙いを替える
    public const float ArriveGap = 12f;         // 最後尾の人へ向かう時、ここまで近づいたら狙いの固定をやめる

    public struct Result
    {
        public bool Active;            // 1) が働いている
        public float SpeedFactor;      // 並走速度に掛ける(1=そのまま)
        public bool AllowBehindTarget; // 狙いの相手より後ろへ下がるのを許す(下限の間合いを外す)
        public int PreferTarget;       // 狙いを替えたい相手(0=替えない)
        public int RearPlayer;
        public float GapToRear;
    }

    public static bool Enabled => NetCombat.Authority && NetMatch.Active && !DebugDisabled;
    public static bool DebugDisabled;   // 自動テストの比較用: 3.1以前と同じ動き(-netAutoNoLeash)

    // 統計(負荷/動作の確認用)
    public static int LeashFrames, RetargetRequests, Snaps;

    // leashTime: ボスごとの「減速が続いている秒数」(各ボスが持つ)
    public static Result Evaluate(float bossX, Transform target, int targetPlayer, ref float leashTime)
    {
        var r = new Result { SpeedFactor = 1f };
        if (!Enabled) { leashTime = 0f; return r; }
        var list = NetTargets.Candidates(); // ALIVE・走行中・カード選択中でない人
        if (list.Count < 2) { leashTime = 0f; return r; }
        float rearX = float.MaxValue; int rearPn = 0;
        foreach (var c in list)
        {
            if (c.T == null) continue;
            float x = c.T.position.x;
            if (x < rearX - 0.01f || (Mathf.Abs(x - rearX) <= 0.01f && c.Player < rearPn)) { rearX = x; rearPn = c.Player; }
        }
        if (rearPn == 0) return r;
        r.RearPlayer = rearPn;
        r.GapToRear = bossX - rearX;
        if (targetPlayer == rearPn)
        {
            // 最後尾の人へ向かっている間は、その人に追いつくまで狙いを替えない(近い先頭の人へ戻って行ったり来たりしない)
            if (r.GapToRear > ArriveGap) r.PreferTarget = rearPn;
            leashTime = 0f;
            return r;
        }
        // 境目の上下で数え直さないよう、条件が外れている間はゆっくり減らす
        if (r.GapToRear <= LeashStart) { leashTime = Mathf.Max(0f, leashTime - Time.deltaTime * 0.5f); return r; }
        leashTime += Time.deltaTime;
        float t = Mathf.Clamp01((r.GapToRear - LeashStart) / LeashRamp);
        t = t * t * (3f - 2f * t);
        r.Active = true;
        r.SpeedFactor = 1f - MaxSlow * Mathf.Max(0.5f, t);
        r.AllowBehindTarget = true;
        if ((target != null && bossX < target.position.x - FallBehindSwitch) || leashTime >= FrontFightTime) { r.PreferTarget = rearPn; RetargetRequests++; }
        LeashFrames++;
        return r;
    }

    // 間合い(desiredMin..desiredMax: 狙いの相手からの差)の外にいる時の戻り方(マルチのHOST)。
    // 戻る相対速度は離れているほど速いが、ボスが後ろ向きに走らないよう「相手の速さの75%」までに抑える
    // (=後ろの人を待つように減速して見える)。前へ追いつく時は最大22m/sの上乗せ。
    // 誰にも見えていなくて FarSnap 以上外れている時だけ、狙いの相手の画面のすぐ外へ位置を直す。
    public static float Approach(float worldX, float targetX, float desiredMin, float desiredMax, float targetSpeed, float dt, bool allowBehind, float offscreenAheadGap)
    {
        float gap = worldX - targetX;
        float lo = allowBehind ? float.MinValue : desiredMin;
        if (gap >= lo && gap <= desiredMax) return worldX;
        float want = gap > desiredMax ? targetX + desiredMax : targetX + desiredMin;
        float excess = Mathf.Abs(worldX - want);
        if (excess > FarSnap && !WorldRange.VisibleToAnyone(worldX, -2f))
        {
            Snaps++;
            return gap > desiredMax ? targetX + Mathf.Max(desiredMax, offscreenAheadGap) : targetX + desiredMin;
        }
        float rate = gap > desiredMax
            ? Mathf.Min(Mathf.Max(2f, excess * 0.8f), Mathf.Max(3f, 0.75f * Mathf.Abs(targetSpeed)))
            : Mathf.Min(Mathf.Max(2f, excess * 1.5f), 22f);
        return Mathf.MoveTowards(worldX, want, rate * dt);
    }
}
