using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class VideoFrameExtractor
{
    [MenuItem("Tools/2ndAction/Extract Video Frames")]
    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject go = new GameObject("FrameExtractorRunner");
        FrameExtractorRunner runner = go.AddComponent<FrameExtractorRunner>();
        runner.jobs = new List<FrameExtractorRunner.Job>
        {
            new FrameExtractorRunner.Job
            {
                videoPath = @"C:\Users\0084k\Downloads\走る.mp4",
                outputDir = "Assets/Art/PlayerRunSource",
                frameCount = 8
            },
            new FrameExtractorRunner.Job
            {
                videoPath = @"C:\Users\0084k\Downloads\ジャンプ.mp4",
                outputDir = "Assets/Art/PlayerJumpSource",
                frameCount = 6
            },
            new FrameExtractorRunner.Job
            {
                videoPath = @"C:\Users\0084k\Downloads\攻撃.mp4",
                outputDir = "Assets/Art/PlayerAttackSource",
                frameCount = 6
            }
        };

        EditorApplication.isPlaying = true;
    }

    // One-off: extracts evenly spaced frames from a user-supplied gameplay
    // screen recording, for visually diagnosing a bug report (not part of
    // the actual game/asset pipeline).
    [MenuItem("Tools/2ndAction/Extract Diagnostic Video Frames")]
    public static void RunDiagnostic()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject go = new GameObject("FrameExtractorRunner");
        FrameExtractorRunner runner = go.AddComponent<FrameExtractorRunner>();
        runner.jobs = new List<FrameExtractorRunner.Job>
        {
            new FrameExtractorRunner.Job
            {
                videoPath = @"C:\Users\0084k\Downloads\screen-20260823-003122-1787412659309.mp4",
                outputDir = "Assets/_diag/NormalSpeed",
                frameCount = 20
            },
            new FrameExtractorRunner.Job
            {
                videoPath = @"C:\Users\0084k\Downloads\screen-20260823-003716-1787413029606.mp4",
                outputDir = "Assets/_diag/BossSpeed",
                frameCount = 30
            }
        };

        EditorApplication.isPlaying = true;
    }

    [MenuItem("Tools/2ndAction/Extract Dragon Video Frames")]
    public static void RunDragon()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject go = new GameObject("FrameExtractorRunner");
        FrameExtractorRunner runner = go.AddComponent<FrameExtractorRunner>();
        runner.jobs = new List<FrameExtractorRunner.Job>
        {
            new FrameExtractorRunner.Job
            {
                videoPath = @"C:\Users\0084k\Downloads\ドラゴン待機.mp4",
                outputDir = "Assets/Art/DragonIdleSource",
                frameCount = 6
            },
            new FrameExtractorRunner.Job
            {
                videoPath = @"C:\Users\0084k\Downloads\ドラゴン.mp4",
                outputDir = "Assets/Art/DragonChargeSource",
                frameCount = 8
            },
            new FrameExtractorRunner.Job
            {
                videoPath = @"C:\Users\0084k\Downloads\ドラゴン攻撃.mp4",
                outputDir = "Assets/Art/DragonFireSource",
                frameCount = 8
            }
        };

        EditorApplication.isPlaying = true;
    }
}
