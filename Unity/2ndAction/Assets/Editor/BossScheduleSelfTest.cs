using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

// 1000m周期ボススケジュールの階層(10,000m専用 > 5,000m > 1,000m)と複数出現数の自己テスト。
public static class BossScheduleSelfTest
{
    public static void Run()
    {
        var go = new GameObject("BossManagerTest");
        var bm = go.AddComponent<BossManager>();
        MethodInfo m = typeof(BossManager).GetMethod("ResolveGate", BindingFlags.NonPublic | BindingFlags.Instance);
        var sb = new StringBuilder();
        bool ok = true;
        void Check(int k, string expectKind, int expectCount, bool expectGate = true)
        {
            object[] args = { k, WildBossKind.Wolf, 0 };
            bool gate = (bool)m.Invoke(bm, args);
            string kind = args[1].ToString(); int count = (int)args[2];
            bool pass = gate == expectGate && (!expectGate || (kind == expectKind && count == expectCount));
            if (!pass) ok = false;
            sb.AppendLine($"  {k}000m -> gate={gate} {kind} x{count} {(pass ? "OK" : "NG expected " + expectKind + " x" + expectCount)}");
        }
        Check(1, "Wolf", 1); Check(2, "Wolf", 1); Check(4, "Wolf", 1); Check(5, "GoblinRider", 1);
        Check(6, "Wolf", 1); Check(10, "Serpent", 1); Check(15, "GoblinRider", 1); // 15,000m: 5,000m系(riderStep 20,000未満)
        Check(20, "Cyclops", 1); Check(30, "Spider", 1); Check(40, "Golem", 1); Check(50, "Griffin", 1);
        Check(60, "Hydra", 1); Check(70, "Demon", 1); Check(80, "Dragon", 1); Check(90, "BlackKnight", 1);
        Check(100, "", 0, false); // 100,000m: 死神(別系統)なので通常ゲートなし
        Check(12, "Wolf", 2); Check(24, "Wolf", 3); Check(48, "Wolf", 4); Check(99, "Wolf", 4); // 最大4体
        Check(25, "GoblinRider", 2); Check(45, "GoblinRider", 3); Check(95, "GoblinRider", 3);
        Object.DestroyImmediate(go);
        Debug.Log("[BossScheduleSelfTest] " + (ok ? "PASS" : "FAIL") + "\n" + sb);
    }
}
