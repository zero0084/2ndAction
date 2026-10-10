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

    void Update()
    {
        if (ComboCount <= 0) return;
        // Time.deltaTimeはLevel Up選択中(Time.timeScale=0)は自動的に0に
        // なるため、ここで特別な一時停止処理をしなくても選択中はコンボが
        // 減らずに凍結される(既存のHitStop/RewardCardSequence等と同じ
        // Time.timeScale依存の挙動)。
        timer -= Time.deltaTime;
        if (timer <= 0f) ComboCount = 0;
    }

    GUIStyle style;
    static readonly Vector2[] offsets = { new Vector2(-2, -2), new Vector2(2, -2), new Vector2(-2, 2), new Vector2(2, 2) };
    void OnGUI()
    {
        if (ComboCount < showThreshold) return;
        if (GameManager.Instance == null || GameManager.Instance.IsGameOver) return;
        if (BonusZone.Instance != null && BonusZone.Instance.State == BonusZone.Phase.Ending) return; // BONUS CLEAR/RESULT(1〜2秒)と同じ所に重なるので、その間は出さない

        if (style == null) style = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter }; // 毎回作らない(OnGUI は1フレームに何度も呼ばれる)

        // 2026-10-10(全体点検): 高解像度では HUD と同じ倍率で大きく。ボス戦の帯(BREAK!/反撃のチャンス 等)が出ている間はその下へ(重なって読めなかった)
        float k = GameManager.HudK;
        style.fontSize = Mathf.RoundToInt(34 * k);
        float y = Screen.height * 0.2f;
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
    }
}
