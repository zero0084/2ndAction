using System.Collections;
using UnityEngine;

// ===== 竜騎士(2026-09-26) ===== //
// 5人目の主人公「巨大ランスの竜騎士」専用の4方向攻撃。CharacterDefinition.isLancer
// がtrueの間だけDoAttack/FireJump/HandleAttackInputからここへ分岐する。専用の
// 細長いHitbox(LanceHitbox)を実行時に1つだけ作って使い回し、既存の攻撃Hitbox/
// Slash VFX/コンボ処理(他4キャラの経路)には一切触れない。
//
//  前: 構え→突き(細長い判定+短い突進)→硬直。判定は体の少し前から始まるので、
//      懐に入り込まれた敵には当たらない(=懐が弱点)。複数の敵をまとめて貫く。
//  後: 石突きで後ろを小突く。短く・弱く・ノックバック弱め。
//  上: 通常ジャンプ+斜め上への突き上げ(打ち上げ/吸い込みは無し)。
//  下: 低い姿勢で前方下への突き(地上/空中どちらでも、急降下もホバーもしない)。
public partial class PlayerController
{
    bool isLancerCharacter;
    CharacterDefinition lancerDef;
    BoxCollider2D lanceHitbox;
    int lanceGeneration;
    // 攻撃ごとの一時倍率(EffectiveAttackPower/KnockbackPowerMultiplierに竜騎士の
    // 間だけ掛かる。他キャラは常に1のまま)。
    float lanceDamageScale = 1f;
    float lanceKnockbackScale = 1f;

    public enum LanceMoveKind { None, Forward, Backward, Down, Up }
    public LanceMoveKind LanceMove { get; private set; }
    // 0=構え/1=突き(PlayerAnimatorが攻撃フェーズに合わせてコマを選ぶ)。
    public int LanceFrameIndex { get; private set; }
    public bool IsLancer => isLancerCharacter;
    public BoxCollider2D LanceHitbox => lanceHitbox;

    void ApplyLancerStats(CharacterDefinition def)
    {
        isLancerCharacter = def.isLancer;
        lancerDef = def.isLancer ? def : null;
        CancelLanceMoves();
        if (isLancerCharacter && lanceHitbox == null)
        {
            var go = new GameObject("LanceHitbox");
            go.transform.SetParent(transform, false);
            go.tag = "PlayerAttack";
            go.AddComponent<PlayerAttackInfo>().kind = PlayerAttackKind.Normal;
            lanceHitbox = go.AddComponent<BoxCollider2D>();
            lanceHitbox.isTrigger = true;
            lanceHitbox.enabled = false;
            go.AddComponent<ColliderDebugView>().color = new Color(0.3f, 1f, 0.85f);
        }
    }

    // 走行速度倍率が1を超えた分だけ突進/ノックバックを強める(将来の速度連動の入口)。
    float LanceSpeedFactor()
    {
        if (lancerDef == null) return 1f;
        float over = Mathf.Max(0f, GetSpeedMultiplier() - 1f);
        return 1f + Mathf.Min(lancerDef.lanceSpeedBonusMax, over * lancerDef.lanceSpeedBonusPerSpeed);
    }

    // 突きの衝撃波(Resources/Effects/lancethrust、PPU512の正方形・中心ピボット・右向き)。
    // 絵のランスの穂先より先は、判定の終端までこの衝撃波で「届いている」ことを見せる。
    static Sprite lanceThrustArt;
    static bool lanceThrustLoaded;
    SpriteRenderer lanceFxRenderer;
    Coroutine lanceFxRoutine;
    // 絵の穂先の位置(判定の向きに沿った距離) - 衝撃波はここから判定の終端まで伸ばす。
    public float lanceArtTipDistance = 0.95f;

    void ShowLanceThrustFx(Vector2 origin, Vector2 dir, float deg, float from, float to, float duration)
    {
        if (!lanceThrustLoaded) { lanceThrustLoaded = true; lanceThrustArt = Resources.Load<Sprite>("Effects/lancethrust"); }
        if (lanceThrustArt == null || to <= from) return;
        if (lanceFxRenderer == null)
        {
            var go = new GameObject("LanceThrustFx");
            go.transform.SetParent(transform, false);
            lanceFxRenderer = go.AddComponent<SpriteRenderer>();
            lanceFxRenderer.sprite = lanceThrustArt;
            lanceFxRenderer.sortingOrder = RenderOrder.SlashFx;
        }
        var t = lanceFxRenderer.transform;
        Vector2 c = origin + dir * ((from + to) * 0.5f);
        t.localPosition = new Vector3(c.x, c.y, 0f);
        t.localRotation = Quaternion.Euler(0f, 0f, deg);
        if (lanceFxRoutine != null) StopCoroutine(lanceFxRoutine);
        lanceFxRoutine = StartCoroutine(LanceThrustFxRoutine(to - from, duration));
    }

