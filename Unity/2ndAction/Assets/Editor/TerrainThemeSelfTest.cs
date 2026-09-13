using UnityEditor;
using UnityEngine;

// ステージ別ビジュアル差し替え(2026-09-13) - TerrainManager.ApplyStageTheme
// の核心ロジックをEdit-mode単発実行で直接検証する。チャンクが1つも存在
// しない(RebuildAllChunkVisualsが空リストに対して安全にno-opであること
// 自体も間接的に検証される)状態でのフィールド差し替えのみを対象にする -
// 実際にチャンクを生成するにはPlayer/GameManager等シーン全体の依存が
// 必要で、他のTerrain関連の自己診断テストが未実装なのと同じ理由により
// このセッションでは対象外(既知の限界)。
public static class TerrainThemeSelfTest
{
    [MenuItem("Tools/2ndAction/Self-Test: Terrain Theme")]
    public static void Run()
    {
        GameObject go = new GameObject("TerrainThemeSelfTest_Terrain", typeof(TerrainManager));
        TerrainManager terrain = go.GetComponent<TerrainManager>();

        Color defaultColor = terrain.groundColor;
        Sprite defaultGroundSprite = terrain.groundSprite;
        Sprite defaultGroundFillSprite = terrain.groundFillSprite;

        var wastelandGroundSprite = Sprite.Create(new Texture2D(4, 4), new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
        Color wastelandColor = new Color(0.4f, 0.6f, 0.2f);
        // 荒野街道 地面埋め修整(2026-09-13深夜) - groundFillSpriteも他の
        // フィールドと同じ「未指定なら無変更、指定時のみ差し替え」パター
        // ンで検証する。
        var wastelandGroundFillSprite = Sprite.Create(new Texture2D(4, 4), new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));

        terrain.stageThemes = new[]
        {
            new TerrainManager.TerrainThemeSet
            {
                stageId = "wasteland_road",
                platformArt = default,
                groundSprite = wastelandGroundSprite,
                groundColor = wastelandColor,
                backgroundSprite = null,
                groundFillSprite = wastelandGroundFillSprite,
            }
        };

        // 1) 未知のstageId(天空回廊 - stageThemesにエントリを追加していない
        // 想定)を渡しても、既存の値を一切変更しないこと(「無改造で温存」
        // という要件の直接検証)。
        terrain.ApplyStageTheme("sky_corridor");
        bool unknownStageUnchanged = terrain.groundColor == defaultColor && terrain.groundSprite == defaultGroundSprite
            && terrain.groundFillSprite == defaultGroundFillSprite;

        // 2) 一致するstageIdを渡すと、そのエントリの値へ実際に差し替わる
        // こと。
        terrain.ApplyStageTheme("wasteland_road");
        bool matchingStageApplied = terrain.groundColor == wastelandColor && terrain.groundSprite == wastelandGroundSprite
            && terrain.groundFillSprite == wastelandGroundFillSprite;

        bool pass = unknownStageUnchanged && matchingStageApplied;

        string result = pass ? "PASS" : "FAIL";
        Debug.Log($"[TerrainThemeSelfTest] {result} - unknownStageUnchanged={unknownStageUnchanged} matchingStageApplied={matchingStageApplied}");

        if (!pass)
        {
            Debug.LogError("[TerrainThemeSelfTest] FAIL - ApplyStageTheme did not behave as expected for an " +
                            "unknown stageId (should leave fields untouched) or a matching one (should swap them).");
        }

        Object.DestroyImmediate(go);
    }
}
