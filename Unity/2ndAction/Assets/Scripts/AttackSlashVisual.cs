using UnityEngine;

// Plays the sword-swing slash VFX as a short one-shot animation during an
// attack. Scales up across the combo chain (small -> medium -> large) so the
// swing's visible size communicates how many hits deep the player is,
// matching the hitbox's own growth.
//
// 攻撃エフェクト全面調整(2026-09-08) - 「巨大な紫剣」(framesベースの複数
// コマ、旧AttackSlashFx)を主役にする代わりに、「剣の軌跡に沿った控えめな
// 青白いエフェクト」を主役にする新方針向けに、1枚絵をScale/Alphaで演出す
// る軽量な単発モード(PlaySingle)と、下降攻撃のような「持続時間が不定
// (着地まで)」なケース向けの持続表示モード(ShowSustained/HideSustained)
// を追加。既存のframes方式(通常攻撃の6コマ)はそのまま維持 - 今回のパス
// では地上/空中上攻撃・下降攻撃のみ新方式へ切り替え、通常攻撃は次回以降
// 問題なければ展開する方針(マスターの明示指示どおり、段階的に展開)。
[RequireComponent(typeof(SpriteRenderer))]
public class AttackSlashVisual : MonoBehaviour
{
    public Sprite[] frames;
    public float fps = 22f;

    [Header("Stage 1 (first hit in a chain) - smallest swing")]
    public float baseScale = 0.75f;
    // Growth per additional combo hit.
    public float scaleStep = 0.3f;

    [Header("Common tuning - Inspectorから調整可能")]
    // VFXの不透明度(0-1)。実機確認後のバランス調整用 - 「エフェクトは主
    // 役ではなくキャラクターの攻撃モーションを強調する役割」という方針
    // どおり、デフォルトでもやや控えめ(1.0未満)にしてある。
    [Range(0f, 1f)] public float opacity = 0.85f;
    // RenderOrder.SlashFx(Awakeで設定される基準値)に対する相対オフセッ
    // ト。0のままなら従来どおり。
    public int sortingOrderOffset = 0;

    [Header("Single-sprite mode (新VFX用) - PlaySingle/ShowSustained参照")]
    // 設定されていれば、frames配列によるコマ送りの代わりに、この1枚絵を
    // 「小さく開始→拡大→薄れながら消える」という手続き的な動きで見せる
    // 軽量モードを使う(新しい三日月/縦方向トレイル素材のような、複数コマ
    // ではなく1枚絵の静止画として供給されたVFX向け)。
    public Sprite singleSprite;
    public float singleDuration = 0.22f;
    [Range(0f, 1f)] public float singleStartScaleFraction = 0.4f;
    [Range(0f, 1f)] public float singleGrowFraction = 0.3f; // durationのうち拡大に使う割合
    [Range(0f, 1f)] public float singleFadeStartFraction = 0.55f; // durationのこの割合を過ぎたらフェード開始

    // Bugfix 2026-09-06 - "Attack Range Upが見た目で分からない". The Range
    // cards' AttackRangeMultiplier already drove the (invisible) attack
    // hitbox's own scale/reach - see PlayerController.ApplyComboStageToHitbox
    // - but was never passed to this VFX at all, so the swing looked
    // identical regardless of how many Range cards were stacked. Deliberately
    // reuses that SAME multiplier (rather than a separately-tuned visual
    // curve) so Hitbox Range and Visual Range can never drift apart - the
    // brief's own explicit warning. influenceX/Y let the visual "punch" be
    // tuned independently of the raw multiplier: 1.0 = exactly 1:1 with
    // AttackRangeMultiplier, 0 = ignore it entirely. X gets the full
    // influence (reads as "reaches further"), Y a smaller fraction (a
    // little more arc height too, without making the swing look like a
    // bigger blob instead of a longer one).
    [Header("Attack Range Up - visual reach (Inspector-tunable)")]
    public float rangeInfluenceX = 1f;
    public float rangeInfluenceY = 0.4f;

    SpriteRenderer sr;
    int frameIndex;
    float frameTimer;
    bool playing;

    // 攻撃エフェクト全面調整(2026-09-08) - singleSprite用の状態。
    bool playingSingle;
    float singleTimer;
    float singleTargetScaleX, singleTargetScaleY;

