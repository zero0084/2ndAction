using UnityEngine;

// 開発用の機能(距離ワープ・解放の上書き など)を使えるか(2026-10-23)。
// 開発版(Editor / Development Build)に加えて、ストア素材の撮影用ビルド(OMM_CAPTURE)だけ。
// 撮影用ビルドは Unity の「Development Build」の透かしを出さないために開発版にしないので、Debug.isDebugBuild は false になる。
// 製品のビルド(Android / 将来の Steam)には OMM_CAPTURE を付けない → ここは Debug.isDebugBuild と同じ
public static class DevGate
{
#if OMM_CAPTURE
    public const bool CaptureBuild = true;
#else
    public const bool CaptureBuild = false;
#endif
#if OMM_RELEASE_PROBE
    public const bool ReleaseProbeBuild = true; // 2026-11-03: 敵の出現の確認用ビルド(配らない。ReleaseSpawnProbe が距離を進めるため)
#else
    public const bool ReleaseProbeBuild = false;
#endif
    public static bool On => Debug.isDebugBuild || CaptureBuild || ReleaseProbeBuild;
}

// ストア素材の撮影中(QaSweep -storeCapture)。開発用の表示(build の番号 / DEV BUILD / TEST FILE)を出さない。
// 製品のビルドでは誰も true にしない
public static class StoreCapture
{
    public static bool Clean;

    // 2026-11-09 画面の演出の時計: 1コマずつ撮る間(Recording、Time.captureFramerate)は、実時間ではなく動画の時間(1コマ = captureDeltaTime)で進める。
    //  撮影は実時間より遅い(1コマに約 80ms)ので、実時間で数える演出(合成/ガチャ/3択/画面の切り替え)が動画の中で縮んでいた。
    //  製品では Recording は誰も true にしない → UiDelta = Time.unscaledDeltaTime、UiTime = Time.unscaledTime(offset は 0 のまま)と同じ
    public static bool Recording;
    static double offset;
    static int tickFrame = -1;
    public static float UiDelta => Recording && Time.captureDeltaTime > 0f ? Time.captureDeltaTime : Time.unscaledDeltaTime;
    public static float UiTime => (float)(Time.unscaledTime + offset);
    // 撮影の書き出し(1コマに1回)から呼ぶ: 実時間との差を貯めて、UiTime を動画の時間で進める(途中で止めても続きの値になる)
    public static void Tick()
    {
        if (tickFrame == Time.frameCount) return;
        tickFrame = Time.frameCount;
        if (Recording && Time.captureDeltaTime > 0f) offset += Time.captureDeltaTime - Time.unscaledDeltaTime;
    }
}
