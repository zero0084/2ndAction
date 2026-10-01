using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// ステージ選択導線追加(2026-09-12) - CharacterSelectUIと同じ「Button.
// onClick/EventSystemに頼らず自前でタップ位置を各Rectと照合する」方式。
// マスター指示「ヴァンサバ系のように、サムネ・名前・特徴だけの簡易表示」
// に合わせ、CharacterSelectUIと違い中央の大きなメインビジュアル/右側の
// 詳細情報パネルは持たない - 各カード自体に名前・特徴テキストを直接
// SceneBuilder.BuildStageSelectCanvasが焼き込む(選択によって変化するのは
// 発光の有無だけ)、より単純な構成。
//
// カード一覧(cardSlotRects/cardGlowImages/lockIcons)はStageDatabase.
// AllStagesの件数ぶんSceneBuilder.BuildStageSelectCanvasが動的に生成する。
//
// 今回のスコープ: 「選ぶ→保存→Homeへ戻る」までの導線の完成が目的で、
// 実際のTerrain/Enemy生成を複数ステージへ分岐させる仕組みはまだ実装
// しない(StageDefinition.csのコメント参照) - 出発ボタンが保存するのは
// GameManager.SelectedStageId(次回NEW RUNの既定値としての文字列)のみ。
public class StageSelectUI : MonoBehaviour
{
    public RectTransform root;
    public CanvasGroup rootGroup;
    public RectTransform backButtonRect;
    public RectTransform departButtonRect;

    // SceneBuilderがStageDatabase.AllStagesの件数ぶん動的に生成。
    public RectTransform[] cardSlotRects = new RectTransform[0];
    public Image[] cardGlowImages = new Image[0];
    public bool[] cardUnlocked = new bool[0];

    public float rootFadeDuration = 0.15f;

    int selectedIndex;

    Coroutine fadeCoroutine;

    public void Open()
    {
        gameObject.SetActive(true);

        var all = StageDatabase.AllStages;
        string current = GameManager.Instance != null ? GameManager.Instance.SelectedStageId : null;
        selectedIndex = 0;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i].stageId == current) { selectedIndex = i; break; }
        }
        RefreshGlow();

        if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
        if (rootGroup != null)
        {
            rootGroup.alpha = 0f;
            fadeCoroutine = StartCoroutine(FadeCanvasGroup(rootGroup, 1f, rootFadeDuration));
        }
    }

    // BACKでは何も確定しない(選択状態はGameManager.SelectedStageIdへまだ
    // 書き込まれていない、Confirm()が呼ばれるまでは画面内のプレビューに
    // 過ぎない) - CharacterSelectUI.Closeと同じ設計。
    public void Close()
    {
        if (ScreenTransitionManager.Instance != null)
        {
            if (ScreenTransitionManager.Instance.IsTransitioning) return;
            if (!suppressCloseSe) if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Cancel);
            suppressCloseSe = false;
            ScreenTransitionManager.Instance.PlayTransition(() =>
            {
                gameObject.SetActive(false);
                if (GameManager.Instance != null) GameManager.Instance.CloseStageSelect();
            }, ScreenTransitionManager.Style.Fade);
            return;
        }

        if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
        fadeCoroutine = StartCoroutine(CloseFadeRoutine());
    }

    IEnumerator CloseFadeRoutine()
    {
        if (rootGroup != null) yield return FadeCanvasGroup(rootGroup, 0f, rootFadeDuration);
        gameObject.SetActive(false);
        if (GameManager.Instance != null) GameManager.Instance.CloseStageSelect();
    }

    IEnumerator FadeCanvasGroup(CanvasGroup group, float target, float duration)
    {
        float start = group.alpha;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, duration);
            group.alpha = Mathf.Lerp(start, target, Mathf.Clamp01(t));
            yield return null;
        }
    }

    // Home画面 / Stage Select改善依頼(2026-09-16), item5/6/9/10 - Stage
    // Selectは「出発時だけの専用画面」という方針になったため、出発ボタンは
    // 選択を確定してHomeへ戻るだけでなく、そのままRunを開始する
    // (GameManager.DepartFromStageSelectが、ステージ確定→この画面を閉じる
    // →Run開始を1回の画面遷移でまとめて行う - Close()は使わない、二重に
    // PlayTransitionを呼ぶとデッドロックするため)。
    bool suppressCloseSe; // 決定で閉じる時はキャンセル音を鳴らさない
    void Confirm()
    {
        var all = StageDatabase.AllStages;
        if (selectedIndex < 0 || selectedIndex >= all.Count) return;
        StageDefinition def = all[selectedIndex];
        if (!def.unlocked) return; // 未開放ステージでは確定できない(安全側の二重ガード)
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Decide);
        suppressCloseSe = true;
        if (GameManager.Instance != null) GameManager.Instance.DepartFromStageSelect(def.stageId);
    }

    void SelectIndex(int index)
    {
        var all = StageDatabase.AllStages;
        if (index < 0 || index >= all.Count) return;
        if (!all[index].unlocked) return; // ロックされたカードは選択自体できない(Acceptance Test 6)
        if (index == selectedIndex) return;
        selectedIndex = index;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.StageSelect);
        RefreshGlow();
    }

    void RefreshGlow()
    {
        for (int i = 0; i < cardGlowImages.Length; i++)
        {
            if (cardGlowImages[i] != null) cardGlowImages[i].gameObject.SetActive(i == selectedIndex);
        }
    }

    void HandleTap(Vector2 screenPos)
    {
        if (backButtonRect != null && RectTransformUtility.RectangleContainsScreenPoint(backButtonRect, screenPos, null))
        {
            Close();
            return;
        }
        if (departButtonRect != null && RectTransformUtility.RectangleContainsScreenPoint(departButtonRect, screenPos, null))
        {
            Confirm();
            return;
        }
        for (int i = 0; i < cardSlotRects.Length; i++)
        {
            if (cardSlotRects[i] != null && RectTransformUtility.RectangleContainsScreenPoint(cardSlotRects[i], screenPos, null))
            {
                SelectIndex(i);
                return;
            }
        }
    }

    void Update()
    {
        if (!gameObject.activeInHierarchy) return;
        // Input Lock(ブラッシュアップ点検2026-09-18で追加、CharacterSelectUI
        // /DeckEditUIと同じガード) - DepartFromStageSelect自体は多重発火
        // から守られているが、画面遷移が始まった後もこのガードが無いと
        // 覆い隠されていく最中に選択カードが裏で切り替わってしまう
        // (見た目上は問題にならないが、他画面との一貫性のため統一)。
        if (ScreenTransitionManager.Instance != null && ScreenTransitionManager.Instance.IsTransitioning) return;
        if (UiInputGate.Blocked) return; // 設定/DEBUGパネルが手前に開いている(閉じた時の指が離れるまでも)
        if (!TouchInputUtil.TryGetTapPosition(out Vector2 screenPos)) return;
        HandleTap(screenPos);
    }
}
