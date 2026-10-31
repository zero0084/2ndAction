using UnityEngine;
using UnityEngine.UI;

// uGUI の一覧(ScrollRect)をマウスのホイールで動かす(2026-10-31、Windows の操作)。
//  デッキ編集/カード合成/キャラ選択は EventSystem を使わず自前でタップ/ドラッグを判定しているため、ScrollRect の標準のホイールが届いていなかった。
//  ・ポインターを置いている一覧だけを動かす(呼び出し側がポインターの下の一覧を選ぶ)
//  ・手前に窓(確認/設定/デッキの一覧など UiInputGate.ModalOpen)がある時は、後ろの一覧を動かさない
//  ・1目盛り = 見えている高さの約2割(項目を目で追える量。1行分前後)
public static class UguiWheel
{
    public const float NotchFraction = 0.22f;

    public static float Delta => PlatformCaps.SupportsMouse ? Input.mouseScrollDelta.y : 0f;

    public static bool Apply(ScrollRect sr, bool blocked = false)
    {
        float w = Delta;
        if (Mathf.Abs(w) < 0.01f || blocked || UiInputGate.ModalOpen) return false;
        if (sr == null || sr.viewport == null || sr.content == null || !sr.gameObject.activeInHierarchy) return false;
        if (!RectTransformUtility.RectangleContainsScreenPoint(sr.viewport, Input.mousePosition, null)) return false;
        float viewH = sr.viewport.rect.height;
        float range = sr.content.rect.height * Mathf.Abs(sr.content.localScale.y) - viewH;
        if (range <= 0.5f) return false;
        sr.verticalNormalizedPosition = Mathf.Clamp01(sr.verticalNormalizedPosition + w * viewH * NotchFraction / range);
        return true;
    }
}
