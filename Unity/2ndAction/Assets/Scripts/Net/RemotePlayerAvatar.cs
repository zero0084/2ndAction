using UnityEngine;

// マルチプレイ対応Phase 1(2026-09-25) - 相手プレイヤーの「見た目だけ」の分身。
// 自分のPlayerと同じ構造(Root → Visual(SpriteRenderer+輪郭線))を実行時に組み立て、
// PlayerAnimatorをパペットモードで動かす。PlayerController/Collider/Hitboxは持たないので、
// 敵の追跡・当たり判定・カメラ・FloatingOrigin(PlayerController.Instance基準)には一切関与しない。
// 位置は毎フレーム「論理X - この端末のFloatingOrigin.Offset」から求め直すため、シーンを
// 戻す処理(FloatingOrigin.Shift)の対象からは外している(FloatingOriginExempt)。
public class RemotePlayerAvatar : MonoBehaviour
{
    Transform visual;
    SpriteRenderer sr;
    PlayerAnimator anim;
    NetPlayer owner;

    public NetPlayer Owner => owner;
    public Vector3 HeadWorldPosition => sr != null && sr.sprite != null ? new Vector3(sr.bounds.center.x, sr.bounds.max.y, 0f) : transform.position + Vector3.up * 2f;
    public bool IsShown => sr != null && sr.enabled;

    public static RemotePlayerAvatar Create(PlayerAnimator template, CharacterDefinition def, NetPlayer owner)
    {
        SpriteRenderer templateSr = template != null ? template.VisualRenderer : null;

        var root = new GameObject($"RemotePlayer_P{owner.PlayerNumber}_Client{owner.OwnerClientId}");
        root.AddComponent<FloatingOriginExempt>();

        var visualGO = new GameObject("Visual");
        visualGO.transform.SetParent(root.transform, false);
        var sr = visualGO.AddComponent<SpriteRenderer>();
        if (templateSr != null)
        {
            visualGO.transform.localPosition = templateSr.transform.localPosition;
            visualGO.transform.localScale = templateSr.transform.localScale;
            sr.sortingLayerID = templateSr.sortingLayerID;
            // 自分のプレイヤーを常に手前に描く(相手は1段奥)。
            sr.sortingOrder = templateSr.sortingOrder - 3;
            sr.color = templateSr.color;
            sr.sprite = templateSr.sprite;
            SpriteOutline templateOutline = templateSr.GetComponent<SpriteOutline>();
            if (templateOutline != null)
            {
                SpriteOutline outline = visualGO.AddComponent<SpriteOutline>();
                outline.thickness = templateOutline.thickness;
                outline.color = templateOutline.color;
            }
        }
        sr.enabled = false; // 最初のスナップショットが届くまでは出さない

        var avatar = root.AddComponent<RemotePlayerAvatar>();
        avatar.visual = visualGO.transform;
        avatar.sr = sr;
        avatar.owner = owner;
        avatar.depthZ = 0.01f * Mathf.Clamp(owner.PlayerNumber, 1, 16); // 相手同士が重なった時の前後(番号の小さい人が手前、毎フレーム入れ替わらない)
        // PlayerAnimatorはAwakeで子のSpriteRendererを探すため、Visualを作った後に追加する。
        avatar.anim = root.AddComponent<PlayerAnimator>();
        avatar.anim.InitPuppet(template, def);
        return avatar;
    }

    public void Apply(NetPlayerSnapshot d, double logicalX, float y, float visPosX, float visPosY, float visRotZ, float visScaleX, float visScaleY, float rootRotZ)
    {
        transform.position = new Vector3((float)(logicalX - FloatingOrigin.Offset), y, depthZ);
        transform.rotation = Quaternion.Euler(0f, 0f, rootRotZ);
        transform.localScale = new Vector3(d.RootScaleX < 0f ? -1f : 1f, 1f, 1f);
        visual.localPosition = new Vector3(visPosX, visPosY, visual.localPosition.z);
        visual.localRotation = Quaternion.Euler(0f, 0f, visRotZ);
        visual.localScale = new Vector3(visScaleX, visScaleY, 1f);
        sr.color = NetPlayerSnapshot.UnpackColor(d.ColorRGBA);
        sr.enabled = (d.Flags & NetPlayerSnapshot.FlagVisible) != 0;
        anim.SetPuppetPose((PlayerAnimator.State)d.State, d.Frame, d.AttackStage, d.FinishTier);
    }

    float depthZ;

    public void SetHidden()
    {
        if (sr != null) sr.enabled = false;
    }
}
