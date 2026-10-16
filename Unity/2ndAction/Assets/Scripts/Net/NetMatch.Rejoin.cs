using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// 協力マルチ: カード選択で大きく離れた人の復帰(2026-10-16 第6弾、マスターの依頼)
//  ・選択中はその場で止まる(今までどおり)ので、長く選ぶほど後ろになる(15秒で約450m)。
//  ・選択を閉じた時、HOST が「選択の間に広がった間合い」を測る。広がりが RejoinMinGain(120m)以上の時だけ戻す。
//    もともとの遅れ(選ぶ前の間合い)や、被弾/通常の遅れでは戻さない。
//  ・基準の仲間 = 前方の、生きていて選択中でない一番近い人。戻す先 = その人の「選ぶ前の間合い」だけ後ろ(最低 RejoinBehind 8m)。
//  ・HOST が自分の世界(地形は全端末で同じ作り、敵/攻撃は HOST が正)で安全な足場を探して決め、論理 X で本人へ送る。
//    穴/段差/天井/障害物/敵とボスの攻撃の範囲を避け、見つからなければ戻さない。
//  ・DOWN の人は戻さない(選択の終わりだけで復活/移動させない)。ランの終わりも戻さない。
//  ・飛ばした距離は距離の除外へ(距離の MILE/EXP/累計距離を新たに付けない。関門は先頭の人の距離で決まるので二重にならない)。
//  ・戻し終わって操作できるようになった時から、閉じた直後の保護(ChoiceExitGrace 1秒)を数える。
public partial class NetMatch
{
    public const float RejoinMinGain = 120f;  // 選択の間に広がった間合いがこれ以上なら戻す(普通の2〜3秒の選択は 30〜110m なので戻さない)
    public const float RejoinBehind = 8f;     // 戻す先は基準の仲間の最低これだけ後ろ
    const byte SyncRejoin = 8, ReqRejoinDone = 9;
    public static int RejoinOrders { get; private set; }     // 確認用(HOST が送った数)
    public static int RejoinSkipped { get; private set; }    // 確認用(安全な足場が無い等で戻さなかった)
    public static string LastRejoinNote { get; private set; } = "";
    int rejoinSerial;

    // HOST: 選択の始まり/終わり(HOST 自身の選択と、JOIN の申告の両方から呼ぶ)
    void HostOnChoosingChanged(Rec rec, bool choosing)
    {
        if (!NetCombat.Authority || rec == null) return;
        if (choosing)
        {
            rec.ChoiceStartX.Clear();
            foreach (var p in AllLogicalX()) rec.ChoiceStartX[p.Key] = p.Value;
            return;
        }
        if (Mode != MultiplayerGameMode.Coop) return;
        try { HostEvaluateRejoin(rec); }
        catch (System.Exception e) { Debug.LogWarning("[NET][REJOIN] " + e); }
    }

    // この端末から見た、参加中の各プレイヤーの論理 X(生きている人と DOWN の人)
    static Dictionary<int, float> AllLogicalX()
    {
        var d = new Dictionary<int, float>();
        var pc = PlayerController.Instance;
        if (pc != null) d[NetCombat.LocalPlayerNumber] = FloatingOrigin.ToLogical(pc.transform.position.x);
        foreach (NetPlayer p in NetPlayer.All)
        {
            if (p == null || p.IsOwner || p.PlayerNumber <= 0 || p.Avatar == null || !p.Avatar.HasRecentPosition) continue;
            d[p.PlayerNumber] = FloatingOrigin.ToLogical(p.Avatar.transform.position.x);
        }
        return d;
    }

