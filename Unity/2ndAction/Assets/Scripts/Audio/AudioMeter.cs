#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Concurrent;
using UnityEngine;

// 音の再設計(2026-10-06)の確認用(開発版だけ): 実際に出ている音(AudioListener の出力)の大きさを測る。
// OnAudioFilterRead は音の処理のスレッドで呼ばれるので、ブロックごとの RMS をキューへ入れ、メインスレッドで読む。
// 使い方: AudioMeter.Ensure() → Begin() → 数フレーム待つ → Collect() で区間の「一番大きい短い区間(約20ms)」と平均を得る。
public class AudioMeter : MonoBehaviour
{
    static AudioMeter inst;
    readonly ConcurrentQueue<float> blocks = new ConcurrentQueue<float>();
    volatile bool on;
    public static int SampleRate { get; private set; }

    public static AudioMeter Ensure()
    {
        var l = FindAnyObjectByType<AudioListener>();
        if (l == null) return null;
        if (inst != null && inst.gameObject == l.gameObject) return inst;
        inst = l.GetComponent<AudioMeter>();
        if (inst == null) inst = l.gameObject.AddComponent<AudioMeter>();
        SampleRate = AudioSettings.outputSampleRate;
        return inst;
    }

    public void Begin() { while (blocks.TryDequeue(out _)) { } on = true; }

    // 区間の測定結果(dBFS)。peak = 一番大きいブロックの RMS、mean = 全ブロックの RMS の平均(エネルギー)
    public void Collect(out float peakDb, out float meanDb, out int count)
    {
        on = false;
        float peak = 0f; double sum = 0; count = 0;
        while (blocks.TryDequeue(out float r)) { if (r > peak) peak = r; sum += r * r; count++; }
        peakDb = Db(peak);
        meanDb = count > 0 ? Db((float)System.Math.Sqrt(sum / count)) : -120f;
    }

    public static float Db(float x) => x <= 1e-6f ? -120f : 20f * Mathf.Log10(x);

    // 録音(マスターが実際の音を聞いて確かめる用): 出力をそのまま wav に書く
    readonly ConcurrentQueue<float[]> rec = new ConcurrentQueue<float[]>();
    volatile bool recording; int recChannels = 2;
    public void StartRecording() { while (rec.TryDequeue(out _)) { } recording = true; }
    public float StopRecording(string path)
    {
        recording = false;
        var all = new System.Collections.Generic.List<float>();
        while (rec.TryDequeue(out var b)) all.AddRange(b);
        int sr = AudioSettings.outputSampleRate, ch = recChannels;
        using (var fs = new System.IO.FileStream(path, System.IO.FileMode.Create))
        using (var w = new System.IO.BinaryWriter(fs))
        {
            int bytes = all.Count * 2;
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes); w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16); w.Write((short)1); w.Write((short)ch); w.Write(sr); w.Write(sr * ch * 2); w.Write((short)(ch * 2)); w.Write((short)16);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(bytes);
            foreach (float f in all) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(f * 32767f), -32768, 32767));
        }
        return all.Count / (float)(sr * ch);
    }

    void OnAudioFilterRead(float[] data, int channels)
    {
        if (recording) { recChannels = channels; rec.Enqueue((float[])data.Clone()); }
        if (!on || data.Length == 0) return;
        double s = 0;
        for (int i = 0; i < data.Length; i++) s += data[i] * data[i];
        blocks.Enqueue((float)System.Math.Sqrt(s / data.Length));
    }
}
#endif
