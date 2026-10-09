using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// uGUI のボタンの「押している間」「使用不可」の見た目(2026-10-09、依頼G-2)。
// シーンで作ったボタン(SceneBuilder)の多くは色の変化なし(Transition.None)で、画面側が自分で当たりを取っている
// (EventSystem を通らない)ため、押しても見た目が変わらなかった。ここでは入力の仕組みに関係なく、
// 指(マウス)が押さえている位置がボタンの四角に入っている間だけ暗い膜を重ねる。interactable=false の間は薄い灰の膜。
// 見た目だけ: 当たり判定/押した時の処理は今までどおり。カード(RewardCardUI)は選択の見た目が別にあるので付けない
[DisallowMultipleComponent]
public class UiPressFx : MonoBehaviour
{
    Button button;
    RectTransform rt;
    Image shade;
    Canvas canvas;
    int state = -1; // 0=通常 1=押している 2=使用不可

    static readonly Color PressColor = new Color(0f, 0f, 0.02f, 0.32f), DisabledColor = new Color(0.05f, 0.05f, 0.08f, 0.5f);

    void Awake()
    {
        button = GetComponent<Button>();
        rt = transform as RectTransform;
        var go = new GameObject("PressShade", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var r = (RectTransform)go.transform;
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = new Vector2(3f, 3f); r.offsetMax = new Vector2(-3f, -3f);
        shade = go.AddComponent<Image>();
        shade.raycastTarget = false;
        shade.enabled = false;
    }

    void OnDisable() { if (shade != null) shade.enabled = false; state = -1; }

    void LateUpdate()
    {
        if (button == null || shade == null) return;
        int s = !button.interactable ? 2 : Pressed() ? 1 : 0;
        if (s == state) return;
        state = s;
        shade.enabled = s != 0;
        shade.color = s == 1 ? PressColor : DisabledColor;
        if (s != 0) shade.transform.SetAsLastSibling(); // 文字の上にも薄く掛ける(押した感じがはっきり出る)
    }

    bool Pressed()
    {
        Vector2 p;
        if (Input.touchCount > 0) p = Input.GetTouch(0).position;
        else if (Input.GetMouseButton(0)) p = Input.mousePosition;
        else return false;
        if (canvas == null) canvas = GetComponentInParent<Canvas>();
        Camera cam = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
        return RectTransformUtility.RectangleContainsScreenPoint(rt, p, cam);
    }

    // ---- 付け忘れが無いよう、出ているボタンへ自動で付ける(画面の作り直し/実行中に作るボタンも拾う)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        var go = new GameObject("UiPressFxInstaller");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideInHierarchy;
        go.AddComponent<Installer>();
    }

    class Installer : MonoBehaviour
    {
        float next;
        void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.5f;
            var all = FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var b in all)
            {
                if (b == null || b.GetComponent<UiPressFx>() != null) continue;
                if (b.transition != Selectable.Transition.None) continue;      // 色が変わる作りの物はそのまま
                if (b.GetComponent<RewardCardUI>() != null) continue;           // カード
                b.gameObject.AddComponent<UiPressFx>();
            }
        }
    }
}
