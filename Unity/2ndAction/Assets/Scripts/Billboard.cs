using UnityEngine;

// Vertical Mode Prototype (2026-09-08) - "縦画面対応・斜め上視点" brief.
// Keeps a flat 2D sprite reading as an upright, side-on character/enemy
// even while PortraitCameraRig's Perspective camera views the world from an
// oblique angle instead of the original orthographic head-on view (see that
// class's own comment for the full reasoning) - without this, a sprite
// quad that stays fixed in the world's X/Y plane would appear squashed/
// foreshortened once viewed at an angle, like a decal glued flat to the
// ground rather than a standing character.
//
// Deliberately does nothing at all while targetCamera is null or disabled -
// this is exactly what keeps the existing Landscape build's visuals
// completely untouched (that camera is Orthographic and never enables this
// component's target), matching the brief's "横画面版を壊さず" requirement.
// Attached to the "Visual" child only (Player/ground-type Enemy), never to
// a Root that also carries a Collider2D/Rigidbody2D - rotating a 2D
// physics body in 3D would tip its collider out of the X/Y plane 2D physics
// assumes it lives in. Bosses (Dragon/Majin/MechanicalDragon) have no such
// Root/Visual split today (SpriteRenderer/Collider share one Transform),
// so this prototype deliberately does NOT billboard them - see the brief
// report for why that's flagged as a found limitation rather than worked
// around here.
//
// 2026-10-11(実機の不具合「疾走の後にキャラが細く小さくなる」): 斜めのカメラが止まった時(縦→横の回転、表示方式の変更)に
// 何もしないと、最後のカメラの向き(横へ約77°)がそのまま残り、横の画面でキャラ(と残像)が横につぶれて見えた。
// 戻るのは被弾などで姿勢が入れ直された時だけ(毎回は起きない理由)。カメラが止まったら元の向きへ戻す。
public class Billboard : MonoBehaviour
{
    public Camera targetCamera;
    public static bool QaDisableRestore; // 開発の確認用: 直す前の動き(向きが残る)を再現する
    Quaternion baseLocalRotation;
    bool facing; // 今カメラの向きに合わせている

    void Awake() { baseLocalRotation = transform.localRotation; }

    void LateUpdate()
    {
        if (targetCamera == null || !targetCamera.enabled)
        {
            if (facing && !QaDisableRestore) { transform.localRotation = baseLocalRotation; facing = false; }
            return;
        }
        transform.rotation = targetCamera.transform.rotation;
        facing = true;
    }

    void OnDisable()
    {
        if (facing) { transform.localRotation = baseLocalRotation; facing = false; }
    }
}
