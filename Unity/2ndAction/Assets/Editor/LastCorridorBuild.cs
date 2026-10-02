using UnityEditor;
using UnityEngine;

// LAST CORRIDOR(2026-09-29): 追加に必要なデータ作成をまとめて行う(batch用)。
//  Encounter Profile(無ければ作る) → AudioLibraryのステージ枠(無ければ足す) → シーン再構築(テーマ/障害物/演出担当) → ステージ一覧の自己テスト
public static class LastCorridorBuild
{
    [MenuItem("Tools/OneMoreMile/Last Corridor/Build All Data + Scene")]
    public static void Run()
    {
        EncounterProfileBuilder.Build();
        StageEncounterProfileReset();
        AudioLibraryBuilder.BuildBatch();
        SceneBuilder.Build();
        StageDatabaseSelfTest.Run();
        Debug.Log("[LastCorridorBuild] done");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    // Profileの読み込みキャッシュ(Editor上で続けて実行した時のため)
    static void StageEncounterProfileReset()
    {
        var f = typeof(StageEncounterProfile).GetField("byStage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (f != null) f.SetValue(null, null);
    }
}
