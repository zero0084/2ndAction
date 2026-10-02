using UnityEditor;
using UnityEditor.Build.Player;
using UnityEngine;

// リリース版(DEVELOPMENT_BUILD なし)のスクリプトだけをコンパイルして、通るか / 開発用の機能が入っていないかを確かめる(2026-10-02)。
// "Unity.exe -batchmode -projectPath ... -executeMethod ReleaseCompileCheck.Run -quit"
// 出力: Builds/ReleaseCompileCheck/ (Assembly-CSharp.dll)。開発用の型(DebugPanel/EndgameDebug等)が含まれていないことは呼び出し側で調べる。
public static class ReleaseCompileCheck
{
    public static void Run()
    {
        var settings = new ScriptCompilationSettings
        {
            target = BuildTarget.StandaloneWindows64,
            group = BuildTargetGroup.Standalone,
            options = ScriptCompilationOptions.None, // DEVELOPMENT_BUILD を定義しない
        };
        var result = PlayerBuildInterface.CompilePlayerScripts(settings, "Builds/ReleaseCompileCheck");
        int n = result.assemblies != null ? result.assemblies.Count : 0;
        Debug.Log($"[ReleaseCompileCheck] compiled assemblies={n}: {(n > 0 ? string.Join(", ", result.assemblies) : "(none - compile failed)")}");
        EditorApplication.Exit(n > 0 ? 0 : 1);
    }
}
