using UnityEngine;
using UnityEngine.UI;

// A small reusable yes/no confirmation modal (uGUI) - "現在のデッキをおすす
// め編成に変更しますか?" / "デッキからすべてのカードを外しますか?"
// (DeckEditUI) today, built as its own component rather than folded into
// DeckEditUI so any future uGUI screen that needs a confirm-before-
// destructive-action prompt can reuse it instead of writing another
// one-off dialog.
//
// Uses the same raw-touch hit-testing every other tap on the Deck Edit
// screen does (see DeckEditUI's class comment for why), not
// Button.onClick - the owning screen's own Update() must check IsOpen
// before processing its own taps while this is showing, and forward the
// released tap into HandleTap here instead (see DeckEditUI.Update).
public class ConfirmDialogUI : MonoBehaviour
{
    public GameObject root;
    public Text messageText;
    public RectTransform yesRect;
    public RectTransform noRect;

    System.Action onConfirm;
    System.Action onCancel;

    public bool IsOpen => root != null && root.activeSelf;

    public void Show(string message, System.Action confirmCallback, System.Action cancelCallback = null)
    {
        if (messageText != null) messageText.text = message;
        onConfirm = confirmCallback;
        onCancel = cancelCallback;
        if (root != null) root.SetActive(true);
    }

    // Androidの戻る: 「いいえ」と同じ
    public void Cancel()
    {
        if (!IsOpen) return;
        System.Action callback = onCancel;
        Hide();
        callback?.Invoke();
    }

    void Hide()
    {
        if (root != null) root.SetActive(false);
        onConfirm = null;
        onCancel = null;
    }

    // Called by the owning screen's raw-touch input handling while IsOpen
    // is true - always returns true (consumed) so the caller never also
    // processes the same tap as a card/button tap of its own; every tap
    // while open resolves here, on Yes/No or otherwise (modal - nothing
    // behind the dialog should react).
    public bool HandleTap(Vector2 screenPos)
    {
        if (!IsOpen) return false;

        if (yesRect != null && RectTransformUtility.RectangleContainsScreenPoint(yesRect, screenPos, null))
        {
            System.Action callback = onConfirm;
            Hide();
            callback?.Invoke();
            return true;
        }
        if (noRect != null && RectTransformUtility.RectangleContainsScreenPoint(noRect, screenPos, null))
        {
            System.Action callback = onCancel;
            Hide();
            callback?.Invoke();
            return true;
        }
        return true;
    }
}
