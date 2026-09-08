using System.Collections;
using UnityEngine;

// How this enemy relates to the ground - the single source of truth other
// components (EnemyAnimator, TerrainManager's spawn placement) read instead
// of each independently guessing from a raw bool. Only Ground is actually
// used today (the goblin art always spawns as one - see TerrainManager),
// but the type exists so a future flying enemy species has a real place to
// plug into the same spawn/animation pipeline instead of needing its own.
public enum EnemyMovementType
{
    Ground,
    Flying
}

public class EnemyController : MonoBehaviour
{
    public EnemyMovementType movementType = EnemyMovementType.Ground;

    // Distance Level Design Ver.1 - "EnemyHP = 1 + floor(CurrentDistance /
    // 2000)" (see DistanceTierManager.CurrentEnemyHp) times the species'
    // own hpMultiplier (EnemyDefinition.hpMultiplier - Heavy's "追加倍率").
    // Set by GroundFactory.CreateEnemy right after spawn, BEFORE this
    // component's own Awake runs its first frame - never left at the
    // default 1 for a species that should be tougher. A hit that doesn't
    // bring hp to 0 now plays a NON-lethal reaction (NonLethalHit) instead
    // of always killing on the first hit like every enemy used to.
    public int maxHp = 1;
    int hp = -1; // -1 sentinel: not yet initialized from maxHp (see EnsureHp)
    void EnsureHp() { if (hp < 0) hp = Mathf.Max(1, maxHp); }

    // Reward/MILE System Ver.1 - set by GroundFactory.CreateEnemy from this
    // spawn's EnemyDefinition.mileReward, handed to GameManager.
    // RegisterEnemyKill on the killing blow (see HitAndDie below).
    public int mileReward = 1;

    // Heavy Enemy - "画面端まで吹っ飛ばすような感じ" on every non-lethal
    // hit; reuses the existing small KnockbackRoutine, just with these two
    // set much larger (see GroundFactory.CreateEnemy) instead of a new
    // mechanic. Every other species keeps the original small "punch" feel.

    // Polish Pass 1, item 2 - makes the moment an attack actually lands
    // read clearly, without slowing the game down. Everything here is
    // individually toggleable/tunable and purely cosmetic (kill timing
    // shifts by at most hitFlashHoldDuration, well under a frame's worth
    // of gameplay-relevant delay) - RegisterEnemyKill/scoring/damage
    // numbers are completely untouched.
    [Header("Polish Pass 1 - Hit Feedback (tunable)")]
    public bool hitFlashEnabled = true;
    public Color hitFlashColor = Color.white;
    // How long the flash+knockback hold before the explosion/despawn plays
    // - deliberately brief so the kill still feels instant, not delayed.
    public float hitFlashHoldDuration = 0.05f;
    public bool hitStopEnabled = true;
    [Range(0.02f, 0.08f)] public float hitStopDuration = 0.03f;
    // Game Feel pass - re-enabled with an actual eased tween (see
    // KnockbackRoutine) instead of the old instant position snap that was
    // reported as not reading well; still small/brief per "小さなKnockback".
    public bool hitKnockbackEnabled = true;
    public float hitKnockbackDistance = 0.1f;
    public float hitKnockbackDuration = 0.08f;
    public bool hitParticleEnabled = true;
    // Assets/Art/Effects/HitSpark.png (see SceneBuilder) - falls back to
    // the plain procedural dot if not assigned, so this never breaks an
    // older scene build.
    public Sprite hitSparkSprite;
    // Visibility Pass - alpha raised to fully opaque (was 0.85); "Hit Spark
    // は攻撃命中の重要Feedbackなので、他より多少目立って構わない".
    public Color hitParticleColor = new Color(1f, 1f, 1f, 1f);
    // Game Feel refinement pass - smaller and much shorter than before
    // ("はっきり見えることより、一瞬光ったと感じる程度" - the spark isn't
    // meant to be the focal point of the hit, just a quick flash confirming
    // it landed) and now spawned at the actual attack-hitbox/enemy contact
    // point (see HitAndDie) instead of the enemy's own center.
    //
    // Visibility Pass (2026-09-02): that pass turned out to have gone too
    // far - scale/duration raised ~1.75x (was 0.3/0.11) so a hit reads
    // clearly instead of "a single frame" ("1フレームのように見えない状態
    // は避けてください"), with a short hold before fading (see HitAndDie).
    public float hitParticleScale = 0.52f;
    public float hitParticleDuration = 0.2f;
    [Range(0f, 1f)] public float hitParticleHoldFraction = 0.25f;
    // Assets/Art/Effects/EnemyDeathSmoke.png (Game Feel refinement pass) -
    // used SMALL (deathCloudScale), as a brief accent alongside the
    // enemy's own Hit->Flash->Fade/Scale->消滅 sequence (DieFadeRoutine),
    // never at anywhere near the enemy's own full size - "Enemyと同じ大き
    // さでそのまま表示する必要はありません。小さな消滅Particleとして".
    //
    // Visibility Pass: scale/duration raised ~1.5x (was 0.5/0.3s) with a
    // hold window - "Enemy消滅と同時に全部消えるのではなく、Death Smokeが
    // 少し残ってからFade". Already spawned as its own independent
    // GameObject (not parented to the enemy - see HitAndDie), so it already
    // naturally outlives the enemy's own SetActive(false); this just makes
    // that lingering moment last long enough to actually notice.
    public Sprite deathCloudSprite;
    public float deathCloudScale = 0.75f;
    public float deathCloudDuration = 0.5f;
    [Range(0f, 1f)] public float deathCloudHoldFraction = 0.3f;
    // A short scale-down + fade on the enemy's own sprite right before it
    // disappears, so "Hit -> Flash -> Fade/Scale -> 消滅" reads as one
    // continuous reaction instead of the sprite just vanishing outright.
    public float dieFadeDuration = 0.14f;

