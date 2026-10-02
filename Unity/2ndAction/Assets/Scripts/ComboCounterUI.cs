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

    void OnGUI()
    {
        if (ComboCount < showThreshold) return;
        if (GameManager.Instance == null || GameManager.Instance.IsGameOver) return;

        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = 34;
        style.fontStyle = FontStyle.Bold;
        style.alignment = TextAnchor.MiddleCenter;

        Rect rect = new Rect(Screen.width / 2f - 160f, Screen.height * 0.2f, 320f, 60f);
        string text = $"{ComboCount} HIT";

        // 黒縁取り(4方向にずらして重ねる簡易アウトライン) - 明るい空背景
        // でも読めるように、既存HUDのUiBackdrop的な発想をテキストへ適用。
        style.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
        Vector2[] offsets = { new Vector2(-2, -2), new Vector2(2, -2), new Vector2(-2, 2), new Vector2(2, 2) };
        foreach (Vector2 o in offsets)
        {
            GUI.Label(new Rect(rect.x + o.x, rect.y + o.y, rect.width, rect.height), text, style);
        }

        style.normal.textColor = new Color(1f, 0.9f, 0.4f);
        GUI.Label(rect, text, style);
    }
}
