using System.Reflection;
using UnityEditor;
using UnityEngine;

// Stage01次段階調整(2026-09-16), item1/3/4 - GenerateNextBranchのforkX/
// mergeXは、branchRanges登録によりその時点で既にIsInBranchRoute==trueと
// なり(GetPitChanceの危険度ブーストを受ける)、運悪くPitチャンクとして
// 生成されてしまうことがあり得た(TerrainManager.EnsureSolidGroundAtの
// コメント参照)。この自己診断テストは、Pit発生確率をほぼ確実(0.95)まで
// 引き上げた上でGenerateNextBranchを多数回(20回)直接呼び出し、毎回
// forkX/mergeXの両方でGetHeightAtが必ず有効な高さを返す(=決してPitの
// 上に置かれない)ことを検証する - EnsureSolidGroundAtが無ければ、この
// 確率設定ではほぼ確実にどこかで失敗するはずの統計的な回帰テスト。
public static class RouteBranchPitSafetySelfTest
{
    [MenuItem("Tools/2ndAction/Self-Test: Route Branch Pit Safety")]
    public static void Run()
    {
        GameObject go = new GameObject("RouteBranchPitSafetySelfTest_Terrain", typeof(TerrainManager));
        TerrainManager terrain = go.GetComponent<TerrainManager>();

        GameObject playerGO = new GameObject("RouteBranchPitSafetySelfTest_Player");
        terrain.player = playerGO.transform;

        // テストを高速化する短めの区間長 + Pitがほぼ確実に出る確率設定。
        terrain.branchStartDistance = 20f;
        terrain.branchLength = 20f;
        terrain.branchRampLength = 4f;
        terrain.branchSegmentLength = 8f;
        terrain.branchMinInterval = 30f;
        terrain.branchMaxInterval = 40f;
        terrain.generateAheadDistance = 10f;
        terrain.pitChanceBase = 0.95f;
        terrain.pitChanceMax = 0.97f;
        terrain.pitChanceRampPer1000m = 0f;

        InvokePrivate(terrain, "Start");

        terrain.stageThemes = new[]
        {
            new TerrainManager.TerrainThemeSet
            {
                stageId = "test_wasteland_pit",
                platformArt = default,
                groundSprite = null,
                groundColor = Color.white,
                backgroundSprite = null,
                decorationSprites = null,
                enableRouteBranch = true,
                branchMarkerSprite = null,
            }
        };
        terrain.ApplyStageTheme("test_wasteland_pit");

        const int branchCount = 20;
        bool pass = true;
        string failDetail = "";
        for (int i = 0; i < branchCount; i++)
        {
            float forkX = GetPrivateFloat(terrain, "nextBranchX");
            float mergeX = forkX + terrain.branchRampLength * 2f + terrain.branchLength;

            InvokePrivate(terrain, "GenerateNextBranch");

            float? forkHeight = terrain.GetHeightAt(forkX);
            float? mergeHeight = terrain.GetHeightAt(mergeX);
            if (!forkHeight.HasValue || !mergeHeight.HasValue)
            {
                pass = false;
                failDetail = $"branch #{i}: forkX={forkX:F1} forkHeight={(forkHeight.HasValue ? forkHeight.Value.ToString("F2") : "null")} " +
                             $"mergeX={mergeX:F1} mergeHeight={(mergeHeight.HasValue ? mergeHeight.Value.ToString("F2") : "null")}";
                break;
            }
        }

        string result = pass ? "PASS" : "FAIL";
        Debug.Log($"[RouteBranchPitSafetySelfTest] {result} - checked {branchCount} branches with pitChanceBase=0.95" +
                  (pass ? "" : $" - {failDetail}"));

        if (!pass)
        {
            Debug.LogError("[RouteBranchPitSafetySelfTest] FAIL - a branch fork/merge point landed on a Pit chunk " +
                            "(GetHeightAt returned null there) - EnsureSolidGroundAt should make this impossible. " +
                            failDetail);
        }

        Object.DestroyImmediate(go);
        Object.DestroyImmediate(playerGO);
    }

    static void InvokePrivate(object target, string methodName)
    {
        MethodInfo m = target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (m == null)
        {
            Debug.LogError($"[RouteBranchPitSafetySelfTest] Method not found: {methodName}");
            return;
        }
        m.Invoke(target, null);
    }

    static float GetPrivateFloat(object target, string fieldName)
    {
        FieldInfo f = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (f == null)
        {
            Debug.LogError($"[RouteBranchPitSafetySelfTest] Field not found: {fieldName}");
            return 0f;
        }
        return (float)f.GetValue(target);
    }
}
