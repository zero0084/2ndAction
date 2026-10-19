using UnityEngine;

// 画面の色の決まり(2026-10-19 見た目の完成度向上: 画面ごとにばらばらだった「金」(約6種)を2種へ)。
//  縁の金 = 枠・区切り・選択中の縁(落ち着いた金) / 文字の金 = 題名・押せる強調の文字(明るい金)
// 新しく画面を作る時・直す時はここの色を使う(数値を直接書かない)。IMGUI と uGUI で同じ値。
public static class UiTheme
{
    public static readonly Color Navy = new Color(0.06f, 0.08f, 0.17f);        // 板の地
    public static readonly Color NavyDeep = new Color(0.03f, 0.04f, 0.09f);    // 押している間・暗幕
    public static readonly Color GoldEdge = new Color(0.83f, 0.68f, 0.32f);    // 縁の金
    public static readonly Color GoldText = new Color(1f, 0.88f, 0.55f);       // 文字の金(題名・強調)
    public static readonly Color GoldBright = new Color(1f, 0.82f, 0.38f);     // 選択中の縁(太い)
    public static readonly Color Text = new Color(0.96f, 0.96f, 1f);           // 本文
    public static readonly Color TextSub = new Color(0.78f, 0.8f, 0.88f);      // 補足
    public static readonly Color Disabled = new Color(0.62f, 0.64f, 0.7f);     // 使えない
    public static readonly Color Danger = new Color(1f, 0.45f, 0.38f);         // 取り消せない操作の注意
}