    IEnumerator LanceThrustFxRoutine(float length, float duration)
    {
        var t = lanceFxRenderer.transform;
        lanceFxRenderer.enabled = true;
        float total = duration + 0.12f, e = 0f;
        while (e < total)
        {
            // 最初の0.06秒で前へ伸び切り、判定が消えた後0.12秒でフェードアウト。
            float grow = Mathf.Clamp01(e / 0.06f);
            float fade = e <= duration ? 1f : 1f - (e - duration) / 0.12f;
            t.localScale = new Vector3(length * Mathf.Lerp(0.55f, 1f, grow), length, 1f);
            lanceFxRenderer.color = new Color(1f, 1f, 1f, fade);
            e += Time.deltaTime;
            yield return null;
        }
        lanceFxRenderer.enabled = false;
        lanceFxRoutine = null;
    }

    // 判定を「開始位置startX〜長さlength、中心高さy、太さthick、角度deg」の帯に設定して有効化する。
    // いったん無効化してから有効化し直すので、前の攻撃で重なっていた敵にも改めて当たる。
    // fxDurationが正なら、絵の穂先から判定の終端まで突きの衝撃波を出す。
    void ArmLanceHitbox(float startX, float length, float y, float thick, float deg, float fxDuration = 0f)
    {
        if (lanceHitbox == null) return;
        lanceHitbox.enabled = false;
        var t = lanceHitbox.transform;
        Vector2 dir = new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad));
        Vector2 origin = new Vector2(0f, y);
        Vector2 center = origin + dir * (startX + length * 0.5f);
        t.localPosition = new Vector3(center.x, center.y, 0f);
        t.localRotation = Quaternion.Euler(0f, 0f, deg);
        // 大きさはTransformのScaleで持つ(ColliderDebugViewは生成時のsizeで枠を描くため)。
        t.localScale = new Vector3(length, thick, 1f);
        lanceHitbox.size = Vector2.one;
        lanceHitbox.offset = Vector2.zero;
        lanceHitbox.enabled = true;
        if (fxDuration > 0f) ShowLanceThrustFx(origin, dir, deg, lanceArtTipDistance, startX + length, fxDuration);
    }

    void DisarmLanceHitbox(int gen)
    {
        if (gen != lanceGeneration) return;
        if (lanceHitbox != null) lanceHitbox.enabled = false;
    }

    void CancelLanceMoves()
    {
        lanceGeneration++;
        if (lanceHitbox != null) lanceHitbox.enabled = false;
        if (lanceFxRoutine != null) { StopCoroutine(lanceFxRoutine); lanceFxRoutine = null; }
        if (lanceFxRenderer != null) lanceFxRenderer.enabled = false;
        LanceMove = LanceMoveKind.None;
        LanceFrameIndex = 0;
        lanceDamageScale = 1f;
        lanceKnockbackScale = 1f;
    }

    // 前/後攻撃(DoAttackから分岐)。
    IEnumerator DoLanceHorizontal(AttackDirection dir)
    {
        var d = lancerDef;
        int gen = attackGeneration;
        int lgen = ++lanceGeneration;
        bool back = dir == AttackDirection.Backward;
        isAttacking = true;
        comboWindowOpen = false;
        comboBuffered = false;
        comboCount++;
        transform.localScale = Vector3.one; // 後ろ攻撃でも向きは変えない(背中側を石突きで突く)
        lungeVelocityX = 0f;
        LanceMove = back ? LanceMoveKind.Backward : LanceMoveKind.Forward;
        LanceFrameIndex = 0;
        lanceDamageScale = back ? d.lanceBackDamageScale : 1f;
        float speedFactor = back ? 1f : LanceSpeedFactor();
        lanceKnockbackScale = back ? d.lanceBackKnockbackScale : speedFactor;

        float s = AttackSpeedMultiplier;
        float windup = d.lanceWindup * s, active = d.lanceThrustActive * s, recovery = d.lanceRecovery * s;
        attackCooldownTimer = (windup + active + recovery) + attackCooldown * s;

        // 構え
        float t = 0f;
        while (t < windup)
        {
            if (gen != attackGeneration || lgen != lanceGeneration) yield break;
            t += Time.deltaTime; yield return null;
        }

        // 突き
        if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(back ? 1 : 3);
        LanceFrameIndex = 1;
        float reach = (back ? d.lanceBackReach : d.lanceReach) * AttackRangeMultiplier;
        if (back) ArmLanceHitbox(0.3f, reach, d.lanceHeight, d.lanceThickness, 180f);
        else ArmLanceHitbox(d.lanceReachStart, reach, d.lanceHeight, d.lanceThickness, 0f, active);
        if (!back) lungeVelocityX = d.lanceDashDistance * speedFactor / Mathf.Max(0.01f, active);
        t = 0f;
        while (t < active)
        {
            if (gen != attackGeneration || lgen != lanceGeneration) yield break;
            t += Time.deltaTime; yield return null;
        }
        lungeVelocityX = 0f;
        DisarmLanceHitbox(lgen);

        // 硬直(後半でだけ次の突きを予約できる)
        bool allowChain = comboCount < maxComboChain;
        t = 0f;
        while (t < recovery)
        {
            if (gen != attackGeneration || lgen != lanceGeneration) yield break;
            if (allowChain && t >= recovery * 0.55f) comboWindowOpen = true;
            t += Time.deltaTime; yield return null;
        }

        isAttacking = false;
        comboWindowOpen = false;
        LanceMove = LanceMoveKind.None;
        LanceFrameIndex = 0;
        lanceDamageScale = 1f;
        lanceKnockbackScale = 1f;

        if (comboBuffered)
        {
            comboBuffered = false;
            attackCooldownTimer = 0f;
            StartCoroutine(DoAttack(bufferedDirection));
        }
    }

    // 上攻撃(FireJumpから分岐、ジャンプ自体はFireJumpが既に発動済み)。
    IEnumerator DoLanceUpThrust()
    {
        var d = lancerDef;
        int lgen = ++lanceGeneration;
        if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(2);
        LanceMove = LanceMoveKind.Up;
        lanceDamageScale = 1f;
        lanceKnockbackScale = 1f;
        upShotVisualTimer = Mathf.Max(upShotVisualTimer, d.lanceUpActive + 0.12f);
        ArmLanceHitbox(0.35f, d.lanceUpReach * AttackRangeMultiplier, d.lanceHeight + 0.15f, d.lanceThickness, d.lanceUpAngle, d.lanceUpActive);
        float t = 0f;
        while (t < d.lanceUpActive)
        {
            if (lgen != lanceGeneration) yield break;
            t += Time.deltaTime; yield return null;
        }
        DisarmLanceHitbox(lgen);
        if (lgen == lanceGeneration) LanceMove = LanceMoveKind.None;
    }

    // 下攻撃(HandleAttackInputから、地上/空中どちらでも)。
    void TryLanceDownThrust()
    {
        if (isAttacking || attackCooldownTimer > 0f) return;
        StartCoroutine(DoLanceDownThrust());
    }

    IEnumerator DoLanceDownThrust()
    {
        var d = lancerDef;
        int gen = attackGeneration;
        int lgen = ++lanceGeneration;
        isAttacking = true;
        comboWindowOpen = false;
        comboBuffered = false;
        comboCount = 1;
        transform.localScale = Vector3.one;
        lungeVelocityX = 0f;
        LanceMove = LanceMoveKind.Down;
        LanceFrameIndex = 0;
        lanceDamageScale = 1f;
        lanceKnockbackScale = 1f;

        float s = AttackSpeedMultiplier;
        float windup = d.lanceWindup * 0.7f * s, active = d.lanceDownActive * s, recovery = d.lanceRecovery * 0.8f * s;
        attackCooldownTimer = windup + active + recovery + attackCooldown * s;

        float t = 0f;
        while (t < windup)
        {
            if (gen != attackGeneration || lgen != lanceGeneration) yield break;
            t += Time.deltaTime; yield return null;
        }
        if (AudioManager.Instance != null) AudioManager.Instance.PlayAttack(2);
        LanceFrameIndex = 1;
        // 空中では少し下向きに突き下ろす(足元の敵に届くよう)。
        float deg = isGrounded ? -8f : -28f;
        ArmLanceHitbox(0.3f, d.lanceDownReach * AttackRangeMultiplier, d.lanceDownHeight + (isGrounded ? 0.12f : 0.3f), d.lanceThickness * 0.9f, deg, active);
        t = 0f;
        while (t < active)
        {
            if (gen != attackGeneration || lgen != lanceGeneration) yield break;
            t += Time.deltaTime; yield return null;
        }
        DisarmLanceHitbox(lgen);
        t = 0f;
        while (t < recovery)
        {
            if (gen != attackGeneration || lgen != lanceGeneration) yield break;
            t += Time.deltaTime; yield return null;
        }
        isAttacking = false;
        LanceMove = LanceMoveKind.None;
        LanceFrameIndex = 0;
    }
}
