using UnityEngine;

// 縦画面のラン表示(2026-10-08、依頼E-4)。縦の画面の時だけ使う(横画面のラン表示には影響しない)。
//  0 = 横から見る … 道が画面を横に流れる(横画面と同じ見せ方を縦に合わせた構図。地表は画面の下寄り)
//  1 = 斜め上から見る … 道が画面の奥へ続く(前方を遠くまで見通す)
//  ・今までは開発版の切り替え(V キー/DEBUG)だけで保存も無かったので、既存ユーザーの選択は「横から見る」(今までの縦の既定)になる。
//  ・ラン中に設定で変えた時は、設定を閉じた時に反映する(ランはリセットしない)。
//  ・判定は「今の画面が縦か」(回転の設定ではなく実際の縦横)。横へ回すと横の表示、縦へ戻すと選んだモード。
public static class PortraitRunView
{
    public const string Key = "PortraitRunViewV1";
    public const int Side = 0, Oblique = 1;
    public static int Mode => Mathf.Clamp(SaveStore.GetInt(Key, Side), 0, 1);
    public static event System.Action Changed;
    public static bool IsPortraitScreen => Screen.height > Screen.width;
    public static bool UseOblique => IsPortraitScreen && Mode == Oblique;
    public static bool UseSidePortrait => IsPortraitScreen && Mode == Side;

    public static void Set(int mode)
    {
        mode = Mathf.Clamp(mode, 0, 1);
        if (SaveStore.GetInt(Key, Side) == mode && SaveStore.HasKey(Key)) return;
        SaveStore.SetInt(Key, mode);
        SaveStore.Save();
        Debug.Log($"[PortraitRunView] mode -> {(mode == Oblique ? "oblique" : "side")}");
        if (!SettingsPanel.IsVisible) Changed?.Invoke(); // 設定を開いている間は閉じた時に(SettingsPanel が NotifyIfPending を呼ぶ)
        else pending = true;
    }

    static bool pending;
    public static void NotifyIfPending() { if (!pending) return; pending = false; Changed?.Invoke(); }
}
