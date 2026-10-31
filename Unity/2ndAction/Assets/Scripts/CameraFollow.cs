using UnityEngine;

[DefaultExecutionOrder(-50)] // 背景など追従物より先にカメラ位置を確定させる
[RequireComponent(typeof(Camera))]
public class CameraFollow : MonoBehaviour
{
    public Transform target;
    public float offsetX = 6f;
    [Tooltip("ボス戦の停止戦で止まっている間の offsetX(2026-10-29: 後ろも見えるように少し中央寄り)")]
    public float haltOffsetX = 3.5f;
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

    // ---- 縦画面(2026-10-08、依頼E-5): 「横から見る」縦のラン表示の構図。ゲームの判定(出現/画面端/ボスの間合い)は
    //      LogicalHalfWidth/LogicalCenterX(横画面と同じ値)を使うので、見た目の幅を変えてもゲームの進み方は変わらない。
    public static CameraFollow Instance { get; private set; }
    [Header("Portrait (縦画面の横から見る表示)")]
    public float portraitHalfWidth = 12f;         // 縦で見せる横幅の半分(横画面の 20.8 より狭く = キャラを大きく)
    [Range(0.6f, 0.9f)] public float portraitGroundFromTop = 0.78f; // 走っている地表を画面の上から何割の所に置くか
    public float portraitSurfaceSmooth = 0.35f;     // 地表の高さの追従の速さ(秒。ジャンプでは動かない)
    public float LogicalHalfWidth { get; private set; } = 20.8f;
    public float LogicalCenterX { get; private set; }
    public bool HasLogical { get; private set; }
    public bool PortraitSide { get; private set; }
    float surfaceY, surfaceVel; bool surfaceInit;

