using UnityEngine;

// Shared raw touch/mouse tap-down detector that bypasses the
// EventSystem/Button pipeline entirely. Extracted from RewardCardSequence
// (which needed this as a "belt-and-suspenders" backup because taps
// weren't always reliably delivered through Button.onClick on this
// project's target devices) so DeckEditUI can use the same trick as its
// only path, not just a backup - see DeckEditUI.Update for why it can't
// safely run both at once.
public static class TouchInputUtil
{
    public static bool TryGetTapPosition(out Vector2 position)
    {
        if (Input.GetMouseButtonDown(0))
        {
            position = Input.mousePosition;
            return true;
        }
        if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
        {
            position = Input.GetTouch(0).position;
            return true;
        }
        // ゲームパッド/キーボードの決定: フォーカスの中心を叩いたことにする(2026-10-06)
        if (PadNav.TakeVirtualTap(out position)) return true;
        position = Vector2.zero;
        return false;
    }
}
