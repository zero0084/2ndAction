using UnityEngine;

// 画面情報の表示(2026-10-31、UI 全体の改善): ラン中の HUD の見せ方の設定。描く側は毎フレームここを見るだけ(変えたらすぐ反映)。
//  ・表示モード: 標準(初期) / シンプル(HP・距離・速度・ボスの HP と BREAK・一時停止だけ) / 非表示(一時停止と操作のボタンだけ) / カスタム(項目ごと)
//  ・どのモードでも消さない物: 一時停止のボタン(そこから設定へ)、必殺技のボタン(操作)、敵の攻撃の予兆・危険な場所(ワールドの演出)、
//    脱出/カウントダウン/再開などの進行の案内、カードの3択などの画面
//  ・大きさ(75/100/125%)と背景の濃さ(通常/薄め/最小)。文字は薄くしない
//  ・確認用: 起動引数 -hudLegacy 1 で改修前の HUD(比較の撮影用。開発版だけ)
public static class HudPrefs
{
    public enum Mode { Standard = 0, Simple = 1, Hidden = 2, Custom = 3 }

    // カスタムで個別に切り替える項目(ビットの位置は保存に使うので並びを変えない。足す時は末尾へ)
    public enum Item
    {
        Best = 0, Distance = 1, Speed = 2, TakeHome = 3, LevelExp = 4, Hp = 5, CharCards = 6, Deck = 7,
        BossHp = 8, BreakGauge = 9, Combo = 10, Guide = 11, FinalEvo = 12, Party = 13,
    }
    public const int ItemCount = 14;

    public const string ModeKey = "HudModeV1", ItemsKey = "HudItemsV1", ScaleKey = "HudScaleV1", OpacityKey = "HudOpacityV1";
    public const int AllItems = (1 << ItemCount) - 1;

    static bool loaded;
    static Mode mode;
    static int items = AllItems, scale = 1, opacity;

    static void Load()
    {
        if (loaded) return;
        loaded = true;
        mode = (Mode)Mathf.Clamp(SaveStore.GetInt(ModeKey, 0), 0, 3);
        items = SaveStore.GetInt(ItemsKey, AllItems) & AllItems;
        scale = Mathf.Clamp(SaveStore.GetInt(ScaleKey, 1), 0, 2);
        opacity = Mathf.Clamp(SaveStore.GetInt(OpacityKey, 0), 0, 2);
    }
    public static void Reload() { loaded = false; }

    public static Mode CurrentMode { get { Load(); return mode; } }
    public static int CustomItems { get { Load(); return items; } }
    public static int ScaleIndex { get { Load(); return scale; } }     // 0 = 小(75%) / 1 = 標準 / 2 = 大(125%)
    public static int OpacityIndex { get { Load(); return opacity; } } // 0 = 通常 / 1 = 薄め / 2 = 最小

    public static void SetMode(Mode m) { Load(); mode = m; SaveStore.SetInt(ModeKey, (int)m); SaveStore.Save(); }
    public static void SetCustom(Item it, bool on)
    {
        Load();
        items = on ? items | (1 << (int)it) : items & ~(1 << (int)it);
        SaveStore.SetInt(ItemsKey, items); SaveStore.Save();
    }
    public static void SetScale(int i) { Load(); scale = Mathf.Clamp(i, 0, 2); SaveStore.SetInt(ScaleKey, scale); SaveStore.Save(); }
    public static void SetOpacity(int i) { Load(); opacity = Mathf.Clamp(i, 0, 2); SaveStore.SetInt(OpacityKey, opacity); SaveStore.Save(); }

    // シンプルで出す物(依頼: HP・距離・速度・ボスの HP・BREAK・一時停止)
    const int SimpleItems = (1 << (int)Item.Hp) | (1 << (int)Item.Distance) | (1 << (int)Item.Speed) | (1 << (int)Item.BossHp) | (1 << (int)Item.BreakGauge);

    public static int ItemsFor(Mode m)
    {
        switch (m)
        {
            case Mode.Simple: return SimpleItems;
            case Mode.Hidden: return 0;
            case Mode.Custom: return CustomItems;
            default: return AllItems;
        }
    }

    // この項目を今描くか(改修前の表示の時は全部 true = 今までどおり)
    public static bool Show(Item it) => Legacy || (ItemsFor(CurrentMode) & (1 << (int)it)) != 0;
    public static bool CustomOn(Item it) => (CustomItems & (1 << (int)it)) != 0;

    // 非表示モード: 一時停止のボタンだけ控えめに(押せる大きさはそのまま)
    public static bool HiddenMode => !Legacy && CurrentMode == Mode.Hidden;

    // HUD の大きさの倍率(ラン中の HUD だけ)
    public static float ScaleFactor => Legacy ? 1f : ScaleIndex == 0 ? 0.75f : ScaleIndex == 2 ? 1.25f : 1f;
    // 背景の板の濃さの倍率(文字と枠は薄くしない)
    public static float PanelAlpha => Legacy ? 1f : OpacityIndex == 1 ? 0.55f : OpacityIndex == 2 ? 0.2f : 1f;

    // 改修前の HUD(比較の撮影用、開発版だけ)。-hudLegacy 1
    static int legacy = -1;
    public static bool Legacy
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (legacy < 0) legacy = DevArg("-hudLegacy") == "1" ? 1 : 0;
            return legacy == 1;
#else
            return false;
#endif
        }
    }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public static void DevSetLegacy(bool on) { legacy = on ? 1 : 0; }
    public static string DevArg(string name)
    {
        var a = QaArgs.All; // 起動引数 + Android の qa_args.txt
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
        return "";
    }
#endif
}
