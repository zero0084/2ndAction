using UnityEngine;

// #100 ULTIMATE(2026-10-04): 必殺技の間の移動。瞬間移動ではなく、地面に沿って高速で走る。
//  - 上り(段差/坂): 足元より下へは行かない(潜らない/壁に引っかからない)
//  - 下り/穴: 直前の地面の高さを保って渡り、ゆっくり下りる(穴に落ちない)
//  - 洞窟の天井: 天井の下で頭打ち(ただし足元より下へ押し込まない)。天井の針の判定はしない(発動中は守られている)
//  - 空中ルートの上にいた場合も下の地面へ(終わりに地面の上へ着地させる)
public partial class PlayerController
{
    float ultSurface;
    bool ultSurfaceKnown;
    PlayerAnimator ultAnim;

    // PlayerAnimator が見る: 構え/締めのポーズ(null=通常の絵)
    public Sprite[] UltimatePoseFrames
    {
        get
        {
            if (!UltimateArt.Driving) return null;
            if (ultAnim == null) ultAnim = GetComponentInChildren<PlayerAnimator>();
            return UltimateFx.PoseFrames(UltimateArt.Instance.Phase, UltimateArt.Instance.CharacterId, ultAnim);
        }
    }

    float UltHover => kit == CharacterKit.Mage && kitDef != null ? kitDef.mage.hoverBase : 0f;

    public void UltimateBegin()
    {
        CancelAttacksForReaction();
        knockbackTimer = 0f; knockbackVelocityX = 0f;
        moveSlowTimer = 0f;
        requestedFlick = null; touchActive = false; bufferedUpAttackTimer = 0f;
        escapeHoldTimer = 0f;
        velocityY = 0f;
        float? g = TerrainManager.Instance != null ? TerrainManager.Instance.GetHeightAt(transform.position.x) : null;
        ultSurfaceKnown = g.HasValue;
        ultSurface = g ?? (transform.position.y - groundOffset - UltHover);
        FreezeDiagnostics.NoteIntendedMove("ULTIMATE begin");
    }

    void UltimateMoveTick(float dt)
    {
        var ua = UltimateArt.Instance;
        if (ua == null) return;
        float dx = ua.DriveStep(dt); // 最後のフレーム(この中で End した時)も同じ移動を入れる
        float newX = transform.position.x + dx;
        var tm = TerrainManager.Instance;
        float? g = tm != null ? tm.GetHeightAt(newX) : null;
        if (g.HasValue) { ultSurface = g.Value; ultSurfaceKnown = true; }
        float target = ultSurface + groundOffset + UltHover;
        float y = transform.position.y;
        if (y < target) y = target;                              // 潜らない(段差は乗り上げる)
        else y = Mathf.Max(target, y - 26f * dt);                // 高い所(ジャンプ中/下り)からは滑らかに下りる
        float? lim = tm != null ? tm.GetCeilingLimitY(newX) : null;
        if (lim.HasValue && y > lim.Value) y = Mathf.Max(lim.Value, target);
        transform.position = new Vector3(newX, y, 0f);
        bool onGround = g.HasValue && y <= target + 0.05f;
        isGrounded = onGround || !g.HasValue; // 穴の上を渡っている間も「地面の上」と同じ扱い(落下の判定に入れない)
        onSky = false;
        velocityY = 0f;
        jumpsUsed = 0;
        if (onGround) UpdateSlopeTilt(newX);
        else transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.identity, 1f - Mathf.Exp(-12f * dt));
        FreezeDiagnostics.NoteIntendedMove("ULTIMATE");
    }

    // 足元に地面があって、その上に立っている
    public bool HasGroundUnderForUltimate()
    {
        var tm = TerrainManager.Instance;
        if (tm == null) return true;
        float? g = tm.GetHeightAt(transform.position.x);
        return g.HasValue && Mathf.Abs(transform.position.y - (g.Value + groundOffset + UltHover)) < 0.35f;
    }

    // 念のため: 前方の一番近い地面へ置く(発動中の落下/着地点に地面が無いまま時間が過ぎた時)
    public void UltimateRescueToGround()
    {
        var tm = TerrainManager.Instance;
        if (tm == null) return;
        float x = transform.position.x;
        for (float s = 0f; s < 120f; s += 0.5f)
        {
            float? g = tm.GetHeightAt(x + s);
            float? g2 = tm.GetHeightAt(x + s + 1.5f);
            if (g.HasValue && g2.HasValue)
            {
                FreezeDiagnostics.NoteIntendedMove("ULTIMATE rescue");
                transform.position = new Vector3(x + s + 0.75f, g.Value + groundOffset + UltHover, 0f);
                isGrounded = true; onSky = false; velocityY = 0f; jumpsUsed = 0;
                Debug.Log($"[ULTIMATE] rescued onto ground +{s:F1}m");
                return;
            }
        }
    }

    public void UltimateEnd()
    {
        var tm = TerrainManager.Instance;
        float? g = tm != null ? tm.GetHeightAt(transform.position.x) : null;
        velocityY = 0f; jumpsUsed = 0; onSky = false;
        lungeVelocityX = 0f; knockbackTimer = 0f;
        requestedFlick = null; touchActive = false; bufferedUpAttackTimer = 0f;
        isGrounded = g.HasValue;
        if (kit == CharacterKit.Mage)
        {
            mageLevel = 0; mageVelY = 0f;
            mageSurfaceKnown = g.HasValue;
            if (g.HasValue) mageSurface = g.Value;
        }
        else if (g.HasValue)
        {
            float target = g.Value + groundOffset;
            if (transform.position.y < target + 0.3f) transform.position = new Vector3(transform.position.x, target, 0f);
            else isGrounded = false; // まだ高い所: 通常の重力で着地する
        }
        transform.localScale = Vector3.one;
    }
}
