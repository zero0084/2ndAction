using UnityEngine;

// 機種ごとに差し替える部分の入口(2026-10-06、家庭用機への移植の準備)。
//  今の実装は Android / PC 用で、中身は今までと同じ(保存は PlayerPrefs、振動なし、LAN マルチあり)。
//  Switch / PS5 では Platform.Install(...) で各サービスを入れ替える(ゲーム側は Platform.* だけを見る)。
//
//  差し替え口の一覧:
//   ・保存            ISaveStore      … PlayerPrefsStore(今)→ 機種のセーブ API(ユーザーごとの保存領域、書き込み中の表示、容量)
//   ・振動            IHaptics        … NullHaptics(今)→ パッドの振動(Input System の Gamepad.SetMotorSpeeds 等)
//   ・オンライン      IOnlineCaps     … LAN マルチの有無(今: あり)→ 機種のネットワーク規約に合わせて無効/置き換え
//   ・画面/品質       IDisplayProfile … 今は何もしない(スマホの挙動を変えない)。据え置き機では解像度/フレームレート/品質を決める
//   ・入力            IInputSource    … GameInput.SetSources(...)(GameInput.cs)
public interface ISaveStore
{
    bool HasKey(string key);
    int GetInt(string key, int def);
    float GetFloat(string key, float def);
    string GetString(string key, string def);
    void SetInt(string key, int v);
    void SetFloat(string key, float v);
    void SetString(string key, string v);
    void DeleteKey(string key);
    void DeleteAll();
    void Save();
}

public class PlayerPrefsStore : ISaveStore
{
    public bool HasKey(string key) => PlayerPrefs.HasKey(key);
    public int GetInt(string key, int def) => PlayerPrefs.GetInt(key, def);
    public float GetFloat(string key, float def) => PlayerPrefs.GetFloat(key, def);
    public string GetString(string key, string def) => PlayerPrefs.GetString(key, def);
    public void SetInt(string key, int v) => PlayerPrefs.SetInt(key, v);
    public void SetFloat(string key, float v) => PlayerPrefs.SetFloat(key, v);
    public void SetString(string key, string v) => PlayerPrefs.SetString(key, v);
    public void DeleteKey(string key) => PlayerPrefs.DeleteKey(key);
    public void DeleteAll() => PlayerPrefs.DeleteAll();
    public void Save() => PlayerPrefs.Save();
}

// PlayerPrefs と同じ形の入口(ゲームのコードはここを呼ぶ)。中身は Platform.Save
public static class SaveStore
{
    static ISaveStore S => Platform.Save;
    public static bool HasKey(string key) => S.HasKey(key);
    public static int GetInt(string key, int def = 0) => S.GetInt(key, def);
    public static float GetFloat(string key, float def = 0f) => S.GetFloat(key, def);
    public static string GetString(string key, string def = "") => S.GetString(key, def);
    public static void SetInt(string key, int v) => S.SetInt(key, v);
    public static void SetFloat(string key, float v) => S.SetFloat(key, v);
    public static void SetString(string key, string v) => S.SetString(key, v);
    public static void DeleteKey(string key) => S.DeleteKey(key);
    public static void DeleteAll() => S.DeleteAll();
    public static void Save() => S.Save();
}

public enum HapticKind { Light, Hit, Heavy, Boss }
public interface IHaptics { void Play(HapticKind kind); void Stop(); }
public class NullHaptics : IHaptics { public void Play(HapticKind kind) { } public void Stop() { } }

public interface IOnlineCaps
{
    bool LanMultiplayer { get; }     // ホームの「マルチ」(LAN の自動発見 + NGO/UTP)
}
public class DefaultOnlineCaps : IOnlineCaps { public bool LanMultiplayer => true; }

public interface IDisplayProfile
{
    string Name { get; }
    void Apply();                    // 起動時に1回(解像度/フレームレート/品質)
}
// 今の Android / PC: 何も変えない(Unity の既定と各画面の作りのまま)
public class DefaultDisplayProfile : IDisplayProfile
{
    public string Name => "default";
    public void Apply() { }
}

public static class Platform
{
    public static ISaveStore Save { get; private set; } = new PlayerPrefsStore();
    public static IHaptics Haptics { get; private set; } = new NullHaptics();
    public static IOnlineCaps Online { get; private set; } = new DefaultOnlineCaps();
    public static IDisplayProfile Display { get; private set; } = new DefaultDisplayProfile();

    // 機種ごとの差し替え(起動の最初、セーブを読む前に呼ぶ)。null の物は今のまま
    public static void Install(ISaveStore save = null, IHaptics haptics = null, IOnlineCaps online = null, IDisplayProfile display = null)
    {
        if (save != null) Save = save;
        if (haptics != null) Haptics = haptics;
        if (online != null) Online = online;
        if (display != null) Display = display;
    }

    // 機種の名前(診断/テストの表示用)
    public static string Name
    {
        get
        {
#if UNITY_ANDROID
            return "Android";
#elif UNITY_SWITCH
            return "Switch";
#elif UNITY_PS5
            return "PS5";
#elif UNITY_STANDALONE_WIN
            return "Windows";
#else
            return Application.platform.ToString();
#endif
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        // 家庭用機の対応を足す時はここで Install(...) する(例: #if UNITY_SWITCH Install(new SwitchSaveStore(), ...) #endif)
        Display.Apply();
    }
}
