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
public class Billboard : MonoBehaviour
{
    public Camera targetCamera;

    void LateUpdate()
    {
        if (targetCamera == null || !targetCamera.enabled) return;
        transform.rotation = targetCamera.transform.rotation;
    }
}
