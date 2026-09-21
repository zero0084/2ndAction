using UnityEngine;

[DefaultExecutionOrder(-50)] // 背景など追従物より先にカメラ位置を確定させる
[RequireComponent(typeof(Camera))]
public class CameraFollow : MonoBehaviour
{
    public Transform target;
    public float offsetX = 6f;
    public float yDamping = 0.15f;
    // 方向攻撃システム Ver.2、項目6 - "下降攻撃を使用した際に、安全な下層
    // へ移動しているのか、死亡領域へ向かっているのかが分からなくならな
    // いよう注意"。下降攻撃はdiveAttackSpeedによる一定の速い下降のため、
    // 通常のyDamping(SmoothDamp)だと追従が一瞬遅れ、実際の危険度より画
    // 面上のカメラ位置(≒PlayerController.DrawFallDeadlineWarningの見た
    // 目上の基準ではないが、プレイヤー自身の画面内位置)がズレて見える
    // 恐れがある。カメラシステム自体は作り直さず、下降攻撃中だけ追従を
    // 少し締める、という最小限の調整。
    public float yDampingDiveAttack = 0.05f;

    // Half the world-width the camera should show, regardless of screen
    // orientation. Orthographic size (vertical half-height) is derived from
    // this every frame, so portrait and landscape always show the same
    // horizontal "zoom" instead of portrait looking more zoomed in just
    // because the screen is narrower.
    public float targetHorizontalHalfWidth = 20.8f;

    // 高速走行の視認性補正(2026-09-22) - Playerの速度は変えず、速くなるほどカメラを少し引いて
    // 進行方向側(先)の表示領域を広げ、「同じ速度でも先を判断できる」ようにする。
    // 速度倍率(1〜最大)に比例し、SmoothDampでゆっくり追従させるので急なズームにならない。
    [Header("High-speed view (高速走行の視認性補正)")]
    public float highSpeedZoomOut = 0.12f;     // 最高速付近で視野を何割広げるか(0.12 = +12%)
    public float highSpeedLookAhead = 2.5f;    // 最高速付近でPlayerをさらに後方へ寄せる量(offsetXへ加算)
    public float highSpeedSmoothTime = 1.4f;   // 補正量が変化する速さ(秒)
    float speedBlend;      // 0=基礎速度 〜 1=最高速(平滑化済み)
    float speedBlendVel;
    public float SpeedBlend => speedBlend;

    Camera cam;
    Vector3 velocity;

    // Game Feel pass, section 19 - "非常に小さなShake" for boss/strong hits
    // only (see DragonController.TakeDamage) - never for a normal enemy
    // kill, and never anything big enough to risk reading as screen shake/
    // motion sickness. A simple decaying random jitter added on top of the
    // normal follow position each frame, not a separate transform.
    float shakeTimer;
    float shakeDuration = 1f;
    float shakeMagnitude;

    void Awake()
    {
        cam = GetComponent<Camera>();
    }

    public void Shake(float magnitude, float duration)
    {
        shakeMagnitude = magnitude;
        shakeDuration = Mathf.Max(0.001f, duration);
        shakeTimer = shakeDuration;
    }

    void LateUpdate()
    {
        var pc = PlayerController.Instance;
        float speedTarget = 0f;
        if (pc != null) speedTarget = Mathf.Clamp01((pc.SpeedRatio - 1f) / Mathf.Max(0.01f, pc.MaxSpeedRatio - 1f));
        speedBlend = Mathf.SmoothDamp(speedBlend, speedTarget, ref speedBlendVel, Mathf.Max(0.05f, highSpeedSmoothTime));

        if (cam != null && Screen.height > 0)
        {
            float aspect = (float)Screen.width / Screen.height;
            cam.orthographicSize = targetHorizontalHalfWidth * (1f + highSpeedZoomOut * speedBlend) / aspect;
        }

        if (shakeTimer > 0f) shakeTimer = Mathf.Max(0f, shakeTimer - Time.unscaledDeltaTime);

        if (target == null) return;

        // Freeze the camera in place once the win-ascension starts, so the
        // player visibly flies up and off the top of the screen instead of
        // the camera awkwardly chasing them forever.
        if (PlayerController.Instance != null && PlayerController.Instance.IsAscending) return;

        // Bugfix 2026-09-06, item 1 - "Level Up Card選択中はCamera Follow
        // も停止". LateUpdate runs every rendered frame regardless of
        // Time.timeScale (unlike Time.deltaTime-based movement, which
        // already freezes on its own), so this was re-snapping to the
        // player's X every single frame throughout a Level Up/Boss Reward
        // pause (and every brief HitStop.Freeze) even though the player
        // itself was correctly frozen - harmless today only because the
        // player never actually moves during those pauses, but explicit
        // now rather than relying on that coincidence.
        if (Time.timeScale <= 0f) return;

        Vector3 pos = transform.position;
        pos.x = target.position.x + offsetX + highSpeedLookAhead * speedBlend;
        bool diving = PlayerController.Instance != null && PlayerController.Instance.IsDiveAttacking;
        float smoothedY = Mathf.SmoothDamp(pos.y, target.position.y, ref velocity.y, diving ? yDampingDiveAttack : yDamping);
        pos.y = smoothedY;

        if (shakeTimer > 0f)
        {
            float falloff = shakeTimer / shakeDuration;
            pos.x += Random.Range(-1f, 1f) * shakeMagnitude * falloff;
            pos.y += Random.Range(-1f, 1f) * shakeMagnitude * falloff;
        }

        transform.position = pos;
    }
}