    // 縦の横から見る表示で、地表より下に見える高さ(地中の塗りの深さに使う)。横では 0
    public static float PortraitBelowGround()
    {
        if (!PortraitRunView.UseSidePortrait || Screen.height <= 0) return 0f;
        var cf = Instance;
        float half = cf != null ? cf.portraitHalfWidth : 12f;
        float fromTop = cf != null ? cf.portraitGroundFromTop : 0.78f;
        float aspect = (float)Screen.width / Screen.height;
        float ortho = half * 1.15f / aspect; // 高速時の引き(+12%)と余裕
        return ortho * 2f * (1f - fromTop);
    }

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
        Instance = this;
        FloatingOrigin.Shifted += OnOriginShifted;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (HudPrefs.DevArg("-bossCamOff") == "1") BossFramingDisabled = true; // 改修前の構図(比較の撮影用)
#endif
    }
    void OnDestroy() { FloatingOrigin.Shifted -= OnOriginShifted; if (Instance == this) Instance = null; }
    // 2026-10-10(全体点検): 判定用の画面の中心(LogicalCenterX)は時間が止まっている間(カード選択/ヒットストップ)は更新しないので、
    // その間に浮動原点のずらし(1,024m 単位)が起きると古い座標のまま約2km先を指し、敵の出現位置が2km先へ送られていた
    // (ボスが残ったままのラン再開の後、1分以上雑魚が出ない)。ずらした分だけ一緒に戻す。
    void OnOriginShifted(float s) { LogicalCenterX -= s; }

    public void Shake(float magnitude, float duration)
    {
        if (!GameSettings.ScreenShake) return; // 設定「画面揺れ: OFF」(2026-10-01)
        shakeMagnitude = magnitude;
        shakeDuration = Mathf.Max(0.001f, duration);
        shakeTimer = shakeDuration;
    }

    // ラストダンジョンのエンディング(2026-09-30): 演出が足すカメラの横ずれ(ONE MORE MILE?ではプレイヤーを画面中央へ)。通常は0。
    public static float ScriptedOffsetX;
    // #100 ULTIMATE(2026-10-04): 必殺技の間のズーム(1=通常、小さいほど寄る。滑らかに追う)と、前進中の先の見え方(m)
    public static float UltimateZoom = 1f;
    public static float UltimateLookAhead;
    float ultZoomNow = 1f;
    // FINAL EVOLUTION(2026-10-04): SPEED UP の超高速状態で少しだけ引く(1=通常)
    public static float FinalEvolutionZoom = 1f;
    float feZoomNow = 1f;
    // BOSS FINISH(2026-10-06): 最後の一撃の一瞬の寄り(1=通常。所有者は1つだけ = 重なって暴走しない)
    public static float BossFinishZoom = 1f;
    float bossZoomNow = 1f;

    public bool IsShaking => shakeTimer > 0f; // 確認用(QA)

    // ===== ボス戦の構図(2026-10-31、UI 全体の改善) =====
    // 横画面のボス戦は、カメラがプレイヤーの足元を画面の中央に置くため画面の下半分が地面の断面になり、キャラとボスが小さかった。
    // ボス戦の間だけ: 少し寄る(既定 13%)+ 地表を画面の下から 27% 前後に置く。地形・当たり・判定の画面幅(GameView)は変えない(見た目だけ)。
    //  ・交戦中のボス(BossCamFraming)とプレイヤーが画面に収まるよう、寄りを自動で弱める(複数のボス・打ち上げ・飛ぶボス)
    //  ・頭/ボスの上端は画面の下から bossTopFrac まで(上は HUD とボスの欄)、足元は bossBottomFrac より上
    //  ・始まり/終わりは bossBlendSeconds でなめらかに。走りながらの戦い(停止しない)は寄りを弱める(高速の見通し)
    //  ・縦画面(横から見る表示)は今までどおり(地表はもともと画面の下寄り)
    [Header("Boss fight framing (ボス戦の構図)")]
    public float bossZoomIn = 0.13f;
    [Range(0.15f, 0.45f)] public float bossGroundFrac = 0.27f;
    public float bossTopFrac = 0.80f, bossBottomFrac = 0.10f;
    public float bossBlendSeconds = 0.55f;
    public static bool BossFramingDisabled; // 確認用(改修前の構図と比べる: -bossCamOff 1)
    float bossBlend, bossZoom = 1f, bossZoomVel, bossY, bossYVel, bossGround, bossGroundVel, followY;
    bool bossGroundInit;
    int bossCount; float bossHoldUntil;
    readonly System.Collections.Generic.List<Bounds> bossBounds = new System.Collections.Generic.List<Bounds>();
    public float BossBlend => bossBlend;            // 確認用
    public float BossZoomApplied { get; private set; } = 1f;
    public int BossFramedCount => bossCount;
    static float BossEase(float t) => t * t * (3f - 2f * t);

    void LateUpdate()
    {
        var pc = PlayerController.Instance;
        float speedTarget = 0f;
        if (pc != null) speedTarget = Mathf.Clamp01((pc.SpeedRatio - 1f) / Mathf.Max(0.01f, pc.MaxSpeedRatio - 1f)) * pc.HaltFactor; // 2026-10-30 停止戦: 止まっている間は高速の引き(ズームアウト)を戻す(キャラとボスが小さくならない)
        speedBlend = Mathf.SmoothDamp(speedBlend, speedTarget, ref speedBlendVel, Mathf.Max(0.05f, highSpeedSmoothTime));

        if (cam != null && Screen.height > 0)
        {
            float aspect = (float)Screen.width / Screen.height;
            ultZoomNow = Mathf.Lerp(ultZoomNow, UltimateZoom, 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));
            feZoomNow = Mathf.Lerp(feZoomNow, FinalEvolutionZoom, 1f - Mathf.Exp(-4f * Time.unscaledDeltaTime));
            bossZoomNow = Mathf.Lerp(bossZoomNow, Mathf.Clamp(BossFinishZoom, 0.85f, 1f), 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
            float zoom = (1f + highSpeedZoomOut * speedBlend) * ultZoomNow * feZoomNow * bossZoomNow;
            LogicalHalfWidth = targetHorizontalHalfWidth * zoom;
            PortraitSide = PortraitRunView.UseSidePortrait;
            cam.orthographicSize = (PortraitSide ? portraitHalfWidth : targetHorizontalHalfWidth) * zoom / aspect;
            // ボス戦の寄り(見た目だけ。LogicalHalfWidth は上で決めた値のまま)。一時停止/ヒットストップの間は進めない
            if (Time.timeScale > 0f)
            {
                bool want = WantsBossFraming();
                bossBlend = Mathf.MoveTowards(bossBlend, want ? 1f : 0f, Time.unscaledDeltaTime / Mathf.Max(0.05f, bossBlendSeconds));
            }
            BossZoomApplied = Mathf.Lerp(1f, bossZoom, BossEase(bossBlend));
            cam.orthographicSize *= BossZoomApplied;
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
        // 攻撃の前進/後退の分だけカメラを遅らせる(画面上でキャラが踏み込む/下がるのが見える。2026-09-30)
        float stepOffset = PlayerController.Instance != null && target == PlayerController.Instance.transform ? PlayerController.Instance.ScreenStepOffset : 0f;
        // 2026-10-29 ボス戦の停止戦: 止まっている間は前の見通し(走りの先読み)を減らし、後ろも見えるようにする(すれ違ったボス/後ろ攻撃)
        float halt = PlayerController.Instance != null && target == PlayerController.Instance.transform ? 1f - PlayerController.Instance.HaltFactor : 0f;
        float offX = Mathf.Lerp(offsetX, haltOffsetX, halt), look = highSpeedLookAhead * speedBlend * (1f - halt);
        pos.x = target.position.x + offX + look - stepOffset + ScriptedOffsetX + UltimateLookAhead;
        LogicalCenterX = pos.x; HasLogical = true;
        bool diving = PlayerController.Instance != null && PlayerController.Instance.IsDiveAttacking;
        if (PortraitSide)
        {
            // 縦の横から見る表示: 横の位置は横画面と同じ割合(プレイヤーの前を広く)、縦は「走っている地表」を画面の下寄りに置く。
            // 地表の高さは地面に立っている時だけ更新し、ゆっくり追う(ジャンプのたびに上下しない)。下へ落ちていく時は追う
            float k = portraitHalfWidth / Mathf.Max(1f, targetHorizontalHalfWidth);
            pos.x = target.position.x + (offX + look) * k - stepOffset + ScriptedOffsetX + UltimateLookAhead * k;
            var pcs = PlayerController.Instance;
            float py = target.position.y;
            if (!surfaceInit) { surfaceY = py; surfaceInit = true; }
            float want = surfaceY;
            if (pcs == null || pcs.IsGrounded) want = py;
            else if (py < surfaceY - 1.5f) want = py; // 下の段/穴へ
            surfaceY = Mathf.SmoothDamp(surfaceY, want, ref surfaceVel, diving ? 0.08f : portraitSurfaceSmooth);
            float ortho = cam != null ? cam.orthographicSize : 20f;
            float y = surfaceY + ortho * (1f - 2f * (1f - portraitGroundFromTop));
            // 高く跳んだ時は頭が画面の上から出ないようにだけ上げる
            float topLimit = py + 3f - ortho * 0.85f;
            if (y < topLimit) y = topLimit;
            pos.y = Mathf.SmoothDamp(pos.y, y, ref velocity.y, 0.08f);
        }
        else
        {
            surfaceInit = false;
            if (bossBlend <= 0f)
            {
                float smoothedY = Mathf.SmoothDamp(pos.y, target.position.y, ref velocity.y, diving ? yDampingDiveAttack : yDamping);
                pos.y = smoothedY;
                followY = pos.y; bossY = pos.y; bossGroundInit = false; bossYVel = 0f;
                bossZoom = Mathf.SmoothDamp(bossZoom, 1f, ref bossZoomVel, 0.3f);
            }
            else
            {
                // ふだんの追従(プレイヤーの足元)とボス戦の構図を混ぜる(始まり/終わりがなめらか)
                followY = Mathf.SmoothDamp(followY, target.position.y, ref velocity.y, diving ? yDampingDiveAttack : yDamping);
                UpdateBossFraming(pos.x);
                pos.y = Mathf.Lerp(followY, bossY, BossEase(bossBlend));
            }
        }

        if (shakeTimer > 0f)
        {
            float falloff = shakeTimer / shakeDuration;
            pos.x += Random.Range(-1f, 1f) * shakeMagnitude * falloff;
            pos.y += Random.Range(-1f, 1f) * shakeMagnitude * falloff;
        }

        transform.position = pos;
    }

    bool WantsBossFraming()
    {
        if (BossFramingDisabled || PortraitSide || target == null) return false;
        var pc = PlayerController.Instance;
        if (pc == null || target != pc.transform || pc.IsAscending) return false;
        bossCount = BossCamFraming.Collect(pc.transform.position.x, bossBounds);
        if (bossCount > 0) bossHoldUntil = Time.unscaledTime + 1.5f;
        // 近くで戦っている間(離れていったボスも 60m まで)と、見失って 1.5 秒は構図を続ける(寄り/戻りを繰り返さない)
        return bossCount > 0 || BossCamFraming.NearCount > 0 || Time.unscaledTime < bossHoldUntil;
    }

    // 寄りの量と高さを決める(横画面)。cam.orthographicSize は今の寄りを掛けた後の値
    void UpdateBossFraming(float camX)
    {
        var pc = PlayerController.Instance;
        if (cam == null || pc == null) return;
        float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 1.78f;
        float baseOrtho = cam.orthographicSize / Mathf.Max(0.01f, BossZoomApplied);
        float px = pc.transform.position.x, py = pc.transform.position.y;
        // 地表: 足元の地面の高さ(穴の上では最後に見た地面)。跳んでも動かない
        float? g = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(px) : null;
        float gy = g ?? (pc.IsGrounded ? py : bossGround);
        if (!bossGroundInit) { bossGround = gy; bossGroundInit = true; bossGroundVel = 0f; }
        bossGround = Mathf.SmoothDamp(bossGround, gy, ref bossGroundVel, 0.25f);
        // 収める物: プレイヤー(頭まで)と交戦中のボス
        float top = py + 2.6f, bottom = Mathf.Min(py, bossGround), left = px - 1.5f, right = px + 1.5f;
        for (int i = 0; i < bossCount && i < bossBounds.Count; i++)
        {
            var b = bossBounds[i];
            top = Mathf.Max(top, b.max.y + 1.0f); // 頭の上の印の分
            bottom = Mathf.Min(bottom, b.min.y);
            left = Mathf.Min(left, b.min.x - 1.5f); right = Mathf.Max(right, b.max.x + 1.5f);
        }
        // 寄りの量: 走りながらの戦いは弱く。横に収まらない / 縦に収まらない時はその分だけ弱める(寄らない = 今までと同じ)
        float halt = 1f - pc.HaltFactor;
        float zMin = 1f - bossZoomIn * Mathf.Lerp(0.4f, 1f, halt);
        float needH = Mathf.Max(camX - left, right - camX) / (0.94f * baseOrtho * aspect);
        float needV = (top - bottom) / (2f * baseOrtho * (bossTopFrac - bossBottomFrac));
        float zWant = Mathf.Clamp(Mathf.Max(zMin, needH, needV), zMin, 1f);
        bossZoom = Mathf.SmoothDamp(bossZoom, zWant, ref bossZoomVel, 0.3f, Mathf.Infinity, Time.unscaledDeltaTime);
        // 高さ: 地表を画面の下から bossGroundFrac に。上/下の物が出る時だけずらす
        float ortho = baseOrtho * Mathf.Lerp(1f, bossZoom, BossEase(bossBlend));
        float y = bossGround + ortho * (1f - 2f * bossGroundFrac);
        float yMin = top + ortho * (1f - 2f * bossTopFrac);       // 上の物が画面の上から出ない
        float yMax = py + ortho * (1f - 2f * bossBottomFrac);     // プレイヤーの足元が下から出ない
        if (yMin > yMax) y = yMax; else y = Mathf.Clamp(y, yMin, yMax);
        bossY = Mathf.SmoothDamp(bossY, y, ref bossYVel, 0.18f);
    }
}
