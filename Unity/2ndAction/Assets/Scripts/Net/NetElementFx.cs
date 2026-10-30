using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// マルチの属性の見た目の同期(2026-10-30)。属性の判定・状態・ダメージは今までどおり HP を持つ HOST だけ(HOST 権威)。
// HOST は「状態の開始/延長」「落雷/連鎖の線」「竜巻/延焼/砕け」「風刃」「吸収」の小さな知らせをためて、0.1秒ごとに1通にまとめて送る。
// JOIN は知らせを受けて VFX/SE だけを再生する(ダメージも状態の判定もしない。継続中の粒は JOIN の側で動かす)。
//  ・同じ敵の同じ状態は、0.5秒以内の延長は送らない(1回の命中ごとに送らない)
//  ・1通に最大 MaxPerFlush 件。超えた分は捨てる(見た目だけなので、遅れて出すより捨てる)
//  ・位置は論理の X(場面の X + 浮動原点のずれ)と Y。敵/ボスは NetCombat の ID
public static class NetElementFx
{
    public const float FlushInterval = 0.1f;
    public const int MaxPerFlush = 64;
    public enum Kind : byte { State = 1, Bolt = 2, Tornado = 3, Inferno = 4, Shatter = 5, WindBlade = 6, Blood = 7 }
    public enum StateKind : byte { Burn = 1, Chill = 2, Freeze = 3, Bleed = 4, Charged = 5, Wind = 6 }

    // 確認用(-qaElemNet / マルチの試験)
    public static int Queued, Sent, Messages, Dropped, Received, Played, Missing;
    public static long BytesSent;

    struct Ev { public Kind k; public int id; public byte a, b; public double x1, x2; public float y1, y2; }
    static readonly List<Ev> buf = new List<Ev>(MaxPerFlush);
    static float flushAt;
    static readonly Dictionary<(int, byte), float> lastState = new Dictionary<(int, byte), float>();

    static bool Host => NetCombat.Authority;
    static int IdOf(Component c)
    {
        switch (c)
        {
            case EnemyController e: return e.NetId;
            case WildBossBase b: return b.NetId;
            case DragonController d: return d.NetId;
            case MajinController m: return m.NetId;
        }
        return 0;
    }
    static void Push(Ev e)
    {
        if (buf.Count >= MaxPerFlush) { Dropped++; return; }
        buf.Add(e); Queued++;
    }

    // ---------------------------------------------------------------- HOST: 知らせ
    public static void State(Component target, StateKind kind, float seconds, int tier)
    {
        if (!Host || target == null) return;
        int id = IdOf(target);
        if (id == 0) return;
        var key = (id, (byte)kind);
        if (kind != StateKind.Freeze && lastState.TryGetValue(key, out float t) && Time.time - t < 0.5f) return; // 延長は0.5秒に1回まで
        if (lastState.Count > 512) lastState.Clear();
        lastState[key] = Time.time;
        Push(new Ev { k = Kind.State, id = id, a = (byte)kind, b = (byte)(Mathf.Clamp(Mathf.RoundToInt(seconds * 10f), 1, 255) | 0), x1 = tier });
    }
    public static void Bolt(Vector3 a, Vector3 b, bool chain, int tier)
    {
        if (!Host) return;
        Push(new Ev { k = Kind.Bolt, a = (byte)(chain ? 1 : 0), b = (byte)tier, x1 = a.x + FloatingOrigin.Offset, y1 = a.y, x2 = b.x + FloatingOrigin.Offset, y2 = b.y });
    }
    public static void Point(Kind k, Vector3 at, float radius)
    {
        if (!Host) return;
        Push(new Ev { k = k, x1 = at.x + FloatingOrigin.Offset, y1 = at.y, a = (byte)Mathf.Clamp(Mathf.RoundToInt(radius * 20f), 1, 255) });
    }
    public static void Tornado(Vector3 at, float radius) => Point(Kind.Tornado, at, radius);
    public static void WindBlade(Vector3 at, float dir, float range)
    {
        if (!Host) return;
        Push(new Ev { k = Kind.WindBlade, x1 = at.x + FloatingOrigin.Offset, y1 = at.y, a = (byte)(dir < 0f ? 1 : 0), b = (byte)Mathf.Clamp(Mathf.RoundToInt(range * 10f), 1, 255) });
    }
    public static void Blood(Component from, bool hit)
    {
        if (!Host || from == null) return;
        int id = IdOf(from);
        if (id != 0) Push(new Ev { k = Kind.Blood, id = id, a = (byte)(hit ? 1 : 0) });
        else BloodAt(from.transform.position);
    }
    public static void BloodAt(Vector3 at)
    {
        if (!Host) return;
        Push(new Ev { k = Kind.Blood, id = 0, x1 = at.x + FloatingOrigin.Offset, y1 = at.y });
    }