    void HostEvaluateRejoin(Rec rec)
    {
        if (rec.State != PState.Alive || RunOver) { Note(rec, "not alive / run over -> no rejoin"); return; }
        var now = AllLogicalX();
        if (!now.TryGetValue(rec.Pn, out float meX)) return;
        // 基準: 前方の、生きていて選択中でない一番近い人
        int anchor = 0; float anchorX = float.MaxValue;
        foreach (var kv in now)
        {
            if (kv.Key == rec.Pn) continue;
            var r = Get(kv.Key);
            if (r == null || r.State != PState.Alive || r.Choosing) continue;
            if (kv.Value <= meX) continue;
            if (kv.Value < anchorX) { anchorX = kv.Value; anchor = kv.Key; }
        }
        if (anchor == 0) { Note(rec, "no alive ally ahead (all choosing/down/behind) -> no rejoin"); return; }
        float gapNow = anchorX - meX;
        float gapStart = rec.ChoiceStartX.TryGetValue(anchor, out float a0) && rec.ChoiceStartX.TryGetValue(rec.Pn, out float m0) ? a0 - m0 : gapNow;
        float gain = gapNow - Mathf.Max(0f, gapStart);
        if (gain < RejoinMinGain) { Note(rec, $"gap grew {gain:F0}m during the choice (< {RejoinMinGain:F0}m) -> stays"); return; }
        float want = Mathf.Max(gapStart, RejoinBehind);
        float targetLogical = anchorX - want;
        // HOST の世界で安全な足場を探す(戻す先から後ろへ。選んでいた位置の 20m 先より後ろには戻さない)
        float sceneTarget = targetLogical - (float)FloatingOrigin.Offset;
        float sceneFloor = (meX + 20f) - (float)FloatingOrigin.Offset;
        float? spot = RejoinSafety.FindSpot(sceneTarget, sceneFloor, out string why);
        if (!spot.HasValue) { RejoinSkipped++; Note(rec, $"no safe footing near {targetLogical:F0} ({why}) -> stays"); return; }
        float finalLogical = FloatingOrigin.ToLogical(spot.Value);
        int serial = ++rejoinSerial;
        RejoinOrders++;
        Note(rec, $"REJOIN P{rec.Pn}: gap to P{anchor} {gapStart:F0}m -> {gapNow:F0}m (+{gain:F0}m while choosing) -> place at {finalLogical:F0} ({anchorX - finalLogical:F0}m behind P{anchor}, moved {finalLogical - meX:F0}m) serial {serial}");
        if (rec.Pn == NetCombat.LocalPlayerNumber)
        {
            var pc = PlayerController.Instance;
            if (pc != null) pc.NetRejoinTo(spot.Value, serial);
        }
        else
        {
            byte pn = (byte)rec.Pn;
            SendToClients(w => { w.WriteValueSafe(SyncRejoin); w.WriteValueSafe(pn); w.WriteValueSafe(finalLogical); w.WriteValueSafe(serial); }, rec.ClientId);
        }
    }

    void Note(Rec rec, string msg) { LastRejoinNote = msg; Log($"[REJOIN] P{rec.Pn} {msg}"); }

    // JOIN: HOST からの復帰の指示
    void OnRejoinOrder(FastBufferReader r)
    {
        r.ReadValueSafe(out byte pn); r.ReadValueSafe(out float logicalX); r.ReadValueSafe(out int serial);
        if (pn != NetCombat.LocalPlayerNumber) return;
        var pc = PlayerController.Instance;
        if (pc == null) return;
        Log($"[REJOIN] order for P{pn}: place at {logicalX:F0} (serial {serial})");
        pc.NetRejoinTo(logicalX - (float)FloatingOrigin.Offset, serial);
    }

    // 戻し終わった(操作できるようになった)= ここから閉じた直後の保護を数える。HOST へ知らせる
    public static void ReportRejoinDone(int serial, float sceneX)
    {
        if (Instance == null) return;
        float logical = FloatingOrigin.ToLogical(sceneX);
        if (NetCombat.Authority)
        {
            var me = Get(NetCombat.LocalPlayerNumber);
            if (me != null) me.ChoiceEndedAt = Time.realtimeSinceStartup;
            Instance.Log($"[REJOIN] P{NetCombat.LocalPlayerNumber} done at {logical:F0} (serial {serial}) - grace starts now");
        }
        else SendToHost(w => { w.WriteValueSafe(ReqRejoinDone); w.WriteValueSafe(serial); w.WriteValueSafe(logical); });
    }

