using UnityEngine;

// カード合成演出のSE(手続き生成、SkyBossSfxと同じ作り)。
public static class FusionSfx
{
    const int Rate = 22050;
    static AudioClip charge, success, fail, burst, bigBurst, coins, tap;

    public static void Play(AudioClip clip, float volume = 0.8f)
    {
        if (clip == null || AudioManager.Instance == null) return;
        AudioManager.Instance.PlaySfxVolume(clip, volume);
    }

    static AudioClip Make(string name, float seconds, System.Func<float, float, float> sample)
    {
        int n = Mathf.Max(1, Mathf.RoundToInt(seconds * Rate));
        float[] data = new float[n];
        for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(sample(i / (float)Rate, i / (float)n), -1f, 1f);
        var clip = AudioClip.Create(name, n, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    static float Noise(int i) { uint x = (uint)i * 747796405u + 2891336453u; x = ((x >> (int)((x >> 28) + 4u)) ^ x) * 277803737u; x = (x >> 22) ^ x; return (x / (float)uint.MaxValue) * 2f - 1f; }
    static float Sine(float hz, float t) => Mathf.Sin(2f * Mathf.PI * hz * t);

    // 魔法陣に光が集まる: 上昇していくうなり
    public static AudioClip Charge() => AudioManager.LibraryClip(SeId.CardFusion) ?? (charge != null ? charge : charge = Make("FusionCharge", 1.1f, (t, f) =>
    {
        float hz = Mathf.Lerp(180f, 520f, f * f);
        float env = Mathf.SmoothStep(0f, 1f, f * 3f) * (1f - Mathf.SmoothStep(0.85f, 1f, f));
        return (Sine(hz, t) * 0.35f + Sine(hz * 1.5f, t) * 0.18f + Sine(hz * 2.01f, t) * 0.1f) * env * 0.6f;
    }));

    // 継承成功: 明るいチャイム
    public static AudioClip Success() => AudioManager.LibraryClip(SeId.FusionSuccess) ?? (success != null ? success : success = Make("FusionSuccess", 0.6f, (t, f) =>
    {
        float env = Mathf.Exp(-t * 6f);
        return (Sine(1318.5f, t) * 0.4f + Sine(1975.5f, t) * 0.25f + Sine(2637f, t) * 0.12f) * env * 0.6f;
    }));

    // 継承失敗: 低く鈍い音
    public static AudioClip Fail() => AudioManager.LibraryClip(SeId.FusionFail) ?? (fail != null ? fail : fail = Make("FusionFail", 0.5f, (t, f) =>
    {
        float hz = Mathf.Lerp(220f, 110f, f);
        return (Sine(hz, t) * 0.5f + Noise((int)(t * Rate)) * 0.05f) * Mathf.Exp(-t * 7f) * 0.7f;
    }));

    // 完成カードの光のバースト
    public static AudioClip Burst() => burst != null ? burst : burst = MakeBurst("FusionBurst", 1f);
    public static AudioClip BigBurst() => bigBurst != null ? bigBurst : bigBurst = MakeBurst("FusionBigBurst", 1.6f);

    static AudioClip MakeBurst(string name, float richness)
    {
        float lp = 0f; int idx = 0;
        return Make(name, 1.3f, (t, f) =>
        {
            float n = Noise(idx++); lp += (n - lp) * 0.15f;
            float whoosh = lp * 1.6f * Mathf.Exp(-t * 5f);
            float chord = (Sine(523.25f, t) + Sine(659.25f, t) * 0.8f + Sine(783.99f, t) * 0.7f + Sine(1046.5f, t) * 0.5f * richness) * 0.18f;
            float shimmer = richness > 1.2f ? Sine(2093f + 40f * Sine(7f, t), t) * 0.08f : 0f;
            return (whoosh + (chord + shimmer) * Mathf.Exp(-t * 2.2f)) * 0.7f;
        });
    }

    // 全失敗 → MILE還元: きらきらしたコイン音
    public static AudioClip Coins() => coins != null ? coins : coins = Make("FusionCoins", 0.9f, (t, f) =>
    {
        float v = 0f;
        for (int k = 0; k < 6; k++)
        {
            float st = k * 0.11f;
            if (t < st) continue;
            float tt = t - st;
            v += Sine(1567.98f + k * 180f, tt) * Mathf.Exp(-tt * 14f) * 0.25f;
        }
        return v * 0.8f;
    });

    public static AudioClip Tap() => tap != null ? tap : tap = Make("FusionTap", 0.08f, (t, f) => Sine(880f, t) * (1f - f) * 0.25f);
}