    // 下降攻撃のように「発動〜着地まで」不定長で表示し続けるモード。
    // Updateの通常のコマ送り/フェード処理を一切行わない(ShowSustained/
    // HideSustainedで明示的に切り替えるだけ) - PlayerController.
    // DoDiveAttack/EndDiveAttackから呼ばれる。
    bool sustained;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        sr.sortingOrder = RenderOrder.SlashFx + sortingOrderOffset;
        sr.enabled = false;
    }

    // stage 1 = first hit (smallest arc) up through the combo cap (largest,
    // the "finisher" swing). rangeMultiplier is PlayerController.
    // AttackRangeMultiplier verbatim (1.0 = no Range cards stacked).
    public void SetComboStage(int stage, float rangeMultiplier = 1f)
    {
        if (frames == null || frames.Length == 0) return;

        int step = Mathf.Max(0, stage - 1);
        float scale = baseScale + scaleStep * step;
        float rangeDelta = Mathf.Max(0f, rangeMultiplier - 1f);
        float scaleX = scale * (1f + rangeDelta * rangeInfluenceX);
        float scaleY = scale * (1f + rangeDelta * rangeInfluenceY);
        transform.localScale = new Vector3(scaleX, scaleY, 1f);

        playingSingle = false;
        sustained = false;
        frameIndex = 0;
        frameTimer = 0f;
        playing = true;
        sr.enabled = true;
        sr.color = new Color(1f, 1f, 1f, opacity);
        sr.sprite = frames[0];
    }

    // 攻撃エフェクト全面調整(2026-09-08) - 1枚絵を「小さく開始→拡大→
    // フェードアウト」で見せる単発再生。地上/空中上攻撃の斬撃で使用(剣の
    // 軌跡に沿った控えめなエフェクト、という新方針)。rangeMultiplierの
    // 扱いはSetComboStageと同じ(Attack Range UpカードでVFXも一緒に拡大)。
    public void PlaySingle(float scale = 1f, float rangeMultiplier = 1f)
    {
        if (singleSprite == null) return;

        float rangeDelta = Mathf.Max(0f, rangeMultiplier - 1f);
        singleTargetScaleX = scale * (1f + rangeDelta * rangeInfluenceX);
        singleTargetScaleY = scale * (1f + rangeDelta * rangeInfluenceY);

        playing = false;
        sustained = false;
        playingSingle = true;
        singleTimer = 0f;
        sr.enabled = true;
        sr.sprite = singleSprite;
        transform.localScale = new Vector3(singleTargetScaleX * singleStartScaleFraction, singleTargetScaleY * singleStartScaleFraction, 1f);
        sr.color = new Color(1f, 1f, 1f, opacity);
    }

    // 攻撃エフェクト全面調整(2026-09-08) - 下降攻撃の急降下中に「後方へ
    // 伸びる風圧・残像」を持続表示するためのモード。呼び出し側
    // (PlayerController.DoDiveAttack)が着地までのHideSustained呼び出しを
    // 責任を持って行う前提(このクラス自身にはタイムアウトを持たせない -
    // 「地上なのに表示され続ける」ような取りこぼしを避けたい場合は呼び出
    // し側で保証すること)。
    public void ShowSustained(float scale = 1f)
    {
        Sprite s = singleSprite != null ? singleSprite : (frames != null && frames.Length > 0 ? frames[0] : null);
        if (s == null) return;

        playing = false;
        playingSingle = false;
        sustained = true;
        sr.enabled = true;
        sr.sprite = s;
        transform.localScale = new Vector3(scale, scale, 1f);
        sr.color = new Color(1f, 1f, 1f, opacity);
    }

    public void HideSustained()
    {
        sustained = false;
        sr.enabled = false;
    }

    void Update()
    {
        if (sustained) return; // ShowSustained/HideSustainedが明示的に管理

        if (playingSingle)
        {
            singleTimer += Time.deltaTime;
            float t = singleDuration > 0f ? Mathf.Clamp01(singleTimer / singleDuration) : 1f;

            float growT = singleGrowFraction > 0f ? Mathf.Clamp01(t / singleGrowFraction) : 1f;
            float scaleFrac = Mathf.Lerp(singleStartScaleFraction, 1f, growT);
            transform.localScale = new Vector3(singleTargetScaleX * scaleFrac, singleTargetScaleY * scaleFrac, 1f);

            float alpha = opacity;
            if (t > singleFadeStartFraction)
            {
                float fadeT = (t - singleFadeStartFraction) / Mathf.Max(0.0001f, 1f - singleFadeStartFraction);
                alpha = Mathf.Lerp(opacity, 0f, fadeT);
            }
            Color c = sr.color;
            sr.color = new Color(c.r, c.g, c.b, alpha);

            if (t >= 1f)
            {
                playingSingle = false;
                sr.enabled = false;
            }
            return;
        }

        if (!playing) return;

        frameTimer += Time.deltaTime;
        if (frameTimer >= 1f / fps)
        {
            frameTimer = 0f;
            frameIndex++;
            if (frameIndex >= frames.Length)
            {
                playing = false;
                sr.enabled = false;
                return;
            }
            sr.sprite = frames[frameIndex];
        }
    }
}
