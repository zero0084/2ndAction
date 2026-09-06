using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Video;
#if UNITY_EDITOR
using UnityEditor;
#endif

// One-off dev tool: plays each source video in Play Mode and grabs evenly
// spaced frames as PNGs, since no ffmpeg/python is available on this machine
// to do it externally. Not used by the actual game.
public class FrameExtractorRunner : MonoBehaviour
{
    [Serializable]
    public class Job
    {
        public string videoPath;
        public string outputDir;
        public int frameCount = 8;
    }

    public List<Job> jobs;

    int jobIndex = -1;
    VideoPlayer vp;
    RenderTexture rt;
    float captureInterval;
    float nextCaptureTime;
    int capturedCount;

    void Start()
    {
        StartNextJob();
    }

    void StartNextJob()
    {
        jobIndex++;
        if (jobs == null || jobIndex >= jobs.Count)
        {
            Debug.Log("FrameExtractorRunner: all jobs complete");
#if UNITY_EDITOR
            EditorApplication.isPlaying = false;
#endif
            return;
        }

        Job job = jobs[jobIndex];
        Directory.CreateDirectory(job.outputDir);

        if (vp != null) Destroy(vp);
        vp = gameObject.AddComponent<VideoPlayer>();
        vp.playOnAwake = false;
        vp.source = VideoSource.Url;
        vp.url = job.videoPath;
        vp.renderMode = VideoRenderMode.RenderTexture;
        vp.isLooping = false;
        vp.audioOutputMode = VideoAudioOutputMode.None;

        capturedCount = 0;
        vp.prepareCompleted += OnPrepared;
        vp.errorReceived += OnError;
        vp.Prepare();
    }

    void OnError(VideoPlayer source, string message)
    {
        Debug.LogError("FrameExtractorRunner: video error: " + message);
    }

    void OnPrepared(VideoPlayer source)
    {
        source.prepareCompleted -= OnPrepared;

        int w = (int)source.width;
        int h = (int)source.height;
        rt = new RenderTexture(w, h, 0);
        source.targetTexture = rt;

        Job job = jobs[jobIndex];
        double length = source.length;
        captureInterval = length > 0 ? (float)(length / job.frameCount) : 0.1f;
        nextCaptureTime = 0f;

        Debug.Log($"FrameExtractorRunner: prepared {job.videoPath} {w}x{h} length={length}");

        source.Play();
    }

    void Update()
    {
        if (vp == null || !vp.isPrepared) return;
        Job job = jobs[jobIndex];

        if (vp.isPlaying && vp.time >= nextCaptureTime && capturedCount < job.frameCount)
        {
            CaptureFrame(job);
            nextCaptureTime += captureInterval;
        }

        bool doneCapturing = capturedCount >= job.frameCount;
        bool videoEnded = !vp.isPlaying && vp.frame > 0;

        if (doneCapturing || videoEnded)
        {
            vp.Stop();
            Debug.Log($"FrameExtractorRunner: job done, captured={capturedCount}");
            StartNextJob();
        }
    }

    void CaptureFrame(Job job)
    {
        RenderTexture prevActive = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prevActive;

        byte[] png = tex.EncodeToPNG();
        string path = Path.Combine(job.outputDir, $"frame_{capturedCount:00}.png");
        File.WriteAllBytes(path, png);
        UnityEngine.Object.Destroy(tex);

        capturedCount++;
    }
}
