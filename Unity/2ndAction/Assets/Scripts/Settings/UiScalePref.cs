using UnityEngine;

// 文字・UI の大きさ(2026-10-31): 標準 / 大きめ。設定・お知らせ・案内・確認の窓・結果・ガチャなど、文字を読む IMGUI の画面の倍率に掛ける。
//  ・小さい画面(短い辺がおよそ 690px 未満)では効かない(大きくすると窓が画面に収まらないため。見切れないことを優先)
//  ・ラン中の HUD は「HUDの大きさ」(HudPrefs)、カードの画面(uGUI)は画面の大きさに合わせて並べ直す作りなので、ここでは変えない
public static class UiScalePref
{
    public const string Key = "UiScaleV1";
    public static readonly float[] Factors = { 1f, 1.15f };
    public static int Index => Mathf.Clamp(SaveStore.GetInt(Key, 0), 0, Factors.Length - 1);
    public static float Factor => Factors[Index];
    public static void Set(int i)
    {
        SaveStore.SetInt(Key, Mathf.Clamp(i, 0, Factors.Length - 1));
        SaveStore.Save();
        Debug.Log($"[UiScale] {Factor:F2}");
    }

    // 画面ごとの倍率 s(短い辺 / 720 の類)に掛ける。仮想の短い辺が 600 を下回るほどは大きくしない
    public static float Menu(float s) => Mathf.Max(s, Mathf.Min(s * Factor, Mathf.Min(Screen.width, Screen.height) / 600f));
}
