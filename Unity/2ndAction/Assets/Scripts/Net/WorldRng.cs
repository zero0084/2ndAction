using UnityEngine;

// マルチプレイ対応Phase 1(2026-09-25) - 地形レイアウト専用の決定的乱数。
// シングルプレイ中(IsDeterministic=false)は各ストリームがUnityEngine.Randomをそのまま返すため、
// 既存の地形生成は乱数の消費順も含めて完全に従来どおり。
// マルチプレイRun中だけ、HOSTから共有されたWorldSeedで初期化した独立ストリームへ切り替え、
// 全端末が同じ地形(地面の起伏/穴/分岐/空中ルート/洞窟天井)を再現する。
// 敵・VFX・SE等はUnityEngine.Randomのまま(端末ごとに独立)なので、それらの乱数消費が
// 地形側のストリームをずらすことはない。系統ごとに別ストリームにしてあるのも同じ理由
// (例: 分岐の生成回数が端末間でずれても地面側の乱数列は影響を受けない)。
public static class WorldRng
{
    public static bool IsDeterministic { get; private set; }
    public static int Seed { get; private set; }

    public static readonly Stream Terrain = new Stream(0x1A2B3C4Du);
    public static readonly Stream Sky = new Stream(0x2B3C4D5Eu);
    public static readonly Stream Branch = new Stream(0x3C4D5E6Fu);
    public static readonly Stream Formation = new Stream(0x4D5E6F70u);
    public static readonly Stream Cave = new Stream(0x5E6F7081u);
    public static readonly Stream CaveDetail = new Stream(0x6F708192u);

    public static void BeginDeterministic(int seed)
    {
        Seed = seed;
        IsDeterministic = true;
        Terrain.Reset(seed);
        Sky.Reset(seed);
        Branch.Reset(seed);
        Formation.Reset(seed);
        Cave.Reset(seed);
        CaveDetail.Reset(seed);
    }

    public static void EndDeterministic()
    {
        IsDeterministic = false;
        Seed = 0;
    }

    public sealed class Stream
    {
        readonly uint salt;
        ulong state;

        public Stream(uint salt) { this.salt = salt; }

        public void Reset(int seed) => ResetRaw(((ulong)(uint)seed << 32) ^ salt);

        // 地点(例: 洞窟天井のノード番号)ごとに独立した乱数列へ切り替える。ある地点で
        // 端末ごとの事情(ボス戦付近の降格など)により消費回数がずれても、他の地点へ波及しない。
        public void ReseedAt(int key)
        {
            if (!IsDeterministic) return;
            ResetRaw(((ulong)(uint)Seed << 32) ^ ((ulong)(uint)key * 0xD6E8FEB86659FD93UL) ^ salt);
        }

        void ResetRaw(ulong mixed)
        {
            // SplitMix64で種を拡散してから xorshift64* の初期状態にする(0は不可)。
            ulong z = mixed ^ 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            state = z != 0 ? z : 0x9E3779B97F4A7C15UL;
        }

        ulong Next()
        {
            state ^= state >> 12;
            state ^= state << 25;
            state ^= state >> 27;
            return state * 2685821657736338717UL;
        }

        // [0, 1)
        public float Value => IsDeterministic ? (Next() >> 40) * (1f / 16777216f) : Random.value;

        public float Range(float min, float max) => IsDeterministic ? min + Value * (max - min) : Random.Range(min, max);

        // [min, maxExclusive) - UnityEngine.Random.Range(int, int)と同じ規約
        public int Range(int min, int maxExclusive)
        {
            if (!IsDeterministic) return Random.Range(min, maxExclusive);
            if (maxExclusive <= min) return min;
            return min + (int)(Next() % (ulong)(maxExclusive - min));
        }
    }
}