    void OnRejoinDoneRequest(Rec rec, FastBufferReader r)
    {
        r.ReadValueSafe(out int serial); r.ReadValueSafe(out float logical);
        rec.ChoiceEndedAt = Time.realtimeSinceStartup;
        Log($"[REJOIN] P{rec.Pn} done at {logical:F0} (serial {serial}) - grace starts now");
    }
}

// 復帰の足場の安全確認(HOST の世界で使う。地形は全端末で同じ作り)
public static class RejoinSafety
{
    // sceneTarget から後ろ(sceneFloor まで)へ 1m ずつ。最初に安全だった X(scene)。無ければ null
    public static float? FindSpot(float sceneTarget, float sceneFloor, out string why)
    {
        why = "";
        var tm = TerrainManager.Instance;
        if (tm == null) { why = "no terrain"; return null; }
        string first = null;
        for (float x = sceneTarget; x >= sceneFloor; x -= 1f)
        {
            if (IsSafe(tm, x, out string w)) return x;
            if (first == null) first = w;
        }
        why = first ?? "range empty";
        return null;
    }

    public static bool IsSafe(TerrainManager tm, float x, out string why)
    {
        why = "";
        float? h0 = tm.GetHeightAt(x);
        if (!h0.HasValue) { why = "pit/no ground"; return false; }
        // 穴/段差/壁: 前後の地面が続いていて平ら
        foreach (float dx in new[] { -1.5f, -0.75f, 0.75f, 1.5f, 3f, 5f })
        {
            float? h = tm.GetHeightAt(x + dx);
            if (!h.HasValue) { why = $"pit at {dx:+0.0;-0.0}m"; return false; }
            if (Mathf.Abs(h.Value - h0.Value) > 0.5f) { why = $"step/wall at {dx:+0.0;-0.0}m"; return false; }
        }
        float feet = h0.Value;
        // 天井(洞窟): 頭の上に十分な空き
        float? ceil = tm.GetCeilingHeightAt(x);
        if (ceil.HasValue && ceil.Value - feet < 3.2f) { why = "low ceiling"; return false; }
        // 障害物 / 敵 / ボス / 攻撃の判定 / 地形の危険
        var hits = Physics2D.OverlapBoxAll(new Vector2(x + 0.5f, feet + 1.2f), new Vector2(4f, 2.6f), 0f);
        foreach (var c in hits)
        {
            if (c == null || !c.enabled) continue;
            var go = c.gameObject;
            if (go.GetComponentInParent<ObstacleController>() != null) { why = "obstacle"; return false; }
            if (go.GetComponentInParent<TerrainWallHazard>() != null || go.GetComponentInParent<TrackedHazard>() != null) { why = "terrain hazard"; return false; }
            var e = go.GetComponentInParent<EnemyController>(); if (e != null && !e.IsDying) { why = "enemy"; return false; }
            var hb = go.GetComponentInParent<BossHitbox>(); if (hb != null && hb.IsActive) { why = "boss attack"; return false; }
            var hz = go.GetComponentInParent<CaveHazard>(); if (hz != null && hz.IsActive) { why = "hazard"; return false; }
            if (go.GetComponentInParent<BossProjectile>() != null || go.GetComponentInParent<FireballController>() != null) { why = "projectile"; return false; }
        }
        // ボスの体の近く(体当たり/近接の間合い)
        foreach (var b in BossBattle.Living)
        {
            if (b == null || !b.isActiveAndEnabled) continue;
            if (Mathf.Abs(b.transform.position.x - x) < 5f) { why = "next to a boss"; return false; }
        }
        // 予告中の危険(CaveHazard の予告: 有効になる前でも避ける)
        foreach (var h in CaveHazard.Live)
        {
            if (h == null || !h.IsActive) continue;
            var c = h.GetComponent<Collider2D>();
            if (c != null && c.bounds.min.x - 1.5f <= x && c.bounds.max.x + 1.5f >= x) { why = "hazard warning"; return false; }
        }
        return true;
    }
}
