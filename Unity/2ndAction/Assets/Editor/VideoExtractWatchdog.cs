using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class VideoExtractWatchdog
{
    static VideoExtractWatchdog()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode && Application.isBatchMode)
        {
            Debug.Log("VideoExtractWatchdog: exiting batch process after extraction");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorApplication.Exit(0);
        }
    }
}
