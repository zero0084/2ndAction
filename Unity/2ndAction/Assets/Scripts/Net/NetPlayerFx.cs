using System.Collections.Generic;
using UnityEngine;

// マルチ 安定化(2026-10-11): ボスの攻撃がプレイヤーへ与える「ダメージ以外の効果」(風で押す・減速など)を、
// HOST / JOIN に関係なく、対象になったプレイヤー(P1〜P8 の任意)へ正しく効かせる。
//
//  以前: ボスの攻撃の処理(HOST)が PlayerController.Instance(= HOST 自身のプレイヤー)に直接掛けていた
//        → JOIN のプレイヤーには効かなかった(天空のタイタンの突風、天空の大蛇の嵐など)。
//  いま: HOST が「誰に・何を・どれだけ」を決める(権威は HOST)
//        → 対象が HOST 自身ならその場で掛ける / JOIN の人ならその端末へ通知し(NetMatch.SendPlayerFx)、
//          受け取った端末が自分のプレイヤーへ掛ける(1回だけ。二重に掛けない)。押された結果の位置は通常の位置同期で全員に共有。
//  シングルプレイ: 自分へそのまま掛ける(従来どおり)。LOCAL / ONLINE で同じ。
public static class NetPlayerFx
{
    public enum Kind : byte { Slow = 1, Knockback = 2 }

    // 効果の範囲にいる参加中の ALIVE のプレイヤー番号(シングルは 0 = 自分)。center: シーン座標X、halfWidth: 範囲の半分(m)
    public static List<int> PlayersNear(float centerSceneX, float halfWidth)
    {
        var list = new List<int>(8);
        if (!NetRunLauncher.IsMultiplayerRun || !NetSession.IsActive) { list.Add(0); return list; }
        foreach (var q in WorldRange.GetAlivePlayers())
            if (Mathf.Abs(q.SceneX - centerSceneX) <= halfWidth) list.Add(q.Pn);
        return list;
    }

    // HOST(またはシングル)から呼ぶ。pn = 0 はシングルの自分
    public static void Apply(int pn, Kind kind, float a, float b, string source)
    {
        bool multi = NetRunLauncher.IsMultiplayerRun && NetSession.IsActive;
        if (!multi || pn == 0 || pn == NetCombat.LocalPlayerNumber) { ApplyLocal(kind, a, b); return; }
        if (!NetCombat.Authority) return; // JOIN はボスの攻撃の結果を決めない(HOST から届いた分だけ掛ける)
        NetMatch.SendPlayerFx(pn, kind, a, b, source);
    }

    // 範囲の全員へ
    public static int ApplyNear(float centerSceneX, float halfWidth, Kind kind, float a, float b, string source)
    {
        var ps = PlayersNear(centerSceneX, halfWidth);
        foreach (int pn in ps) Apply(pn, kind, a, b, source);
        return ps.Count;
    }

    // この端末の自分のプレイヤーへ掛ける
    public static void ApplyLocal(Kind kind, float a, float b)
    {
        var pc = PlayerController.Instance;
        if (pc == null || pc.IsFinishing || pc.NetIsDowned) return;
        switch (kind)
        {
            case Kind.Slow: pc.ApplyMoveSlow(a, b); break;
            case Kind.Knockback: pc.ApplyKnockback(a, b); break;
        }
        LastLocalKind = kind; LastLocalTime = Time.time; LocalCount++;
    }

    // 自動テスト用
    public static Kind LastLocalKind; public static float LastLocalTime = -99f; public static int LocalCount;
}
