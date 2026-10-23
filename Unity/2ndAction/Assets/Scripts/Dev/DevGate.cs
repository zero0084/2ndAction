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
    public static bool On => Debug.isDebugBuild || CaptureBuild;
}

// ストア素材の撮影中(QaSweep -storeCapture)。開発用の表示(build の番号 / DEV BUILD / TEST FILE)を出さない。
// 製品のビルドでは誰も true にしない
public static class StoreCapture
{
    public static bool Clean;
}
