using UnityEngine;
using UnityEditor;

// Temporary diagnostic (Enemy Visual Size unification pass) - logs each
// grunt species' Sprite.bounds (the tight, alpha-cropped mesh bounds
// Unity's default SpriteMeshType.Tight produces) so the actual on-screen
// character height/width can be measured in world units, independent of
// each source PNG's raw canvas size (which includes padding/whitespace).
// Run via: Unity.exe -batchmode -quit -projectPath . -executeMethod EnemySizeProbe.Probe
public static class EnemySizeProbe
{
    static readonly (string label, string path)[] Paths =
    {
        ("goblin", "Assets/Art/Enemy/enemy_v1.png"),
        ("flying", "Assets/Art/Enemy/FlyingEnemy.png"),
        ("irregular", "Assets/Art/Enemy/IrregularEnemy.png"),
        ("shooter", "Assets/Art/Enemy/ShooterEnemy.png"),
        ("heavy", "Assets/Art/Enemy/HeavyEnemy.png"),
        ("runner", "Assets/Art/Enemy/RunnerEnemy.png"),
    };

    [MenuItem("Tools/OneMoreMile/Probe Enemy Sizes")]
    public static void Probe()
    {
        foreach (var (label, path) in Paths)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
            {
                Debug.Log($"[EnemySizeProbe] {label}: sprite not found at {path}");
                continue;
            }
            Debug.Log($"[EnemySizeProbe] {label}: bounds.size={sprite.bounds.size} rect={sprite.rect} ppu={sprite.pixelsPerUnit}");
        }
    }
}
