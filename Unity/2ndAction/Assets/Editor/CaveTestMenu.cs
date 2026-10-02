using UnityEditor;

// Cave stage test helper (Editor only): forces section types so spikes / low ceilings show up quickly.
public static class CaveTestMenu
{
    public const string Key = "CaveTestMode";
    [MenuItem("Tools/OneMoreMile/Cave Test/Normal")] static void Normal() { EditorPrefs.SetInt(Key, 0); }
    [MenuItem("Tools/OneMoreMile/Cave Test/Spikes Only")] static void Spikes() { EditorPrefs.SetInt(Key, 1); }
    [MenuItem("Tools/OneMoreMile/Cave Test/Low Ceiling Only")] static void Low() { EditorPrefs.SetInt(Key, 2); }
    [MenuItem("Tools/OneMoreMile/Cave Test/Auto Test On Next Run")] static void Auto() { EditorPrefs.SetInt("CaveAutoTest", 1); }
}
