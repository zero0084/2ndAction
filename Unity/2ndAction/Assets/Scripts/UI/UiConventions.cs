using UnityEngine;
using UnityEngine.UI;

// 画面をまたいだボタンの置き方(2026-10-09、依頼G-2)。
//  ・「« 戻る」= 左上(安全領域の内側)、どの画面も同じ大きさ/同じ文字/同じ金枠
//  ・「決定 / 出発」= 下(縦画面では中央寄せで大きく、横画面では右下)
// 画面側の当たり判定は RectTransform をそのまま使っているので、置き直しても押せる範囲は見た目と一致する
public static class UiConventions
{
    public static readonly Vector2 BackSize = new Vector2(200f, 72f);
    public static readonly Vector2 BackPos = new Vector2(30f, -26f);
    public const int BackFont = 28;

    public static void PlaceBack(RectTransform back)
    {
        if (back == null) return;
        back.anchorMin = back.anchorMax = new Vector2(0f, 1f);
        back.pivot = new Vector2(0f, 1f);
        back.sizeDelta = BackSize;
        back.anchoredPosition = BackPos;
        var t = back.GetComponentInChildren<Text>(true);
        if (t != null)
        {
            t.text = Loc.Auto("« BACK");
            t.fontSize = BackFont; t.resizeTextMaxSize = BackFont;
            t.color = new Color(0.92f, 0.93f, 0.98f);
            t.alignment = TextAnchor.MiddleCenter;
        }
    }

    // 左上の「戻る」の右から始まる見出し(左寄せの見出しが戻るに重ならないように)
    public static float AfterBackX => BackPos.x + BackSize.x + 24f;

    // 下の決定ボタン(縦画面: 中央・幅広 / 横画面: 右下)
    public static void PlaceConfirm(RectTransform rt, bool portrait, float rootWidth, Vector2 landscapeSize, Vector2 landscapePos)
    {
        if (rt == null) return;
        if (portrait)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(Mathf.Min(520f, rootWidth - 80f), 84f);
            rt.anchoredPosition = new Vector2(0f, 36f);
        }
        else
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f);
            rt.sizeDelta = landscapeSize;
            rt.anchoredPosition = landscapePos;
        }
    }
}
