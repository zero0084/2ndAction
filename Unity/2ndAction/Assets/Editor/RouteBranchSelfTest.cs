using System.Reflection;
using UnityEditor;
using UnityEngine;

// ルート構造再調整(2026-09-13) - TerrainManagerのRoute Branch生成
// (GenerateNextBranch)をEdit-mode単発実行で直接検証する。Start/Update/
// GenerateNextBranch等はすべてprivateなのでreflection経由で呼び出す -
// TerrainThemeSelfTestと同じ「Player/GameManager等シーン全体への依存を
// 避け、TerrainManager単体をEdit-mode上に生成して検証する」方針を踏襲。
public static class RouteBranchSelfTest
{
    [MenuItem("Tools/2ndAction/Self-Test: Route Branch")]
    public static void Run()
    {
        GameObject go = new GameObject("RouteBranchSelfTest_Terrain", typeof(TerrainManager));
        TerrainManager terrain = go.GetComponent<TerrainManager>();

        GameObject playerGO = new GameObject("RouteBranchSelfTest_Player");
        terrain.player = playerGO.transform;

        // テストを高速化するため短めの値に - forkX/mergeXは決定的
        // (branchStartDistance + branchRampLength*2 + branchLength)なので
        // 事前に正確な位置が分かる。
        terrain.branchStartDistance = 20f;
        terrain.branchLength = 20f;
        terrain.branchRampLength = 4f;
        terrain.branchSegmentLength = 8f;
        terrain.generateAheadDistance = 10f;

        InvokePrivate(terrain, "Start");

        // 1) routeBranchEnabled=falseのまま(天空回廊相当) - 分岐が一切
        // 生成されないこと。
        playerGO.transform.position = new Vector3(200f, 0f, 0f);
        InvokePrivate(terrain, "Update");
        bool noneWhenDisabled = !terrain.IsInBranchRoute(48f);

        // 2) ApplyStageThemeでenableRouteBranch=trueへ切り替え、プレイヤーを
        // 分岐終端より先まで進めてUpdate()を呼ぶと、分岐区間が実際に生成
        // されること。
        terrain.stageThemes = new[]
        {
            new TerrainManager.TerrainThemeSet
            {
                stageId = "test_wasteland",
                platformArt = default,
                groundSprite = null,
                groundColor = Color.white,
                backgroundSprite = null,
                decorationSprites = null,
                enableRouteBranch = true,
                branchMarkerSprite = null,
            }
        };
        terrain.ApplyStageTheme("test_wasteland");

        float forkX = 20f;
        float mergeX = forkX + 4f * 2f + 20f; // = 48
        playerGO.transform.position = new Vector3(mergeX + 20f, 0f, 0f);
        for (int i = 0; i < 5; i++) InvokePrivate(terrain, "Update");

        float midX = (forkX + mergeX) * 0.5f;
        bool foundBranch = terrain.IsInBranchRoute(midX);
        bool noneBeforeFork = !terrain.IsInBranchRoute(forkX - 5f);
        bool noneAfterMerge = !terrain.IsInBranchRoute(mergeX + 15f); // 次の分岐にはまだ早い位置

        float? skyAtMid = terrain.GetSkyHeightAt(midX);
        float? groundAtMid = terrain.GetHeightAt(midX);
        bool bothSurfacesExist = skyAtMid.HasValue && groundAtMid.HasValue;
        bool upperIsHigher = bothSurfacesExist && skyAtMid.Value > groundAtMid.Value;

        // 分岐直前・直後(フォーク/マージそのもの)では上ルートが存在しない
        // (地上と同じ高さで始まり/終わるだけ)ことを確認 - GetSkyHeightAtは
        // フォークのすぐ手前ではnullを返すはず。
        bool noSkyBeforeFork = !terrain.GetSkyHeightAt(forkX - 1f).HasValue;

        bool pass = noneWhenDisabled && foundBranch && noneBeforeFork && noneAfterMerge
            && bothSurfacesExist && upperIsHigher && noSkyBeforeFork;

        string result = pass ? "PASS" : "FAIL";
        Debug.Log($"[RouteBranchSelfTest] {result} - noneWhenDisabled={noneWhenDisabled} foundBranch={foundBranch} " +
                  $"noneBeforeFork={noneBeforeFork} noneAfterMerge={noneAfterMerge} bothSurfacesExist={bothSurfacesExist} " +
                  $"upperIsHigher={upperIsHigher} noSkyBeforeFork={noSkyBeforeFork}");

        if (!pass)
        {
            Debug.LogError("[RouteBranchSelfTest] FAIL - see individual flags above for which check failed.");
        }

        Object.DestroyImmediate(go);
        Object.DestroyImmediate(playerGO);
    }

    static void InvokePrivate(object target, string methodName)
    {
        MethodInfo m = target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (m == null)
        {
            Debug.LogError($"[RouteBranchSelfTest] Method not found: {methodName}");
            return;
        }
        m.Invoke(target, null);
    }
}
