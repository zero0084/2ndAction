using UnityEngine;

// エリアルコンボ改修(2026-09-11) - 「連続して敵へ攻撃を当てた場合、3 HIT/
// 4 HIT...のような簡単なコンボ表示を追加。一定時間攻撃が当たらなければ
// リセット」。マスター指示どおり「最初は派手な演出は不要...まずはコンボ
// が正常につながっていることを確認できる簡易UI」なので、既存のHUD
// (GameManager/PlayerControllerのOnGUI)と同じIMGUIでの単純なテキスト
// 表示に留めている。EnemyController.OnTriggerEnter2Dが敵種別・生死を
// 問わず「攻撃が命中した」瞬間に毎回RegisterHit()を呼ぶ。
//
// 将来のスコア倍率発展に備え、ComboCountをpublicに公開済み - 「コンボ数
// によるスコア倍率」を追加する際はここに新しいプロパティ(例:
// ScoreMultiplier => 1f + ComboCount * 0.05f)を足すだけで済む。
public class ComboCounterUI : MonoBehaviour
{
    public static ComboCounterUI Instance { get; private set; }

    [Header("Combo Counter (Inspectorから調整可能)")]
    // 一定時間攻撃が当たらなければリセット。
    public float resetWindow = 1.5f;
    // このヒット数未満は画面に表示しない(「1 HIT」を毎回出すと煩わしい
    // ため) - 表示条件だけの話で、ComboCount自体はshowThreshold未満でも
    // 正しく積み上がっている。
    public int showThreshold = 2;

    public int ComboCount { get; private set; }
    float timer;

    void Awake()
    {
        Instance = this;
    }

    public void RegisterHit()
    {
        ComboCount++;
        timer = resetWindow;
    }

    // エリアルコンボ改修(2026-10-18): 空中追撃の段(AIR 2 / AIR 3 …)。「N HIT」の下に小さく出す(敵/足場/予告に重ねない)
    public int AirStage { get; private set; }
    float airTimer; bool airFinal;
    public const float AirShowSeconds = 0.9f;
    public static int AirMaxShown; // 確認用(QA): 表示した最大の段
    public void RegisterAir(int stage, bool final)
    {
        if (stage < 2) return;
        AirStage = stage; airFinal = final; airTimer = AirShowSeconds;
        if (stage > AirMaxShown) AirMaxShown = stage;
    }

    void Update()
    {
        if (ComboCount <= 0 && airTimer <= 0f) return;
        // Time.deltaTimeはLevel Up選択中(Time.timeScale=0)は自動的に0に
        // なるため、ここで特別な一時停止処理をしなくても選択中はコンボが
        // 減らずに凍結される(既存のHitStop/RewardCardSequence等と同じ
        // Time.timeScale依存の挙動)。
        timer -= Time.deltaTime;
        if (timer <= 0f) ComboCount = 0;
        if (airTimer > 0f) { airTimer -= Time.deltaTime; if (airTimer <= 0f) AirStage = 0; }
    }

    GUIStyle style;
    static readonly Vector2[] offsets = { new Vector2(-2, -2), new Vector2(2, -2), new Vector2(-2, 2), new Vector2(2, 2) };
    void OnGUI()
    {
        if (ComboCount < showThreshold) return;
        if (GameManager.Instance == null || GameManager.Instance.RunScreenOpen) return; // 2026-10-14: カード選択などの画面の手前に出さない
        if (BonusZone.Instance != null && BonusZone.Instance.State == BonusZone.Phase.Ending) return; // BONUS CLEAR/RESULT(1〜2秒)と同じ所に重なるので、その間は出さない

        if (style == null) style = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter }; // 毎回作らない(OnGUI は1フレームに何度も呼ばれる)

        // 2026-10-10(全体点検): 高解像度では HUD と同じ倍率で大きく。ボス戦の帯(BREAK!/反撃のチャンス 等)が出ている間はその下へ(重なって読めなかった)
        float k = GameManager.HudK;
        style.fontSize = Mathf.RoundToInt(34 * k);
        float y = Mathf.Max(Screen.height * 0.2f, GameManager.NoticeTopPx); // HUD の下(2026-10-11)
        if (BossBattleHud.BannerVisible) y = Mathf.Max(y, BossBattleHud.BannerBottomPx + 4f);
        Rect rect = new Rect(Screen.width / 2f - 160f * k, y, 320f * k, 60f * k);
        string text = $"{ComboCount} HIT";

        // 黒縁取り(4方向にずらして重ねる簡易アウトライン) - 明るい空背景
        // でも読めるように、既存HUDのUiBackdrop的な発想をテキストへ適用。
        style.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
        foreach (Vector2 o in offsets)
        {
            LocGUI.Label(new Rect(rect.x + o.x, rect.y + o.y, rect.width, rect.height), text, style);
        }

        style.normal.textColor = new Color(1f, 0.9f, 0.4f);
        LocGUI.Label(rect, text, style);

        if (AirStage >= 2 && airTimer > 0f)
        {
            // 小さく・すぐ消える(最後の段=叩き落とし/撃破は少しだけ強い色)
            float a = Mathf.Clamp01(airTimer / (AirShowSeconds * 0.4f));
            int fs = style.fontSize; style.fontSize = Mathf.RoundToInt(22 * k);
            Rect r2 = new Rect(rect.x, rect.y + rect.height * 0.78f, rect.width, 34f * k);
            string t2 = $"AIR {AirStage}";
            style.normal.textColor = new Color(0f, 0f, 0f, 0.8f * a);
            foreach (Vector2 o in offsets) LocGUI.Label(new Rect(r2.x + o.x * 0.75f, r2.y + o.y * 0.75f, r2.width, r2.height), t2, style);
            style.normal.textColor = airFinal ? new Color(1f, 0.62f, 0.35f, a) : new Color(0.75f, 0.92f, 1f, a);
            LocGUI.Label(r2, t2, style);
            style.fontSize = fs;
        }
    }
}
