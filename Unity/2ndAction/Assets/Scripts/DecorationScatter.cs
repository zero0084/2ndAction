using UnityEngine;

// Game Feel pass, section 17 - purely visual clutter (flowers/rocks/broken
// pillars/signposts/flags) scattered along each ground chunk's top surface
// as it's generated, to cut down on "the ground repeats itself" flatness.
// Deliberately NOT part of TerrainManager's own chunk-generation logic -
// this is called once per chunk AFTER GroundFactory.CreateSlopeVisual
// already built that chunk's real (collidable) ground, and everything it
// spawns has no Collider2D at all, so it can never affect footing, spawn
// placement, or anything else gameplay-relevant. If TerrainManager's own
// generation rules ever change, this keeps working unmodified as long as
// it's still handed a valid chunk span.
public static class DecorationScatter
{
    // One decoration slot roughly every `slotLength` world units along the
    // chunk, each independently rolling spawnChance - not a fixed count per
    // chunk, so a long flat stretch gets proportionally more props than a
    // short one instead of the same 1-2 regardless of length.
    public static void ScatterAlongChunk(Transform parent, Sprite[] decorationSprites, Vector2 a, Vector2 b, float spawnChance = 0.35f, float slotLength = 3.5f, float scaleMin = 0.7f, float scaleMax = 1.15f, float sideMargin = 0.6f)
    {
        if (decorationSprites == null || decorationSprites.Length == 0) return;

        float length = Vector2.Distance(a, b);
        int slots = Mathf.Max(1, Mathf.FloorToInt(length / slotLength));
        Vector2 dir = (b - a).normalized;
        Vector2 normal = new Vector2(-dir.y, dir.x); // perpendicular "up" along the slope

        for (int i = 0; i < slots; i++)
        {
            if (Random.value > spawnChance) continue;

            // Keep clear of both chunk edges (sideMargin) so a prop never
            // straddles a chunk boundary/cap seam.
            float t = Mathf.Lerp(sideMargin / Mathf.Max(length, 0.001f), 1f - sideMargin / Mathf.Max(length, 0.001f), (i + Random.value) / slots);
            t = Mathf.Clamp01(t);
            Vector2 basePos = Vector2.Lerp(a, b, t);

            Sprite sprite = decorationSprites[Random.Range(0, decorationSprites.Length)];
            if (sprite == null) continue;

            GameObject go = new GameObject("Decor_" + sprite.name);
            go.transform.SetParent(parent, false);

            // Sits ON the surface (sprite's own bottom-pivot convention:
            // decoration art extracted for this pass has its natural base
            // near the bottom of its transparent bounding box, same as
            // every foot-pivoted character sprite in this project) with a
            // tiny fixed lift so it doesn't z-fight the ground tile itself.
            Vector3 pos = new Vector3(basePos.x, basePos.y, 0f) + (Vector3)(normal * 0.02f);
            go.transform.position = pos;

            float scale = Random.Range(scaleMin, scaleMax);
            bool flip = Random.value < 0.5f;
            go.transform.localScale = new Vector3(flip ? -scale : scale, scale, 1f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            // Same layer as the ground tiles themselves (0) plus a hair
            // above it - reads as sitting on the ground, but never in front
            // of the player/enemies/effects (all sortingOrder >= 1 - see
            // RenderOrder).
            sr.sortingOrder = RenderOrder.Ground;
        }
    }
}