    SpriteRenderer sr;
    bool dying;

    void Awake()
    {
        // The SpriteRenderer lives on the "Visual" child, not this Root -
        // see GroundFactory.CreateEnemy.
        sr = GetComponentInChildren<SpriteRenderer>();
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (dying) return;

        if (other.CompareTag("PlayerAttack"))
        {
            EnsureHp();
            // The actual point the two colliders meet, not either object's
            // center - other.ClosestPoint(transform.position) is the point
            // on the ATTACK HITBOX's own boundary nearest this enemy, which
            // reads as "where the blade actually reached" (see HitAndDie's
            // spark spawn) rather than always landing dead-center on the
            // enemy regardless of which side got hit.
            Vector3 contactPoint = other.ClosestPoint(transform.position);
            int damage = PlayerController.Instance != null ? PlayerController.Instance.EffectiveAttackPower : 1;
            hp -= Mathf.Max(1, damage);

            if (hp > 0)
            {
                // Distance Level Design Ver.1 - a multi-hp species (any
                // maxHp>1 - see EnemyDefinition.hpMultiplier) survives this
                // hit: flash/knock/spark, but no death sequence, no
                // RegisterEnemyKill.
                StartCoroutine(NonLethalHit(contactPoint));
                return;
            }

            dying = true;
            // EnemyAnimator's idle squash/sway (Update, unconditional) sets
            // visual.localScale/localRotation every frame - left enabled,
            // it would fight DieFadeRoutine's own scale-down below for
            // ownership of the exact same Transform, flickering between
            // the two every other frame depending on component execution
            // order. The death sequence owns this Transform exclusively
            // from here on.
            var idleAnimator = GetComponent<EnemyAnimator>();
            if (idleAnimator != null) idleAnimator.enabled = false;
            var specialBehavior = GetComponent<EnemySpecialBehavior>();
            if (specialBehavior != null) specialBehavior.enabled = false;
            StartCoroutine(HitAndDie(contactPoint));
            return;
        }

        if (other.CompareTag("Player"))
        {
            if (PlayerController.Instance != null) PlayerController.Instance.TakeDamage();
        }
    }

    // Distance Level Design Ver.1 - the multi-hp survive-a-hit reaction:
    // the same Hit Spark/Flash/Knockback HitAndDie already used for the
    // killing blow, just without the death half (no Death Smoke, no
    // DieFadeRoutine, no SetActive(false)/RegisterEnemyKill). Heavy's
    // "画面端まで吹っ飛ばす" is just hitKnockbackDistance/Duration set much
    // larger at spawn time (see GroundFactory.CreateEnemy) - this method
    // itself doesn't know or care which species it's playing for.
    //
    // 不具合修正/演出調整(2026-09-09) - 「雑魚敵が攻撃を受けた後に体力が
    // 0でなければ、右上（進行方向のすこし上部）へ画面端へ飛ぶように」。
    // 従来の小さな水平パンチ(KnockbackRoutine)の代わりに、専用の
    // LaunchAwayRoutineへ差し替え - 現在のカメラ視野の右上端の少し外側
    // まで実際に飛んでいき、画面外に出たところで(击破ではなく)非アクティ
    // ブ化する(HPは0のまま残る値だが、RegisterEnemyKill/MILE報酬などの
    // 撃破処理は一切行わない - 生存中の敵をその場から取り除くだけ)。
    IEnumerator NonLethalHit(Vector3 contactPoint)
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlayAttackHit();

