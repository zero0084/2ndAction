using UnityEngine;

// ボス戦の強化(2026-10-01)で、ボス以外の仕組みが参照する共有の状態。
//  ・UltimateActive … どれかのボスが必殺技の溜め〜実行中(雑魚の攻撃を遅らせる/障害物を置かない)
//  ・TryBeginUltimate … 同時に2体以上が必殺技を始めないための順番取り(複数オオカミの同時突進を防ぐ)
//  ・ZakoAttackScale … 雑魚の攻撃タイマーの進み方(1=通常)
public static class BossBattle
{
    static Object ultimateOwner;
    static float ultimateSince;
    public static float LastUltimateEnd = -99f;

    public static bool UltimateActive => ultimateOwner != null && Time.time - ultimateSince < 15f;

    // 次の必殺技は、前の必殺技が終わってから少し空ける(複数体の連続必殺技で詰まないように)
    public static bool TryBeginUltimate(Object owner, float gapAfterPrevious = 2.5f)
    {
        if (UltimateActive && ultimateOwner != owner) return false;
        if (ultimateOwner != owner && Time.time - LastUltimateEnd < gapAfterPrevious) return false;
        ultimateOwner = owner;
        ultimateSince = Time.time;
        return true;
    }

    public static void EndUltimate(Object owner)
    {
        if (ultimateOwner != owner) return;
        ultimateOwner = null;
        LastUltimateEnd = Time.time;
    }

    public static float ZakoAttackScale => UltimateActive ? Mathf.Clamp01(BossBattleTuning.I.zakoAttackScaleDuringUltimate) : 1f;
    public static bool SuppressObstacles => UltimateActive && BossBattleTuning.I.suppressObstaclesDuringUltimate;

    public static void ResetAll() { ultimateOwner = null; LastUltimateEnd = -99f; }

    // 戦っている(撃破の演出に入っていない)ボスがいるか。ラン再開の時計はこれがtrueの間だけ進む。
    public static readonly System.Collections.Generic.HashSet<MonoBehaviour> Living = new System.Collections.Generic.HashSet<MonoBehaviour>();
    public static bool AnyBossFighting
    {
        get
        {
            Living.RemoveWhere(b => b == null);
            foreach (var b in Living)
            {
                if (b is WildBossBase w) { if (!w.IsDead && w.isActiveAndEnabled) return true; }
                else if (b is DragonController d) { if (!d.IsDead) return true; }
            }
            return false;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => ResetAll();
        if (BossBattleHud.Instance == null)
        {
            var go = new GameObject("[BossBattleHud]");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<BossBattleHud>();
        }
    }
}

// ボス戦の短い表示(IMGUI): ラン再開の告知 / 再開までの残り秒(最後の数秒だけ) / 段階移行 / BREAK。
// HPバー(ワールド上)は既存のまま、崩しゲージはその下の細い帯(DragonHealthBar.SetSub)。
public class BossBattleHud : MonoBehaviour
{
    public static BossBattleHud Instance { get; private set; }
    string bannerText; Color bannerColor; float bannerUntil, bannerStart;
    GUIStyle big, small;

    void Awake() { Instance = this; }

    public static void Banner(string text, Color color, float seconds)
    {
        if (Instance == null) return;
        Instance.bannerText = text;
        Instance.bannerColor = color;
        Instance.bannerStart = Time.unscaledTime;
        Instance.bannerUntil = Time.unscaledTime + seconds;
    }

    void OnGUI()
    {
        var gm = GameManager.Instance;
        if (gm == null || !gm.HasStarted || gm.IsGameOver) return;
        float s = Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 720f, 1f, 2.6f);
        Matrix4x4 keep = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float w = Screen.width / s, h = Screen.height / s;
        if (big == null) { big = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold }; small = new GUIStyle(big); }

        var bm = BossManager.Instance;
        // ラン再開までの残り(最後の5秒だけ。ボスを早く倒す理由を見せる)
        if (bm != null && bm.ResumeCountdownVisible)
        {
            float left = bm.ResumeSecondsLeft;
            small.fontSize = 20;
            string t = $"ラン再開まで {Mathf.CeilToInt(left)}";
            Rect r = new Rect(w * 0.5f - 140f, h * 0.17f, 280f, 30f);
            UiKit.Fill(new Rect(r.x + 40f, r.y + 2f, r.width - 80f, r.height - 4f), new Color(0.05f, 0.03f, 0.08f, 0.55f));
            small.normal.textColor = Color.Lerp(new Color(1f, 0.4f, 0.3f), new Color(1f, 0.9f, 0.5f), Mathf.PingPong(Time.unscaledTime * 3f, 1f));
            GUI.Label(r, t, small);
        }

        if (!string.IsNullOrEmpty(bannerText) && Time.unscaledTime < bannerUntil)
        {
            float age = Time.unscaledTime - bannerStart, left = bannerUntil - Time.unscaledTime;
            float a = Mathf.Clamp01(age / 0.15f) * Mathf.Clamp01(left / 0.35f);
            float pop = 1f + 0.25f * Mathf.Clamp01(1f - age / 0.18f);
            big.fontSize = Mathf.RoundToInt(30 * pop);
            Rect r = new Rect(0f, h * 0.24f, w, 50f);
            UiKit.Fill(new Rect(w * 0.5f - 260f, r.y + 4f, 520f, 42f), new Color(0f, 0f, 0f, 0.45f * a));
            Color c = bannerColor; c.a *= a;
            Color sh = new Color(0f, 0f, 0f, 0.8f * a);
            big.normal.textColor = sh;
            GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), bannerText, big);
            big.normal.textColor = c;
            GUI.Label(r, bannerText, big);
        }
        GUI.matrix = keep;
    }
}
