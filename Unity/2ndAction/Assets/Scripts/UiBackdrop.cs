using UnityEngine;

// Small shared helper: a dark, semi-transparent box drawn behind HUD text so
// it stays readable over bright/busy backgrounds (sky, terrain, explosions)
// instead of blending in.
//
// Visual Style Ver.1 (navy fill + thin gold edge + white text) is baked
// straight into the reward/deck card art as textures, so this is the one
// place that treatment needs to be reproduced procedurally - every
// always-on HUD element (distance, best, hearts, Lv/EXP) goes through
// Draw() below, so giving it a gold edge here makes the whole HUD read as
// the same UI system as the cards with no changes needed at any call site.
public static class UiBackdrop
{
    static Texture2D tex;

    static readonly Color NavyFill = new Color(0.06f, 0.08f, 0.17f);
    static readonly Color GoldEdge = new Color(0.83f, 0.68f, 0.32f);

    static Texture2D Tex()
    {
        if (tex == null)
        {
            tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
        }
        return tex;
    }

    // fillAlpha keeps each call site's existing "how solid should this be"
    // tuning; the gold edge itself is a fixed thin thickness so it reads as
    // a delicate frame/decoration rather than a bulky border that would
    // make elements look bigger than before.
    //
    // Every alpha here is multiplied against the AMBIENT GUI.color.a
    // (captured as prev.a before any reassignment) rather than just set
    // outright, so a caller wrapping a call in its own
    // `GUI.color = new Color(1,1,1,fadeAlpha)` correctly fades the whole
    // element (see OrnateUi.DrawPanel's own comment for the same reasoning).
    public static void Draw(Rect rect, float alpha = 0.55f)
    {
        Color prev = GUI.color;
        float ambient = prev.a;

        GUI.color = new Color(NavyFill.r, NavyFill.g, NavyFill.b, alpha * ambient);
        GUI.DrawTexture(rect, Tex());

        const float edge = 1.5f;
        GUI.color = new Color(GoldEdge.r, GoldEdge.g, GoldEdge.b, Mathf.Min(1f, alpha + 0.35f) * ambient);
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, edge), Tex());
        GUI.DrawTexture(new Rect(rect.x, rect.yMax - edge, rect.width, edge), Tex());
        GUI.DrawTexture(new Rect(rect.x, rect.y, edge, rect.height), Tex());
        GUI.DrawTexture(new Rect(rect.xMax - edge, rect.y, edge, rect.height), Tex());

        GUI.color = prev;
    }
}
