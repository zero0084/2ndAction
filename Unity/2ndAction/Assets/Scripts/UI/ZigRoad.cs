using UnityEngine;

// 縦画面「斜め上から見る」のジグザグの道(2026-10-09、依頼G-3)。表示だけ(判定/位置/敵の順番/出現は変えない)。
//  ・標準のスプライトのマテリアル(全部の SpriteRenderer が共有する Sprites-Default)のシェーダーを、起動時に SpriteZig へ差し替える
//    (SpriteZig は _ZigOn が 0 の間 Sprites/Default と同じ結果)。光る残像/縁取り(SpriteGlow/SpriteRim)も同じ曲げ方。
//  ・斜めカメラが動いている間だけ PortraitCameraRig が Apply(true, ...) で曲げを入れる。横から見る/横画面では 0。
//  ・背景の空の1枚(BackgroundFollower)は曲げない(NoZigMaterial)。
public static class ZigRoad
{
    static Shader original, zig;
    static Material noZig;
    public static bool Installed { get; private set; }
    public static bool Enabled = false;

    // 調整値(開発用の起動引数 -zigAmp -zigStart -zigRamp -zigWave で変えられる)
    public static float Amp = 5f, Start = 7f, Ramp = 16f, Wave = 42f;

    static readonly int IdOn = Shader.PropertyToID("_ZigOn"), IdAmp = Shader.PropertyToID("_ZigAmp"), IdStart = Shader.PropertyToID("_ZigStart"),
        IdRamp = Shader.PropertyToID("_ZigRamp"), IdWave = Shader.PropertyToID("_ZigWave"), IdOrigin = Shader.PropertyToID("_ZigOriginX"), IdPhase = Shader.PropertyToID("_ZigPhase");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Install()
    {
        Shader.SetGlobalFloat(IdOn, 0f);
        // 試作段階のため既定では入れない(開発用の起動引数 -zig 1 の時だけ)。方式の確定後に既定を決める
        bool want = Enabled;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var args0 = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args0.Length - 1; i++) if (args0[i] == "-zig") want = args0[i + 1] != "0";
#endif
        if (!want) return;
        zig = Resources.Load<Shader>("Shaders/SpriteZig");
        if (zig == null || !zig.isSupported) { Debug.LogWarning("[ZigRoad] SpriteZig shader missing/unsupported - zigzag view disabled"); return; }
        var tmp = new GameObject("ZigRoadProbe").AddComponent<SpriteRenderer>();
        var def = tmp.sharedMaterial;
        Object.Destroy(tmp.gameObject);
        if (def == null) return;
        original = def.shader;
        noZig = new Material(original) { name = "Sprites-Default (no zig)" };
        def.shader = zig;
        Installed = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++)
        {
            if (a[i] == "-zigAmp") float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out Amp);
            if (a[i] == "-zigStart") float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out Start);
            if (a[i] == "-zigRamp") float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out Ramp);
            if (a[i] == "-zigWave") float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out Wave);
        }
#endif
#if UNITY_EDITOR
        // エディタでは組み込みのマテリアルを書き換えたままにしない
        UnityEditor.EditorApplication.playModeStateChanged += s => { if (s == UnityEditor.PlayModeStateChange.ExitingPlayMode && def != null && original != null) def.shader = original; };
#endif
    }

    // 曲げない描き方(背景の空など)
    public static Material NoZigMaterial => noZig;

    public static void Apply(bool on, float originX)
    {
        if (!Installed) return;
        Shader.SetGlobalFloat(IdOn, on ? 1f : 0f);
        if (!on) return;
        Shader.SetGlobalFloat(IdAmp, Amp); Shader.SetGlobalFloat(IdStart, Start); Shader.SetGlobalFloat(IdRamp, Ramp); Shader.SetGlobalFloat(IdWave, Wave);
        Shader.SetGlobalFloat(IdOrigin, originX);
        // 道の位置に固定(Floating Origin でシーンを戻しても曲がりの位置が飛ばない)。float の精度のため波長で割った余りだけ使う
        Shader.SetGlobalFloat(IdPhase, (float)(FloatingOrigin.Offset % Mathf.Max(1f, Wave)));
    }
}