        if (hitKnockbackEnabled)
        {
            StartCoroutine(LaunchAwayRoutine());
        }

        if (hitParticleEnabled)
        {
            Sprite spark = hitSparkSprite != null ? hitSparkSprite : OneShotSpriteEffect.SoftDotSprite();
            OneShotSpriteEffect.CreateTweened(spark, contactPoint, hitParticleColor, duration: hitParticleDuration, startScale: hitParticleScale * 0.7f, endScale: hitParticleScale, sortingOrder: RenderOrder.CombatFx, holdFraction: hitParticleHoldFraction);
        }

        if (hitStopEnabled) yield return HitStop.Freeze(hitStopDuration);

        // Flash to hitFlashColor and back (HitAndDie's flash never reverts -
        // the enemy dies right after it - but a surviving enemy needs to
        // visibly return to normal, or every subsequent hit would just look
        // like nothing changed).
        if (hitFlashEnabled && sr != null)
        {
            Color normal = sr.color;
            sr.color = hitFlashColor;
            yield return new WaitForSecondsRealtime(Mathf.Max(0.04f, hitFlashHoldDuration));
            if (sr != null) sr.color = normal;
        }
    }

    IEnumerator HitAndDie(Vector3 contactPoint)
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlayAttackHit();

        if (hitKnockbackEnabled)
        {
            float dir = PlayerController.Instance != null
                ? Mathf.Sign(transform.position.x - PlayerController.Instance.transform.position.x)
                : 1f;
            StartCoroutine(KnockbackRoutine(dir));
        }

        if (hitParticleEnabled)
        {
            Sprite spark = hitSparkSprite != null ? hitSparkSprite : OneShotSpriteEffect.SoftDotSprite();
            // A quick small pop (not just a fade) - grows slightly then
            // gone, over hitParticleDuration, held briefly at full alpha
            // first so it doesn't read as a single frame.
            OneShotSpriteEffect.CreateTweened(spark, contactPoint, hitParticleColor, duration: hitParticleDuration, startScale: hitParticleScale * 0.7f, endScale: hitParticleScale, sortingOrder: RenderOrder.CombatFx, holdFraction: hitParticleHoldFraction);
        }

        if (hitFlashEnabled && sr != null) sr.color = hitFlashColor;

        if (hitStopEnabled) yield return HitStop.Freeze(hitStopDuration);
        if (hitFlashHoldDuration > 0f) yield return new WaitForSecondsRealtime(hitFlashHoldDuration);

        if (deathCloudSprite != null)
        {
            // "Enemy撃破 -> Death Smoke生成 -> ... -> Effect Fade Out" - one
            // small tweened accent (grows in slightly, then fades), not a
            // multi-particle burst this time (see the field comment).
            OneShotSpriteEffect.CreateTweened(deathCloudSprite, transform.position, Color.white, duration: deathCloudDuration, startScale: deathCloudScale * 0.7f, endScale: deathCloudScale, sortingOrder: RenderOrder.CombatFx, holdFraction: deathCloudHoldFraction);
        }
        if (AudioManager.Instance != null) AudioManager.Instance.PlayEnemyDefeat();

        // The enemy's own sprite side of "Hit -> Flash -> Fade/Scale ->
        // 消滅" - previously this jumped straight from the flash hold to
        // gameObject.SetActive(false) with no transition at all, which is
        // what read as the sprite just vanishing outright.
        if (sr != null) yield return DieFadeRoutine();

        gameObject.SetActive(false);
        if (GameManager.Instance != null) GameManager.Instance.RegisterEnemyKill(mileReward);
    }

    IEnumerator DieFadeRoutine()
    {
        Color startColor = sr.color;
        Vector3 startScale = sr.transform.localScale;
        float t = 0f;
        while (t < dieFadeDuration)
        {
            t += Time.unscaledDeltaTime;
            float frac = Mathf.Clamp01(t / dieFadeDuration);
            Color c = startColor;
            c.a = Mathf.Lerp(startColor.a, 0f, frac);
            sr.color = c;
            sr.transform.localScale = Vector3.Lerp(startScale, startScale * 0.6f, frac);
            yield return null;
        }
    }

    // A short eased punch-back-and-settle instead of an instant position
    // snap - out fast, drifts back only partway (doesn't undo the whole hit,
    // reads as "knocked" not "teleported and un-teleported").
    IEnumerator KnockbackRoutine(float dir)
    {
        Vector3 start = transform.position;
        Vector3 peak = start + new Vector3(hitKnockbackDistance * dir, 0f, 0f);
        float t = 0f;
        while (t < hitKnockbackDuration)
        {
            // Bugfix 2026-09-06, item 1 - "Level Up Card選択中にEnemyが動き
            // 続ける". This drives transform.position directly, so it must
            // respect Time.timeScale like every other gameplay movement in
            // the project (see HitStop.cs's own class comment - "so every
            // animation/movement/physics update actually pauses" is already
            // this project's stated design principle; this one coroutine
            // was the exception). Was Time.unscaledDeltaTime - a knockback
            // already mid-flight when Level Up (Time.timeScale=0) or even
            // this enemy's own HitStop.Freeze triggers kept sliding across
            // the screen throughout, since unscaled time never stops.
            t += Time.deltaTime;
            float frac = Mathf.Clamp01(t / hitKnockbackDuration);
            // Out quickly (eased), then settle back about halfway - never
            // fully undoes the punch, so it still reads as a net shove.
            float outFrac = 1f - Mathf.Pow(1f - Mathf.Clamp01(frac / 0.4f), 2f);
            float settleFrac = frac > 0.4f ? Mathf.Clamp01((frac - 0.4f) / 0.6f) * 0.5f : 0f;
            transform.position = Vector3.Lerp(start, peak, Mathf.Clamp01(outFrac) - settleFrac);
            yield return null;
        }
    }

    // 不具合修正/演出調整(2026-09-09) - 「体力が0でなければ、右上（進行方
    // 向のすこし上部）へ画面端へ飛ぶように」。KnockbackRoutine(その場で
    // 小さく揺れて戻るだけ)とは別の、現在のカメラ視野を基準に「右上の
    // 画面端の少し外側」まで実際に飛んでいく専用ルーチン。撃破ではない
    // ので RegisterEnemyKill/MILE報酬/Death Smoke等の撃破演出は一切行わ
    // ない - 画面外へ出たところでSetActive(false)するだけ(HPは0のまま
    // 残らず、単にその場から取り除かれる)。
    [Header("Non-lethal Hit - Launch Away (Inspectorから調整可能)")]
    public float launchAwayDuration = 0.45f;
    // 画面端からどれだけ外側まで飛ばすか(カメラ半幅/半高に対する比率) -
    // 1.0でちょうど画面端、それより大きいほど完全に画面外まで飛ぶ。
    public float launchAwayOvershootX = 1.35f;
    public float launchAwayOvershootY = 0.9f;

    IEnumerator LaunchAwayRoutine()
    {
        // 飛んでいる間は再度PlayerAttack/Playerと衝突しないよう、判定を
        // 止めておく(この後SetActive(false)するので最終的には自動的に
        // 解決するが、それまでの短い間に暴れないようにする保険)。
        foreach (Collider2D col in GetComponentsInChildren<Collider2D>())
        {
            col.enabled = false;
        }
        // HitAndDie(致死Hit)側と同じ理由 - EnemyAnimator(idle squash/sway)
        // やEnemySpecialBehavior(独自の移動)がtransform位置を毎フレーム
        // 上書きし続けると、この後の飛翔Tweenと同じTransformを奪い合って
        // 目に見えてガクつく/目的地まで届かない、という不具合になる
        // (HitAndDieのコメント参照、同じクラスの問題)。
        var idleAnimator = GetComponent<EnemyAnimator>();
        if (idleAnimator != null) idleAnimator.enabled = false;
        var specialBehavior = GetComponent<EnemySpecialBehavior>();
        if (specialBehavior != null) specialBehavior.enabled = false;

        Camera cam = Camera.main;
        Vector3 start = transform.position;
        Vector3 target = start + new Vector3(3f, 3f, 0f); // camがまだ無い場合の最低限のフォールバック
        if (cam != null)
        {
            float halfHeight = cam.orthographicSize;
            float halfWidth = halfHeight * cam.aspect;
            Vector3 camPos = cam.transform.position;
            // 「右上（進行方向のすこし上部）へ画面端へ」 - 進行方向(常に+X)
            // 側の右上角の少し外側を狙う。
            target = new Vector3(camPos.x + halfWidth * launchAwayOvershootX, camPos.y + halfHeight * launchAwayOvershootY, 0f);
        }

        float t = 0f;
        while (t < launchAwayDuration)
        {
            // KnockbackRoutine同様、Time.timeScaleに従う(Level Up選択中等
            // に飛び続けないように)。
            t += Time.deltaTime;
            float frac = Mathf.Clamp01(t / launchAwayDuration);
            // 加速していくイーズイン(吹っ飛ばされた勢いが増していく感じ)。
            float eased = frac * frac;
            transform.position = Vector3.Lerp(start, target, eased);
            yield return null;
        }

        gameObject.SetActive(false);
    }
}
