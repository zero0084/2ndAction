#if UNITY_EDITOR || DEVELOPMENT_BUILD
// 2026-11-25 セキュリティ監査: JOIN が改変した/壊れた知らせを送った時、HOST が受け入れない・落ちない・二重に報酬を出さないかの確認(開発版だけ)
//  -netAutoAbuseAt T   … JOIN で T 秒に、次の不正な知らせを HOST へ送る(結果は HOST のログの [NET][SEC] と ABUSE の行、teamrun/abuse.py で判定)
//   ① JOIN からのラン開始(HOST を別のステージへ読み直させる)  ② 座標が NaN の攻撃  ③ 存在しない敵の ID
//   ④ 1秒に 400 回の攻撃(1)  ⑤ 吸い寄せの速さ/秒数が巨大  ⑥ 同じ連番の攻撃の再送  ⑦ 最大HP 10億+全回復/回復 10億
//   ⑧ 範囲外の定型メッセージの ID  ⑨ 他人の分身の位置を送る(RPC は本人だけ)  ⑩ 脱出の距離 NaN → 10億(最後)
using System;
using System.Reflection;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public partial class NetAutoTest
{
    float abuseAt = -1f; bool abuseDone, abuseArgs;
    void AbuseTick()
    {
        if (!abuseArgs)
        {
            abuseArgs = true;
            string[] a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-netAutoAbuseAt") float.TryParse(a[i + 1], out abuseAt);
        }
        if (abuseAt < 0f || abuseDone || runTime < abuseAt || !NetCombat.Replica) return;
        abuseDone = true;
        var nm = NetSession.Manager; if (nm == null) return;
        var cmm = nm.CustomMessagingManager;
        ulong host = NetworkManager.ServerClientId;
        int seed = NetRunLauncher.ActiveRunSeed;
        int sent = 0;
        void Send(string msg, Action<FastBufferWriter> write)
        {
            using (var w = new FastBufferWriter(256, Allocator.Temp, 4096)) { write(w); cmm.SendNamedMessage(msg, host, w, NetworkDelivery.ReliableSequenced); sent++; }
        }
        void Hit(byte op, int id, int seq, int dmg, double cx, float cy, float vx, float vy, float vdur) => Send("OMM.CombatHit", w =>
        {
            w.WriteValueSafe(seed); w.WriteValueSafe(op); w.WriteValueSafe(id); w.WriteValueSafe(seq); w.WriteValueSafe(dmg); w.WriteValueSafe((byte)0);
            w.WriteValueSafe(cx); w.WriteValueSafe(cy); w.WriteValueSafe(vx); w.WriteValueSafe(vy); w.WriteValueSafe(vdur);
        });
        int enemy = -1;
        if (NetCombat.Instance != null) foreach (var kv in NetCombat.Instance.Entities) if (kv.Value != null && !kv.Value.Dead && kv.Value.Go != null && kv.Value.Enemy != null) { enemy = kv.Key; break; }
        L($"ABUSE start (enemy id {enemy})");
        // ① ラン開始(JOIN → HOST)
        Send("OMM.RunStart", w => { w.WriteValueSafe(new FixedString64Bytes("last_corridor")); w.WriteValueSafe(12345); w.WriteValueSafe(0.0); w.WriteValueSafe((byte)0); w.WriteValueSafe((byte)0); });
        // ② NaN の座標 ③ 存在しない敵
        int seq = 900000;
        if (enemy >= 0) Hit(1, enemy, ++seq, 5, double.NaN, float.NaN, 0f, 0f, 0f);
        Hit(1, 987654321, ++seq, 999999, 0.0, 0f, 0f, 0f, 0f);
        // ④ 1秒に 400 回(1ダメージずつ)
        if (enemy >= 0) for (int i = 0; i < 400; i++) Hit(1, enemy, ++seq, 1, 0.0, 0f, 0f, 0f, 0f);
        // ⑤ 巨大な吸い寄せ(op 2 = HitVacuum)
        if (enemy >= 0) Hit(2, enemy, ++seq, 1, 0.0, 0f, 1e9f, -1e9f, 1e6f);
        // ⑥ 同じ連番の再送
        if (enemy >= 0) { Hit(1, enemy, 950000, 1, 0.0, 0f, 0f, 0f, 0f); Hit(1, enemy, 950000, 1, 0.0, 0f, 0f, 0f, 0f); }
        // ⑦ 最大HP 10億 + 全回復 / 回復 10億(NetMatch の ReqSetMax=4 / ReqHeal=3)
        Send("OMM.MatchReq", w => { w.WriteValueSafe(seed); w.WriteValueSafe((byte)4); w.WriteValueSafe(1000000000); w.WriteValueSafe(true); });
        Send("OMM.MatchReq", w => { w.WriteValueSafe(seed); w.WriteValueSafe((byte)3); w.WriteValueSafe(1000000000); });
        // ⑧ 範囲外の定型メッセージ
        Send("OMM.QuickChat", w => { w.WriteValueSafe((byte)0); w.WriteValueSafe((byte)250); });
        // ⑨ 他人の分身の位置(本人ではない RPC は NGO が拒否する)
        int spoof = 0;
        foreach (NetPlayer p in NetPlayer.All)
        {
            if (p == null || p.IsOwner) continue;
            var m = typeof(NetPlayer).GetMethod("SnapshotToServerRpc", BindingFlags.Instance | BindingFlags.NonPublic);
            try { m?.Invoke(p, new object[] { new NetPlayerSnapshot { X = 1, Y = 1, Distance = 1000000, RunSpeed = float.NaN } }); spoof++; }
            catch (Exception e) { L($"ABUSE spoof rpc threw {e.GetBaseException().GetType().Name} (expected: not the owner)"); }
        }
        // ⑩ 脱出の距離(NaN → 10億)。最後に(この人は脱出の扱いになる)
        Send("OMM.MatchReq", w => { w.WriteValueSafe(seed); w.WriteValueSafe((byte)10); w.WriteValueSafe(double.NaN); });
        L($"ABUSE sent {sent} messages (spoof rpc tried {spoof})");
    }
}
#endif
