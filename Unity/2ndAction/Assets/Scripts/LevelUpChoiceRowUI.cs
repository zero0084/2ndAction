using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// レベルアップ選択UI改修(2026-09-11) - マスター提供の参考画像(「レベル
// アップ演出案 - カードからアイコンへ(ヴァンサバ風選択UI)」)のStep3
// 「シンプルな選択UIに切り替え」を実現する、横長1行ぶんの選択肢。
// RewardCardSequenceがカード3枚を短く見せた後にこの3行へ切り替える -
// 受け取るデータは既存のRewardCardData(GameManager.MakeChoiceCardDataが
// 抽選・効果適用ロジックから作る、既存のカード表示と完全に同じもの)その
// ままなので、抽選ロジック/効果適用ロジックには一切触れていない。
//
// 「行全体をタップ可能にする」("スマホ横画面なので大きなタップ領域を確
// 保")という要望どおり、ButtonはこのGameObjectのRoot(行全体)に付ける。
// 見た目の配色はUiBackdrop(HUDの紺地+金縁と同じ処方)に合わせ、新規アート
// を追加せず既存のOneMoreMileの色調のまま。
public class LevelUpChoiceRowUI : MonoBehaviour
{
    public RectTransform rect;
    public CanvasGroup canvasGroup;
    public Button button;
    public Image edgeImage;      // 行の縁取り(金) - 選択時に発光色へ
    public Image bgImage;        // 行の下地(紺) - 選択時に少し明るく
    public Image iconBackdrop;
    public Image iconImage;
    public Text titleText;
    public Text descriptionText;
    public Text valueText;

    RewardCardData data;
    public RewardCardData Data => data;

    // UiBackdrop.GoldEdge/NavyFillと同じ処方(既存OneMoreMile UIの配色を
    // 踏襲) - 新しい色を持ち込まない。
    static readonly Color EdgeNormalColor = new Color(0.83f, 0.68f, 0.32f, 1f);
    static readonly Color EdgeSelectedColor = new Color(1f, 0.92f, 0.55f, 1f);
    static readonly Color BgNormalColor = new Color(0.06f, 0.08f, 0.17f, 0.92f);
    // 「背景が少し明るくなる」 - 紺のままわずかに金みを足して明るくする。
    static readonly Color BgSelectedColor = new Color(0.17f, 0.15f, 0.09f, 0.96f);

    public void SetContent(RewardCardData cardData)
    {
        data = cardData;
        if (iconImage.sprite != null) Destroy(iconImage.sprite);
        iconImage.sprite = cardData.Icon != null
            ? Sprite.Create(cardData.Icon, new Rect(0f, 0f, cardData.Icon.width, cardData.Icon.height), new Vector2(0.5f, 0.5f))
            : null;
        titleText.text = cardData.Title;
        descriptionText.text = cardData.Description;
        if (valueText != null)
        {
            valueText.text = cardData.ValueLine;
            valueText.enabled = !string.IsNullOrEmpty(cardData.ValueLine);
        }
        SetSelectedVisual(false);
    }

    public void SetInteractable(bool value)
    {
        button.interactable = value;
        canvasGroup.blocksRaycasts = value;
    }

    // 「タップした際には、枠が発光する・背景が少し明るくなる・軽く拡大す
    // る」の前半2つ(発光/明るさ)を即時反映する版 - 拡大自体はScaleTo
    // (RewardCardSequence側で呼ぶ)が担当。
    public void SetSelectedVisual(bool selected)
    {
        edgeImage.color = selected ? EdgeSelectedColor : EdgeNormalColor;
        bgImage.color = selected ? BgSelectedColor : BgNormalColor;
    }

    public IEnumerator ScaleTo(float targetScale, float duration)
    {
        Vector3 start = rect.localScale;
        Vector3 target = Vector3.one * targetScale;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, duration);
            rect.localScale = Vector3.Lerp(start, target, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t)));
            yield return null;
        }
    }

    public IEnumerator FadeTo(float targetAlpha, float duration)
    {
        float start = canvasGroup.alpha;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, duration);
            canvasGroup.alpha = Mathf.Lerp(start, targetAlpha, Mathf.Clamp01(t));
            yield return null;
        }
    }

    // 選択された瞬間の縁の発光(速く上がって遅く戻る、RewardCardUI.
    // FlashFrameと同じ形の演出) - 終わったらSetSelectedVisual(true)相当の
    // 明るい縁色に落ち着かせる。
    public IEnumerator FlashEdge(float duration)
    {
        float t = 0f;
        Color bright = new Color(1f, 1f, 0.85f, 1f);
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, duration);
            float f = Mathf.Clamp01(t);
            float intensity = f < 0.3f ? f / 0.3f : 1f - (f - 0.3f) / 0.7f;
            edgeImage.color = Color.Lerp(EdgeSelectedColor, bright, intensity);
            yield return null;
        }
        edgeImage.color = EdgeSelectedColor;
    }
}