    // HOST: NetCombat の LateUpdate から(0.1秒ごとに1通)
    public static void Tick()
    {
        if (!Host) { buf.Clear(); return; }
        if (buf.Count == 0 || Time.unscaledTime < flushAt) return;
        flushAt = Time.unscaledTime + FlushInterval;
        int n = buf.Count;
        NetCombat.SendElemFx(w =>
        {
            w.WriteValueSafe((ushort)n);
            foreach (var e in buf)
            {
                w.WriteValueSafe((byte)e.k);
                switch (e.k)
                {
                    case Kind.State: w.WriteValueSafe(e.id); w.WriteValueSafe(e.a); w.WriteValueSafe(e.b); w.WriteValueSafe((byte)e.x1); break;
                    case Kind.Bolt: w.WriteValueSafe(e.a); w.WriteValueSafe(e.b); w.WriteValueSafe(e.x1); w.WriteValueSafe(e.y1); w.WriteValueSafe(e.x2); w.WriteValueSafe(e.y2); break;
                    case Kind.Tornado: case Kind.Inferno: case Kind.Shatter: w.WriteValueSafe(e.x1); w.WriteValueSafe(e.y1); w.WriteValueSafe(e.a); break;
                    case Kind.WindBlade: w.WriteValueSafe(e.x1); w.WriteValueSafe(e.y1); w.WriteValueSafe(e.a); w.WriteValueSafe(e.b); break;
                    case Kind.Blood: w.WriteValueSafe(e.id); w.WriteValueSafe(e.a); if (e.id == 0) { w.WriteValueSafe(e.x1); w.WriteValueSafe(e.y1); } break;
                }
            }
        }, 8 + n * 34, out int bytes);
        Sent += n; Messages++; BytesSent += bytes;
        buf.Clear();
    }

    // ---------------------------------------------------------------- JOIN: 再生(見た目と音だけ)
    public static void Handle(FastBufferReader r)
    {
        r.ReadValueSafe(out ushort n);
        for (int i = 0; i < n; i++)
        {
            r.ReadValueSafe(out byte kb);
            var k = (Kind)kb;
            Received++;
            switch (k)
            {
                case Kind.State:
                {
                    r.ReadValueSafe(out int id); r.ReadValueSafe(out byte sk); r.ReadValueSafe(out byte tenths); r.ReadValueSafe(out byte tier);
                    var c = Resolve(id);
                    if (c == null) { Missing++; break; }
                    var st = ElementStatus.ForRemote(c);
                    st.RemoteState((StateKind)sk, tenths / 10f, tier);
                    Played++;
                    break;
                }
                case Kind.Bolt:
                {
                    r.ReadValueSafe(out byte chain); r.ReadValueSafe(out byte tier); r.ReadValueSafe(out double ax); r.ReadValueSafe(out float ay); r.ReadValueSafe(out double bx); r.ReadValueSafe(out float by);
                    ElementVfx.Bolt(new Vector3((float)(ax - FloatingOrigin.Offset), ay, 0f), new Vector3((float)(bx - FloatingOrigin.Offset), by, 0f), Mathf.Max(1, tier), chain == 1);
                    if (chain == 0) ElementVfx.Sfx(ElementType.Thunder, 0.08f);
                    Played++;
                    break;
                }
                case Kind.Tornado: case Kind.Inferno: case Kind.Shatter:
                {
                    r.ReadValueSafe(out double x); r.ReadValueSafe(out float y); r.ReadValueSafe(out byte rr);
                    var at = new Vector3((float)(x - FloatingOrigin.Offset), y, 0f); float radius = rr / 20f;
                    if (k == Kind.Tornado) ElementVfx.Tornado(at, radius, 0);
                    else if (k == Kind.Inferno) ElementVfx.InfernoBurst(at, radius);
                    else ElementVfx.Shatter(at, radius);
                    Played++;
                    break;
                }
                case Kind.WindBlade:
                {
                    r.ReadValueSafe(out double x); r.ReadValueSafe(out float y); r.ReadValueSafe(out byte left); r.ReadValueSafe(out byte range);
                    var at = new Vector3((float)(x - FloatingOrigin.Offset), y, 0f); float dir = left == 1 ? -1f : 1f;
                    ElementVfx.RemoteWindBlade(at, dir, range / 10f);
                    Played++;
                    break;
                }
                case Kind.Blood:
                {
                    r.ReadValueSafe(out int id); r.ReadValueSafe(out byte hit);
                    Vector3 at;
                    if (id == 0) { r.ReadValueSafe(out double x); r.ReadValueSafe(out float y); at = new Vector3((float)(x - FloatingOrigin.Offset), y, 0f); }
                    else { var c = Resolve(id); if (c == null) { Missing++; break; } at = ElementVfx.BoundsOf(c).center; }
                    ElementVfx.RemoteBlood(at);
                    Played++;
                    break;
                }
                default: return; // 知らない種類(版の違い): 残りは読まない
            }
        }
    }

    static Component Resolve(int id)
    {
        var e = NetCombat.Find(id);
        if (e == null || e.Go == null || e.Dead) return null;
        if (e.Enemy != null) return e.Enemy;
        if (e.Wild != null) return e.Wild;
        if (e.Dragon != null) return e.Dragon;
        if (e.Majin != null) return e.Majin;
        return null;
    }

    public static void ResetAll() { buf.Clear(); lastState.Clear(); Queued = Sent = Messages = Dropped = Received = Played = Missing = 0; BytesSent = 0; }
}
