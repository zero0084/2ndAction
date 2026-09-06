using UnityEngine;

// Generates simple placeholder tones/loops in code so the prototype has
// audio feedback without needing any licensed music/SFX assets. Swap these
// calls out for Resources.Load<AudioClip>(...) later once real audio exists.
public static class AudioFactory
{
    const int SampleRate = 44100;

    public static AudioClip CreateJumpSe()
    {
        return CreateSweep("JumpSE", 300f, 750f, 0.15f, 0.5f);
    }

    public static AudioClip CreateAttackSe()
    {
        return CreatePercussive("AttackSE", 220f, 0.1f, 0.6f);
    }

    public static AudioClip CreateTitleBgm()
    {
        float[] notes = { 261.63f, 329.63f, 392.00f, 329.63f }; // C E G E, slow
        return CreateArpeggioLoop("TitleBGM", notes, 0.45f, 0.18f);
    }

    public static AudioClip CreateGameplayBgm()
    {
        float[] notes = { 293.66f, 349.23f, 440.00f, 349.23f, 293.66f, 220.00f }; // D F A F D A, faster
        return CreateArpeggioLoop("GameplayBGM", notes, 0.22f, 0.16f);
    }

    static AudioClip CreateSweep(string name, float freqStart, float freqEnd, float duration, float volume)
    {
        int sampleCount = Mathf.CeilToInt(SampleRate * duration);
        float[] data = new float[sampleCount];
        float phase = 0f;

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleCount;
            float freq = Mathf.Lerp(freqStart, freqEnd, t);
            phase += freq / SampleRate;
            float sample = Mathf.Sin(phase * Mathf.PI * 2f);
            float envelope = Mathf.Sin(t * Mathf.PI); // fades in/out, avoids clicks
            data[i] = sample * envelope * volume;
        }

        return BuildClip(name, data);
    }

    static AudioClip CreatePercussive(string name, float freq, float duration, float volume)
    {
        int sampleCount = Mathf.CeilToInt(SampleRate * duration);
        float[] data = new float[sampleCount];
        float phase = 0f;
        var rng = new System.Random(12345);

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleCount;
            phase += freq / SampleRate;
            float tone = Mathf.Sign(Mathf.Sin(phase * Mathf.PI * 2f));
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0) * 0.3f;
            float envelope = Mathf.Pow(1f - t, 3f); // fast decay, percussive "hit" feel
            data[i] = (tone * 0.7f + noise) * envelope * volume;
        }

        return BuildClip(name, data);
    }

    static AudioClip CreateArpeggioLoop(string name, float[] notes, float noteDuration, float volume)
    {
        int samplesPerNote = Mathf.CeilToInt(SampleRate * noteDuration);
        float[] data = new float[samplesPerNote * notes.Length];

        for (int n = 0; n < notes.Length; n++)
        {
            float freq = notes[n];
            int offset = n * samplesPerNote;
            for (int i = 0; i < samplesPerNote; i++)
            {
                float t = i / (float)samplesPerNote;
                float sample = Mathf.Sin(2f * Mathf.PI * freq * (i / (float)SampleRate));
                // Each note fades in/out to zero, so the loop point and note
                // boundaries are click-free.
                float envelope = Mathf.Sin(t * Mathf.PI);
                data[offset + i] = sample * envelope * volume;
            }
        }

        return BuildClip(name, data);
    }

    static AudioClip BuildClip(string name, float[] data)
    {
        AudioClip clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
